#define USE_METADATA_FRAMEID

using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using Unity.WebRTC;
using Unity.WebRTC.Samples;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;

class PeerConnectionSample : MonoBehaviour
{
#pragma warning disable 0649
    [SerializeField] private Button startButton;
    [SerializeField] private Button callButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button hangUpButton;
    [SerializeField] private Text localCandidateId;
    [SerializeField] private Text remoteCandidateId;
    [SerializeField] private Dropdown dropDownProtocol;
    [SerializeField] private Toggle shouldEncodeFrameData;
    [SerializeField] private Text localEncodedFrameData;
    [SerializeField] private Text remoteEncodedFrameData;

    [SerializeField] private Camera cam;
    [SerializeField] private RawImage sourceImage;
    [SerializeField] private RawImage receiveImage;
    [SerializeField] private Transform rotateObject;
#pragma warning restore 0649

    enum ProtocolOption
    {
        Default,
        UDP,
        TCP
    }

    private RTCPeerConnection _pc1, _pc2;
    private List<RTCRtpSender> pc1Senders;
    private List<RTCRtpReceiver> pc2Receivers;
    private MediaStream videoStream, receiveStream;
    private DelegateOnIceConnectionChange pc1OnIceConnectionChange;
    private DelegateOnIceConnectionChange pc2OnIceConnectionChange;
    private DelegateOnIceCandidate pc1OnIceCandidate;
    private DelegateOnIceCandidate pc2OnIceCandidate;
    private DelegateOnTrack pc2Ontrack;
    private DelegateOnNegotiationNeeded pc1OnNegotiationNeeded;
    private bool videoUpdateStarted;
    private long pc1EncodedDataToSend = -1;
    private long pc2EncodedFrameDataReceived = -1;
    private bool pc1ShouldEncodeFrameData;
    private NativeArray<byte> pc1ScratchBuffer;

    private const int width = 1280;
    private const int height = 720;

    private void Awake()
    {
        Debug.Log($"WebRTCSettings.EncoderType = {WebRTCSettings.EncoderType}");
        WebRTC.Initialize(WebRTCSettings.EncoderType, WebRTCSettings.LimitTextureSize);
        startButton.onClick.AddListener(OnStart);
        callButton.onClick.AddListener(Call);
        restartButton.onClick.AddListener(RestartIce);
        hangUpButton.onClick.AddListener(HangUp);
        receiveStream = new MediaStream();

        pc1ShouldEncodeFrameData = shouldEncodeFrameData.isOn;
        shouldEncodeFrameData.onValueChanged.AddListener(OnShouldEncodeFrameDataToggled);
    }

    private void OnDestroy()
    {
        WebRTC.Dispose();

        if (pc1ScratchBuffer.IsCreated)
            pc1ScratchBuffer.Dispose();
    }

    private void Start()
    {
        pc1Senders = new List<RTCRtpSender>();
        pc2Receivers = new List<RTCRtpReceiver>();
        callButton.interactable = false;
        restartButton.interactable = false;
        hangUpButton.interactable = false;

        pc1OnIceConnectionChange = state => { OnIceConnectionChange(_pc1, state); };
        pc2OnIceConnectionChange = state => { OnIceConnectionChange(_pc2, state); };
        pc1OnIceCandidate = candidate => { OnIceCandidate(_pc1, candidate); };
        pc2OnIceCandidate = candidate => { OnIceCandidate(_pc2, candidate); };
        pc2Ontrack = e =>
        {
            receiveStream.AddTrack(e.Track);
            pc2Receivers.Add(e.Receiver);

            e.Receiver.Transform = new RTCRtpScriptTransform(TrackKind.Video, HandleReceiverTransformEvent);;
        };
        pc1OnNegotiationNeeded = () => { StartCoroutine(PeerNegotiationNeeded(_pc1)); };

        receiveStream.OnAddTrack = e =>
        {
            if (e.Track is VideoStreamTrack track)
            {
                track.OnVideoReceived += tex =>
                {
                    receiveImage.texture = tex;
                    receiveImage.color = Color.white;
                };
            }
        };
    }

    private void HandleReceiverTransformEvent(RTCTransformEvent ev)
    {
#if USE_METADATA_FRAMEID
        var frame = (RTCEncodedVideoFrame)(ev.Frame);
        var metadata = frame.GetMetadata();
        if (!metadata.frameId.HasValue)
            Debug.Log($"Receiving No frame Frameid");
        else
        {
            Debug.Log($"Receiving frame {metadata.frameId.Value}");
            pc2EncodedFrameDataReceived = metadata.frameId.Value;
        }
#else
        if (pc1ShouldEncodeFrameData)
        {
            var frame = (RTCEncodedVideoFrame)(ev.Frame);
            var data = frame.GetData();

            // The last 4 bytes contain the injected frame data.
            var start = data.Length - sizeof(int);
            pc2EncodedFrameDataReceived = data[start];
            for (var i = 1; i < sizeof(int); ++i)
            {
                pc2EncodedFrameDataReceived += (data[start + i] << (i * 8));
            }
        }
#endif
    }

    private void OnStart()
    {
        startButton.interactable = false;
        callButton.interactable = true;

        if (videoStream == null)
        {
            videoStream = cam.CaptureStream(width, height, 1000000);
        }

        sourceImage.texture = cam.targetTexture;
        sourceImage.color = Color.white;
    }

    private void Update()
    {
        if (rotateObject != null)
        {
            float t = Time.deltaTime;
            rotateObject.Rotate(100 * t, 200 * t, 300 * t);
        }

        if (pc1EncodedDataToSend != -1)
            localEncodedFrameData.text = pc1EncodedDataToSend.ToString();

        if (pc2EncodedFrameDataReceived != -1)
            remoteEncodedFrameData.text = pc2EncodedFrameDataReceived.ToString();
    }

    private static RTCConfiguration GetSelectedSdpSemantics()
    {
        RTCConfiguration config = default;
        config.iceServers = new[] {new RTCIceServer {urls = new[] {"stun:stun.l.google.com:19302"}}};

        return config;
    }

    private void OnIceConnectionChange(RTCPeerConnection pc, RTCIceConnectionState state)
    {
        Debug.Log($"{GetName(pc)} IceConnectionState: {state}");

        if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
        {
            StartCoroutine(CheckStats(pc));
        }
    }

    // Display the video codec that is actually used.
    IEnumerator CheckStats(RTCPeerConnection pc)
    {
        yield return new WaitForSeconds(0.1f);
        if (pc == null)
            yield break;

        var op = pc.GetStats();
        yield return op;
        if (op.IsError)
        {
            Debug.LogErrorFormat("RTCPeerConnection.GetStats failed: {0}", op.Error);
            yield break;
        }

        RTCStatsReport report = op.Value;
        RTCIceCandidatePairStats activeCandidatePairStats = null;
        RTCIceCandidateStats remoteCandidateStats = null;

        foreach (var transportStatus in report.Stats.Values.OfType<RTCTransportStats>())
        {
            if (report.Stats.TryGetValue(transportStatus.selectedCandidatePairId, out var tmp))
            {
                activeCandidatePairStats = tmp as RTCIceCandidatePairStats;
            }
        }

        if (activeCandidatePairStats == null || string.IsNullOrEmpty(activeCandidatePairStats.remoteCandidateId))
        {
            yield break;
        }

        foreach (var iceCandidateStatus in report.Stats.Values.OfType<RTCIceCandidateStats>())
        {
            if (iceCandidateStatus.Id == activeCandidatePairStats.remoteCandidateId)
            {
                remoteCandidateStats = iceCandidateStatus;
            }
        }

        if (remoteCandidateStats == null || string.IsNullOrEmpty(remoteCandidateStats.Id))
        {
            yield break;
        }

        Debug.Log($"{GetName(pc)} candidate stats Id:{remoteCandidateStats.Id}, Type:{remoteCandidateStats.candidateType}");
        var updateText = GetName(pc) == "pc1" ? localCandidateId : remoteCandidateId;
        updateText.text = remoteCandidateStats.Id;
    }

    IEnumerator PeerNegotiationNeeded(RTCPeerConnection pc)
    {
        var op = pc.CreateOffer();
        yield return op;

        if (!op.IsError)
        {
            if (pc.SignalingState != RTCSignalingState.Stable)
            {
                Debug.LogError($"{GetName(pc)} signaling state is not stable.");
                yield break;
            }

            yield return StartCoroutine(OnCreateOfferSuccess(pc, op.Desc));
        }
        else
        {
            OnCreateSessionDescriptionError(op.Error);
        }
    }

    private void AddTracks()
    {
        foreach (var track in videoStream.GetTracks())
        {
            var sender = _pc1.AddTrack(track, videoStream);
            pc1Senders.Add(sender);

            sender.Transform = new RTCRtpScriptTransform(TrackKind.Video, HandleSenderTransformEvent);
        }

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }
    }

    private void HandleSenderTransformEvent(RTCTransformEvent ev)
    {
#if USE_METADATA_FRAMEID
        var videoFrame = (RTCEncodedVideoFrame)(ev.Frame);
        var videoFrameMetadata = videoFrame.GetMetadata();
        if (!videoFrameMetadata.frameId.HasValue)
            throw new DataException("Frame ID doesnt have a value");

        Debug.Log($"Sending frame {videoFrameMetadata.frameId.Value}");
        pc1EncodedDataToSend = videoFrameMetadata.frameId.Value;
#else
        if (pc1ShouldEncodeFrameData)
        {
            ++pc1EncodedDataToSend;

            const int kExtraMemoryToAllocate = 1024;
            var videoFrame = (RTCEncodedVideoFrame)(ev.Frame);
            var videoFramePayload = videoFrame.GetData();
            var finalPayloadSize = videoFramePayload.Length + sizeof(int);
            if (!pc1ScratchBuffer.IsCreated || pc1ScratchBuffer.Length < finalPayloadSize)
            {
                if (pc1ScratchBuffer.IsCreated)
                {
                    pc1ScratchBuffer.Dispose();
                }
                pc1ScratchBuffer = new NativeArray<byte>(finalPayloadSize + kExtraMemoryToAllocate, Allocator.Persistent);
            }

            NativeArray<byte>.Copy(videoFramePayload, pc1ScratchBuffer, videoFramePayload.Length);

            // Append the integer value to the end of the video payload
            for (var i = 0; i < sizeof(int); ++i)
            {
                pc1ScratchBuffer[videoFramePayload.Length + i] = (byte)((pc1EncodedDataToSend >> (i * 8)) & 0xff);
            }

            videoFrame.SetData(pc1ScratchBuffer.GetSubArray(0, finalPayloadSize).AsReadOnly());
        }
#endif
    }

    private void OnShouldEncodeFrameDataToggled(bool isOn)
    {
        pc1ShouldEncodeFrameData = isOn;
    }

    private void RemoveTracks()
    {
        foreach (var sender in pc1Senders)
        {
            _pc1.RemoveTrack(sender);
        }

        pc1Senders.Clear();

        MediaStreamTrack[] tracks = receiveStream.GetTracks().ToArray();
        foreach (var track in tracks)
        {
            receiveStream.RemoveTrack(track);
            track.Dispose();
        }
    }

    private void Call()
    {
        callButton.interactable = false;
        hangUpButton.interactable = true;
        restartButton.interactable = true;

        var configuration = GetSelectedSdpSemantics();
        _pc1 = new RTCPeerConnection(ref configuration);
        _pc1.OnIceCandidate = pc1OnIceCandidate;
        _pc1.OnIceConnectionChange = pc1OnIceConnectionChange;
        _pc1.OnNegotiationNeeded = pc1OnNegotiationNeeded;
        _pc2 = new RTCPeerConnection(ref configuration);
        _pc2.OnIceCandidate = pc2OnIceCandidate;
        _pc2.OnIceConnectionChange = pc2OnIceConnectionChange;
        _pc2.OnTrack = pc2Ontrack;

        AddTracks();
    }

    private void RestartIce()
    {
        restartButton.interactable = false;

        _pc1.RestartIce();
    }

    private void HangUp()
    {
        RemoveTracks();

        _pc1.Close();
        _pc2.Close();
        _pc1.Dispose();
        _pc2.Dispose();
        _pc1 = null;
        _pc2 = null;

        callButton.interactable = true;
        restartButton.interactable = false;
        hangUpButton.interactable = false;

        receiveImage.color = Color.black;
    }

    private void OnIceCandidate(RTCPeerConnection pc, RTCIceCandidate candidate)
    {
        switch((ProtocolOption)dropDownProtocol.value)
        {
            case ProtocolOption.Default:
                break;
            case ProtocolOption.UDP:
                if (candidate.Protocol != RTCIceProtocol.Udp)
                    return;
                break;
            case ProtocolOption.TCP:
                if (candidate.Protocol != RTCIceProtocol.Tcp)
                    return;
                break;
        }

        GetOtherPc(pc).AddIceCandidate(candidate);
        Debug.Log($"{GetName(pc)} ICE candidate:\n {candidate.Candidate}");
    }

    private string GetName(RTCPeerConnection pc)
    {
        return (pc == _pc1) ? "pc1" : "pc2";
    }

    private RTCPeerConnection GetOtherPc(RTCPeerConnection pc)
    {
        return (pc == _pc1) ? _pc2 : _pc1;
    }

    private IEnumerator OnCreateOfferSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Offer from {GetName(pc)}\n{desc.sdp}");
        Debug.Log($"{GetName(pc)} setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        var otherPc = GetOtherPc(pc);
        Debug.Log($"{GetName(otherPc)} setRemoteDescription start");
        var op2 = otherPc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        Debug.Log($"{GetName(otherPc)} createAnswer start");
        // Since the 'remote' side has no media stream we need
        // to pass in the right constraints in order for it to
        // accept the incoming offer of audio and video.

        var op3 = otherPc.CreateAnswer();
        yield return op3;
        if (!op3.IsError)
        {
            yield return OnCreateAnswerSuccess(otherPc, op3.Desc);
        }
        else
        {
            OnCreateSessionDescriptionError(op3.Error);
        }
    }

    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"{GetName(pc)} SetLocalDescription complete");
    }

    void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
        HangUp();
    }

    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"{GetName(pc)} SetRemoteDescription complete");
    }

    IEnumerator OnCreateAnswerSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Answer from {GetName(pc)}:\n{desc.sdp}");
        Debug.Log($"{GetName(pc)} setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        var otherPc = GetOtherPc(pc);
        Debug.Log($"{GetName(otherPc)} setRemoteDescription start");

        var op2 = otherPc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
        }
    }

    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }
}

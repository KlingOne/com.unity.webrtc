using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Unity.WebRTC
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="renderer"></param>
    public delegate void OnVideoReceived(Texture renderer);

    /// <summary>
    ///
    /// </summary>
    public class VideoStreamTrack : MediaStreamTrack
    {
        internal static ConcurrentDictionary<IntPtr, WeakReference<VideoStreamTrack>> s_tracks =
            new ConcurrentDictionary<IntPtr, WeakReference<VideoStreamTrack>>();

        bool m_needFlip = false;
        Texture m_sourceTexture;
#if !UNITY_WEBGL
        RenderTexture m_destTexture;
#else
        Texture m_destTexture;
#endif

#if !UNITY_WEBGL
        UnityVideoRenderer m_renderer;
        VideoTrackSource m_source;
#else
        public bool IsRemote { get; private set; }
#endif

        private static RenderTexture CreateRenderTexture(int width, int height)
        {
            var format = WebRTC.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
            var tex = new RenderTexture(width, height, 0, format);
            tex.Create();
            return tex;
        }

        internal VideoStreamTrack(Texture source, RenderTexture dest, int width, int height)
#if !UNITY_WEBGL
            : this(dest.GetNativeTexturePtr(), width, height, source.graphicsFormat)
#else
            : this(source.GetNativeTexturePtr(), dest.GetNativeTexturePtr(), width, height)
#endif
        {
            m_needFlip = true;
            m_sourceTexture = source;
            m_destTexture = dest;
        }

        /// <summary>
        /// note:
        /// The videotrack cannot be used if the encoder has not been initialized.
        /// Do not use it until the initialization is complete.
        /// </summary>
        [Obsolete("Remove this for next version")]
        public bool IsEncoderInitialized
        {
            get
            {
#if !UNITY_WEBGL
                return WebRTC.Context.GetInitializationResult(GetSelfOrThrow()) == CodecInitializationResult.Success;
#else
                return !IsRemote;
#endif
            }
        }

         public bool IsDecoderInitialized
        {
            get
            {
#if !UNITY_WEBGL
                return m_renderer != null && m_renderer.self != IntPtr.Zero;
#else
                return IsRemote;
#endif
            }
        }

        /// <summary>
        /// encoded / decoded texture
        /// </summary>
        public Texture Texture
        {
            get
            {
                if (m_renderer != null)
                    return m_renderer.Texture;
                return m_destTexture;
            }
        }

        public Texture InitializeReceiver(int width, int height)
        {
#if !UNITY_WEBGL
            if (IsDecoderInitialized)
                throw new InvalidOperationException("Already initialized receiver, use Texture property");
#endif

            m_needFlip = true;
            var format = WebRTC.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
            m_sourceTexture = new Texture2D(width, height, format, TextureCreationFlags.None);
            //m_destTexture = CreateRenderTexture(m_sourceTexture.width, m_sourceTexture.height);
#if !UNITY_WEBGL
            //m_sourceTexture = new Texture2D(width, height, format, TextureCreationFlags.None);
            m_destTexture = CreateRenderTexture(m_sourceTexture.width, m_sourceTexture.height);
            m_renderer = new UnityVideoRenderer(WebRTC.Context.CreateVideoRenderer(), this);
#else
            //m_destTexture = CreateRenderTexture(width, height, renderTextureFormat);
            var texPtr = NativeMethods.CreateNativeTexture();
            var tex = Texture2D.CreateExternalTexture(width, height, TextureFormat.RGBA32, false, false, texPtr);
            tex.UpdateExternalTexture(texPtr);
            m_destTexture = tex;
            IsRemote = true;
#endif


            return m_destTexture;
        }

        /// <
        /// <summary>
        ///
        /// </summary>: 2020-03-09] Flip vertical /// <summary>
        ///
        /// </summary>
        public event OnVideoReceived OnVideoReceived;

        internal void UpdateReceiveTexture()
        {
#if !UNITY_WEBGL
            m_renderer?.U#else
            NativeMethods.UpdateRendererTexture(self, m_destTexture.GetNativeTexturePtr(), m_needFlip);
#endif void UpdateSendTexture()
        {
#if !UNITY_WEBGL#if !UNITY_WEBGL
            if (m_source == null)
                return;
            // [Note-kazuki: 2020-03-09] Flip vertically RenderTexture
            // note: streamed video is flipped vertical if no action was taken:
            //  - duplicate RenderTexture from its source texture
            //  - call Graphics.Blit command with flip material every frame
            //  - it might be better to implement this if possible
            if (m_needFlip)
            {
                Graphics.Blit(m_sourceTexture, m_destTexture, WebRTC.flipMat);
            }
            else
            {
                Graphics.Blit(m_sourceTexture, m_destTexture);
            }

            WebRTC.Context.Encode(GetSelfOrThrow());

#else
            NativeMethods.RenderLocalVideotrack(GetSelfOrThrow(), m_needFlip);
#endif        }ly Rend#elser, dstTexturePtr        /// <summary>
        /// Creates a new VideoStream object.
        /// The track is created with a `source`.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="needFlip"></param>
        public VideoStreamTrack(Texture source, bool needFlip = true)
            : this(source,
                CreateRenderTexture(source.width, source.height),
                source.width,
                source.height,
                needFlip)
        {
        }
#if !UNITY
_WEBGL
        internal VideoStreamTrack(Texture texture, RenderTexture dest, int width, int height, bool needFlip)
            : this(dest.GetNativeTexturePtr(), width, height, texture.graphicsFormat, needFlip)
        {
            m_sourceTexture = texture;
            m_destTexture = dest;
        }

        /// <summary>
        /// Creates a new VideoStream object.
        /// The track is created with a source texture `ptr`.
        /// It is noted that streamed video might be flipped when not action was taken. Almost case it has no problem to use other constructor instead.
        ///
        /// See Also: Texture.GetNativeTexturePtr
        /// </summary>
        /// <param name="texturePtr"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="format"></param>
        public VideoStreamTrack(IntPtr texturePtr, int width, int height, GraphicsFormat format, bool needFlip)
            : this(Guid.NewGuid().ToString(), new VideoTrackSource(), needFlip)
        {
            var error = WebRTC.ValidateTextureSize(width, height, Application.platform, WebRTC.GetEncoderType());
            if (error.errorType != RTCErrorType.None)
            {
                throw new ArgumentException(error.message);
            }
            WebRTC.ValidateGraphicsFormat(format);
            WebRTC.Context.SetVideoEncoderParameter(GetSelfOrThrow(), width, height, format, texturePtr);
            WebRTC.Context.InitializeEncoder(GetSelfOrThrow());
        }

        /// <summary>
        /// Video Sender
        /// </summary>
        /// <param name="label"></param>
        /// <param name="source"></param>
        /// <param name="needFlip"></param>
        internal VideoStreamTrack(string label, VideoTrackSource source, bool needFlip)
            : base(WebRTC.Context.CreateVideoTrack(label, source.self))
        {
            if (!s_tracks.TryAdd(self, new WeakReference<VideoStreamTrack>(this)))
                throw new InvalidOperationException();

            m_needFlip = needFlip;
            m_source = source;
        }

    
    #else
        /// <summary>
        /// Creates a new VideoStream object.
        /// The track is created with a source texture `ptr`.
        /// It is noted that streamed video might be flipped when not action was taken. Almost case it has no problem to use other constructor instead.
        ///
        /// See Also: Texture.GetNativeTexturePtr
        /// </summary>
        /// <param name="srcTexturePtr"></param>
        /// <param name="dstTexturePtr"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public VideoStreamTrack(IntPtr srcTexturePtr, IntPtr dstTexturePtr, int width, int height)
            : base(WebRTC.Context.CreateVideoTrack(srcTexturePtr, dstTexturePtr, width, height))
        {
            if (!s_tracks.TryAdd(self, new WeakReference<VideoStreamTrack>(this)))
                throw new InvalidOperationException();
        }
#endif/// <summary>
        /// Video Receiver
        /// </summary>
        /// <param name="ptr"></param>
        internal VideoStreamTrack(IntPtr ptr) : base(ptr)
        {
            if (!s_tracks.TryAdd(self, new WeakReference<VideoStreamTrack>(this)))
                throw new InvalidOperationException();

            m_renderer = new UnityVideoRenderer(this, true);
        }, width, height))
        {
        {

            m_needFlip = needFlip;
            m_source = source;
        }
#endif
        /// <summary>
        /// Creates from MediaStreamTrack object
        /// </summary>Lre);                if (m_source != null)
                {
                    WebRTC.Context.FinalizeEncoder(self);
                    if (RenderTexture.active == m_destTexture)
                        RenderTexture.active = null;
                }

                m_sourceTexture = null;
                // Unity API must be called from main thread.
                WebRTC.DestroyOnMainThread(m_destTexture);

                m_renderer?.Dispose();
                m_source?.Dispose();

                s_tracks.TryRemove(self, out var value);

m_sourceTexture = 					null;
            m_renderer = new UnityVideoRenderer(this, true);
        }

        public override void Dispose()
        {
            if (this.disposed)
            {
                return;
}				            }             WebRTC.DestroyOnMainThread(m_destTexture);
                }

                _source?.Dispose();
                }

                _source?.Dispose();
                m_renderer?.Dispose();
                m_source?.Dispose();
                    // Unity API must be called from main thread.
                    WebRTC.DestroyOnMainThread(m_destTexture);
                }

                _source?.Dispose();
#endif



                s_tracks.TryRemove(self, out var value);
            }
            base.Dispose();
        }

        internal void OnVideoFrameResize(Texture texture)
        {
            OnVideoReceived?.Invoke(texture);
        }
    }

    public static class CameraExtension
    {
        public static VideoStreamTrack CaptureStreamTrack(this Camera cam, int width, int height, int bitrate,
            RenderTextureDepth depth = RenderTextureDepth.DEPTH_24, bool needFlip = true)
        {
            switch (depth)
            {
                case RenderTextureDepth.DEPTH_16:
                case RenderTextureDepth.DEPTH_24:
                case RenderTextureDepth.DEPTH_32:
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(depth), (int)depth, typeof(RenderTextureDepth));
            }

            if (width == 0 || height == 0)
            {
                throw new ArgumentException("width and height are should be greater than zero.");
            }

            int depthValue = (int)depth;
            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            var rt = new UnityEngine.RenderTexture(width, height, depthValue, format);
            rt.Create();
            cam.targetTexture = rt;
            return new VideoStreamTrack(rt, needFlip);
        }


        public static MediaStream CaptureStream(this Camera cam, int width, int height, int bitrate,
            RenderTextureDepth depth = RenderTextureDepth.DEPTH_24)
        {
            var stream = new MediaStream();
            var track = cam.CaptureStreamTrack(width, height, bitrate, depth);
            stream.AddTrack(track);
            return stream;
        }
    }

    internal class VideoTrackSource : RefCountedObject
    {
        public VideoTrackSource() : base(WebRTC.Context.CreateVideoTrackSource())
        {
            WebRTC.Table.Add(self, this);
        }

        ~VideoTrackSource()
        {
            this.Dispose();
        }

        public override void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            if (self != IntPtr.Zero && !WebRTC.Context.IsNull)
            {
                WebRTC.Table.Remove(self);
            }
            base.Dispose();
        }
    }

    internal class UnityVideoRenderer : IDisposable
    {
        internal IntPtr self;
        private VideoStreamTrack track;

        internal uint id => NativeMethods.GetVideoRendererId(self);
        private bool disposed;

        public Texture Texture { get; private set; }

        public UnityVideoRenderer(VideoStreamTrack track, bool needFlip)
        {
            self = WebRTC.Context.CreateVideoRenderer(OnVideoFrameResize, needFlip);
            this.track = track;
            NativeMethods.VideoTrackAddOrUpdateSink(track.GetSelfOrThrow(), self);
            WebRTC.Table.Add(self, this);
        }

        public void Update()
        {
            if (Texture == null)
                return;
            WebRTC.Context.UpdateRendererTexture(id, Texture);
        }

        ~UnityVideoRenderer()
        {
            this.Dispose();
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            if (self != IntPtr.Zero)
            {
                IntPtr trackPtr = track.GetSelfOrThrow();
                if (trackPtr != IntPtr.Zero)
                {
                    NativeMethods.VideoTrackRemoveSink(trackPtr, self);
                }
                WebRTC.DestroyOnMainThread(Texture);
                WebRTC.Context.DeleteVideoRenderer(self);
                WebRTC.Table.Remove(self);
                self = IntPtr.Zero;
            }

            this.disposed = true;
            GC.SuppressFinalize(this);
        }

        private void OnVideoFrameResizeInternal(int width, int height)
        {
            if (Texture != null &&
                Texture.width == width &&
                Texture.height == height)
            {
                return;
            }

            if (Texture != null)
            {
                WebRTC.DestroyOnMainThread(Texture);
                Texture = null;
            }

            var format = WebRTC.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
            Texture = new Texture2D(width, height, format, TextureCreationFlags.None);
            track.OnVideoFrameResize(Texture);
        }

        [AOT.MonoPInvokeCallback(typeof(DelegateVideoFrameResize))]
        static void OnVideoFrameResize(IntPtr ptrRenderer, int width, int height)
        {
            WebRTC.Sync(ptrRenderer, () =>
            {
                if (WebRTC.Table[ptrRenderer] is UnityVideoRenderer renderer)
                {
                    renderer.OnVideoFrameResizeInternal(width, height);
                }
            });
        }
    }
}

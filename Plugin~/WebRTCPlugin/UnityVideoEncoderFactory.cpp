#include "pch.h"

#include <media/engine/internal_encoder_factory.h>
#include <modules/video_coding/include/video_error_codes.h>

#include "GraphicsDevice/GraphicsUtility.h"
#include "ProfilerMarkerFactory.h"
#include "ScopedProfiler.h"
#include "UnityVideoEncoderFactory.h"

#if CUDA_PLATFORM
#include "Codec/NvCodec/NvCodec.h"
#endif

#if UNITY_OSX || UNITY_IOS
#import <sdk/objc/components/video_codec/RTCDefaultVideoEncoderFactory.h>
#import <sdk/objc/native/api/video_encoder_factory.h>
#elif UNITY_ANDROID
#include "Android/AndroidCodecFactoryHelper.h"
#include "Android/Jni.h"
#endif

namespace unity
{
namespace webrtc
{
    class UnityVideoEncoder : public VideoEncoder
    {
    public:
        UnityVideoEncoder(std::unique_ptr<VideoEncoder> encoder, ProfilerMarkerFactory* profiler)
            : encoder_(std::move(encoder))
            , profiler_(profiler)
            , marker_(nullptr)
            , profilerThread_(nullptr)
        {
            if (profiler)
                marker_ = profiler->CreateMarker(
                    "UnityVideoEncoder.Encode", kUnityProfilerCategoryOther, kUnityProfilerMarkerFlagDefault, 0);
        }
        ~UnityVideoEncoder() override { }

        void SetFecControllerOverride(FecControllerOverride* fec_controller_override) override
        {
            encoder_->SetFecControllerOverride(fec_controller_override);
        }
        int32_t InitEncode(const VideoCodec* codec_settings, int32_t number_of_cores, size_t max_payload_size) override
        {
            int32_t result = encoder_->InitEncode(codec_settings, number_of_cores, max_payload_size);
            if (result >= WEBRTC_VIDEO_CODEC_OK && !profilerThread_)
            {
                std::stringstream ss;
                ss << "Encoder ";
                ss
                    << (encoder_->GetEncoderInfo().implementation_name.empty()
                            ? "VideoEncoder"
                            : encoder_->GetEncoderInfo().implementation_name);
                ss << "(" << CodecTypeToPayloadString(codec_settings->codecType) << ")";
                profilerThread_ = profiler_->CreateScopedProfilerThread("WebRTC", ss.str().c_str());
            }

            return result;
        }
        int InitEncode(const VideoCodec* codec_settings, const VideoEncoder::Settings& settings) override
        {
            int result = encoder_->InitEncode(codec_settings, settings);
            if (result >= WEBRTC_VIDEO_CODEC_OK && !profilerThread_)
            {
                std::stringstream ss;
                ss << "Encoder ";
                ss
                    << (encoder_->GetEncoderInfo().implementation_name.empty()
                            ? "VideoEncoder"
                            : encoder_->GetEncoderInfo().implementation_name);
                ss << "(" << CodecTypeToPayloadString(codec_settings->codecType) << ")";
                profilerThread_ = profiler_->CreateScopedProfilerThread("WebRTC", ss.str().c_str());
            }

            return result;
        }
        int32_t RegisterEncodeCompleteCallback(EncodedImageCallback* callback) override
        {
            return encoder_->RegisterEncodeCompleteCallback(callback);
        }
        int32_t Release() override { return encoder_->Release(); }
        int32_t Encode(const VideoFrame& frame, const std::vector<VideoFrameType>* frame_types) override
        {
            int32_t result;
            {
                std::unique_ptr<const ScopedProfiler> profiler;
                if (profiler_)
                    profiler = profiler_->CreateScopedProfiler(*marker_);
                result = encoder_->Encode(frame, frame_types);
            }
            return result;
        }
        void SetRates(const RateControlParameters& parameters) override { encoder_->SetRates(parameters); }
        void OnPacketLossRateUpdate(float packet_loss_rate) override
        {
            encoder_->OnPacketLossRateUpdate(packet_loss_rate);
        }
        void OnRttUpdate(int64_t rtt_ms) override { encoder_->OnRttUpdate(rtt_ms); }
        void OnLossNotification(const LossNotification& loss_notification) override
        {
            encoder_->OnLossNotification(loss_notification);
        }
        EncoderInfo GetEncoderInfo() const override { return encoder_->GetEncoderInfo(); }

    private:
        std::unique_ptr<VideoEncoder> encoder_;
        ProfilerMarkerFactory* profiler_;
        const UnityProfilerMarkerDesc* marker_;
        std::unique_ptr<const ScopedProfilerThread> profilerThread_;
    };

    static webrtc::VideoEncoderFactory* CreateNativeEncoderFactory(IGraphicsDevice* gfxDevice, ProfilerMarkerFactory* profiler)
    {
#if UNITY_OSX || UNITY_IOS
        return webrtc::ObjCToNativeVideoEncoderFactory([[RTCDefaultVideoEncoderFactory alloc] init]).release();
#elif UNITY_ANDROID
        if (IsVMInitialized())
            return CreateAndroidEncoderFactory().release();
#elif CUDA_PLATFORM
        if (gfxDevice->IsCudaSupport() && NvEncoder::IsSupported())
        {
            CUcontext context = gfxDevice->GetCUcontext();
            NV_ENC_BUFFER_FORMAT format = gfxDevice->GetEncodeBufferFormat();
            return new NvEncoderFactory(context, format, profiler);
        }
#endif
        return nullptr;
    }

    UnityVideoEncoderFactory::UnityVideoEncoderFactory(IGraphicsDevice* gfxDevice, ProfilerMarkerFactory* profiler)
        : profiler_(profiler)
        , internal_encoder_factory_(new webrtc::InternalEncoderFactory())
        , native_encoder_factory_(CreateNativeEncoderFactory(gfxDevice, profiler))
    {
    }

    UnityVideoEncoderFactory::~UnityVideoEncoderFactory() = default;

    std::vector<webrtc::SdpVideoFormat> UnityVideoEncoderFactory::GetSupportedFormats() const
    {
        std::vector<SdpVideoFormat> supported_codecs;

        for (const webrtc::SdpVideoFormat& format : internal_encoder_factory_->GetSupportedFormats())
            supported_codecs.push_back(format);
        if (native_encoder_factory_)
        {
            for (const webrtc::SdpVideoFormat& format : native_encoder_factory_->GetSupportedFormats())
                supported_codecs.push_back(format);
        }

        // Set video codec order: default video codec is VP8
        auto findIndex = [&](webrtc::SdpVideoFormat& format) -> long
        {
            const std::string sortOrder[4] = { "VP8", "VP9", "H264", "AV1X" };
            auto it = std::find(std::begin(sortOrder), std::end(sortOrder), format.name);
            if (it == std::end(sortOrder))
                return LONG_MAX;
            return static_cast<long>(std::distance(std::begin(sortOrder), it));
        };
        std::sort(
            supported_codecs.begin(),
            supported_codecs.end(),
            [&](webrtc::SdpVideoFormat& x, webrtc::SdpVideoFormat& y) -> int { return (findIndex(x) < findIndex(y)); });
        return supported_codecs;
    }

    webrtc::VideoEncoderFactory::CodecInfo
    UnityVideoEncoderFactory::QueryVideoEncoder(const webrtc::SdpVideoFormat& format) const
    {
        if (native_encoder_factory_ && format.IsCodecInList(native_encoder_factory_->GetSupportedFormats()))
        {
            return native_encoder_factory_->QueryVideoEncoder(format);
        }
        RTC_DCHECK(format.IsCodecInList(internal_encoder_factory_->GetSupportedFormats()));
        return internal_encoder_factory_->QueryVideoEncoder(format);
    }

    std::unique_ptr<webrtc::VideoEncoder>
    UnityVideoEncoderFactory::CreateVideoEncoder(const webrtc::SdpVideoFormat& format)
    {
        std::unique_ptr<webrtc::VideoEncoder> encoder;
        if (native_encoder_factory_ && format.IsCodecInList(native_encoder_factory_->GetSupportedFormats()))
        {
            encoder = native_encoder_factory_->CreateVideoEncoder(format);
        }
        else
        {
            encoder = internal_encoder_factory_->CreateVideoEncoder(format);
        }
        if (!profiler_)
            return encoder;

        // Use Unity Profiler for measuring encoding process.
        return std::make_unique<UnityVideoEncoder>(std::move(encoder), profiler_);
    }
}
}

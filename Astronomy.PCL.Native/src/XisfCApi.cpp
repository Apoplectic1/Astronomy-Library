#include "Astronomy/PCL/XisfCApi.h"

#include <pcl/XISF.h>
#include <pcl/Image.h>
#include <pcl/ImageInfo.h>
#include <pcl/ImageOptions.h>
#include <pcl/ColorSpace.h>
#include <pcl/Exception.h>
#include <pcl/String.h>

#include "Exception.h"
#include "LastError.h"
#include "SilentLogHandler.h"

namespace
{
    pcl::XISFReader* AsReader(AstronomyXisfHandle h) noexcept
    {
        return static_cast<pcl::XISFReader*>(h);
    }

    // Configure PCL exception output once. Without this, an exception's destructor
    // can try to write to the PixInsight core's GUI console, which doesn't exist
    // in a host process — that side effect manifests as an unrecognized exception
    // type at our catch sites.
    struct PclRuntimeInit
    {
        PclRuntimeInit() noexcept
        {
            try
            {
                pcl::Exception::DisableGUIOutput();
                pcl::Exception::DisableConsoleOutput();
            }
            catch (...) {}
        }
    };
    PclRuntimeInit g_pclInit;
}

extern "C" int32_t AstronomyXisf_Open(const wchar_t* utf16Path, AstronomyXisfHandle* outHandle)
{
    if (utf16Path == nullptr || outHandle == nullptr)
        return AstronomyXisfStatus_InvalidArgument;
    *outHandle = nullptr;
    ASTRONOMY_PCL_TRY
        auto* reader = new pcl::XISFReader();
        reader->SetLogHandler(new astronomy::SilentLogHandler);
        reader->Open(pcl::String(reinterpret_cast<const pcl::char16_type*>(utf16Path)));
        *outHandle = reader;
        return AstronomyXisfStatus_Ok;
    ASTRONOMY_PCL_CATCH
}

extern "C" int32_t AstronomyXisf_Close(AstronomyXisfHandle handle)
{
    if (handle == nullptr)
        return AstronomyXisfStatus_Ok;
    ASTRONOMY_PCL_TRY
        auto* reader = AsReader(handle);
        if (reader->IsOpen())
            reader->Close();
        delete reader;
        return AstronomyXisfStatus_Ok;
    ASTRONOMY_PCL_CATCH
}

extern "C" int32_t AstronomyXisf_NumberOfImages(AstronomyXisfHandle handle, int32_t* outCount)
{
    if (handle == nullptr || outCount == nullptr)
        return AstronomyXisfStatus_InvalidArgument;
    *outCount = 0;
    ASTRONOMY_PCL_TRY
        auto* reader = AsReader(handle);
        if (!reader->IsOpen())
            return AstronomyXisfStatus_NotOpen;
        *outCount = reader->NumberOfImages();
        return AstronomyXisfStatus_Ok;
    ASTRONOMY_PCL_CATCH
}

extern "C" int32_t AstronomyXisf_SelectImage(AstronomyXisfHandle handle, int32_t index)
{
    if (handle == nullptr)
        return AstronomyXisfStatus_InvalidArgument;
    ASTRONOMY_PCL_TRY
        auto* reader = AsReader(handle);
        if (!reader->IsOpen())
            return AstronomyXisfStatus_NotOpen;
        if (index < 0 || index >= reader->NumberOfImages())
            return AstronomyXisfStatus_OutOfRange;
        reader->SelectImage(index);
        return AstronomyXisfStatus_Ok;
    ASTRONOMY_PCL_CATCH
}

extern "C" int32_t AstronomyXisf_GetImageInfo(AstronomyXisfHandle handle, AstronomyXisfImageInfo* outInfo)
{
    if (handle == nullptr || outInfo == nullptr)
        return AstronomyXisfStatus_InvalidArgument;
    ASTRONOMY_PCL_TRY
        auto* reader = AsReader(handle);
        if (!reader->IsOpen())
            return AstronomyXisfStatus_NotOpen;
        const pcl::ImageInfo info = reader->ImageInfo();
        const pcl::ImageOptions opts = reader->ImageOptions();
        outInfo->width = info.width;
        outInfo->height = info.height;
        outInfo->numberOfChannels = info.numberOfChannels;
        outInfo->bitsPerSample = static_cast<int32_t>(opts.bitsPerSample);
        outInfo->ieeefpSampleFormat = opts.ieeefpSampleFormat ? 1 : 0;
        outInfo->colorSpace = static_cast<int32_t>(info.colorSpace);
        outInfo->reserved0 = 0;
        outInfo->reserved1 = 0;
        return AstronomyXisfStatus_Ok;
    ASTRONOMY_PCL_CATCH
}

extern "C" int32_t AstronomyXisf_ReadImageF32(AstronomyXisfHandle handle, float* outSamples, int64_t samplesCount)
{
    if (handle == nullptr || outSamples == nullptr || samplesCount <= 0)
        return AstronomyXisfStatus_InvalidArgument;
    ASTRONOMY_PCL_TRY
        auto* reader = AsReader(handle);
        if (!reader->IsOpen())
        {
            ::astronomy::SetLastError(pcl::String("not open"));
            return AstronomyXisfStatus_NotOpen;
        }

        const pcl::ImageInfo info = reader->ImageInfo();
        const int64_t expected = static_cast<int64_t>(info.width)
                               * static_cast<int64_t>(info.height)
                               * static_cast<int64_t>(info.numberOfChannels);
        if (samplesCount != expected)
        {
            ::astronomy::SetLastError(pcl::String("buffer size mismatch"));
            return AstronomyXisfStatus_BufferTooSmall;
        }

        const pcl::ImageOptions opts = reader->ImageOptions();
        const int64_t perChannel = static_cast<int64_t>(info.width) * static_cast<int64_t>(info.height);

        // Read in the file's native sample format, then convert to float32 ourselves.
        // PCL's auto-converting ReadImage(FImage&) path appears to need PixInsight platform
        // services that aren't available in a host process.
        if (opts.ieeefpSampleFormat && opts.bitsPerSample == 32)
        {
            pcl::FImage image;
            reader->ReadImage(image);
            for (int c = 0; c < info.numberOfChannels; ++c)
            {
                const float* src = image.PixelData(c);
                if (src == nullptr) return AstronomyXisfStatus_UnknownException;
                std::memcpy(outSamples + c * perChannel, src, static_cast<size_t>(perChannel) * sizeof(float));
            }
        }
        else if (opts.ieeefpSampleFormat && opts.bitsPerSample == 64)
        {
            pcl::DImage image;
            reader->ReadImage(image);
            for (int c = 0; c < info.numberOfChannels; ++c)
            {
                const double* src = image.PixelData(c);
                if (src == nullptr) return AstronomyXisfStatus_UnknownException;
                float* dst = outSamples + c * perChannel;
                for (int64_t i = 0; i < perChannel; ++i) dst[i] = static_cast<float>(src[i]);
            }
        }
        else if (!opts.ieeefpSampleFormat && opts.bitsPerSample == 16)
        {
            pcl::UInt16Image image;
            reader->ReadImage(image);
            const float scale = 1.0f / 65535.0f;
            for (int c = 0; c < info.numberOfChannels; ++c)
            {
                const pcl::uint16* src = image.PixelData(c);
                if (src == nullptr) return AstronomyXisfStatus_UnknownException;
                float* dst = outSamples + c * perChannel;
                for (int64_t i = 0; i < perChannel; ++i) dst[i] = static_cast<float>(src[i]) * scale;
            }
        }
        else if (!opts.ieeefpSampleFormat && opts.bitsPerSample == 8)
        {
            pcl::UInt8Image image;
            reader->ReadImage(image);
            const float scale = 1.0f / 255.0f;
            for (int c = 0; c < info.numberOfChannels; ++c)
            {
                const pcl::uint8* src = image.PixelData(c);
                if (src == nullptr) return AstronomyXisfStatus_UnknownException;
                float* dst = outSamples + c * perChannel;
                for (int64_t i = 0; i < perChannel; ++i) dst[i] = static_cast<float>(src[i]) * scale;
            }
        }
        else if (!opts.ieeefpSampleFormat && opts.bitsPerSample == 32)
        {
            pcl::UInt32Image image;
            reader->ReadImage(image);
            const float scale = 1.0f / 4294967295.0f;
            for (int c = 0; c < info.numberOfChannels; ++c)
            {
                const pcl::uint32* src = image.PixelData(c);
                if (src == nullptr) return AstronomyXisfStatus_UnknownException;
                float* dst = outSamples + c * perChannel;
                for (int64_t i = 0; i < perChannel; ++i) dst[i] = static_cast<float>(src[i]) * scale;
            }
        }
        else
        {
            ::astronomy::SetLastError(pcl::String("unsupported sample format"));
            return AstronomyXisfStatus_InvalidArgument;
        }
        return AstronomyXisfStatus_Ok;
    ASTRONOMY_PCL_CATCH
}

extern "C" int32_t AstronomyXisf_GetLastErrorMessage(wchar_t* outBuffer, int32_t bufferCharCount, int32_t* outRequiredCharCount)
{
    const pcl::String& msg = astronomy::GetLastError();
    const int32_t needed = static_cast<int32_t>(msg.Length()) + 1;
    if (outRequiredCharCount != nullptr)
        *outRequiredCharCount = needed;

    if (outBuffer == nullptr || bufferCharCount <= 0)
        return AstronomyXisfStatus_Ok;

    if (bufferCharCount < needed)
        return AstronomyXisfStatus_BufferTooSmall;

    const pcl::char16_type* src = msg.Begin();
    const int32_t copy = needed - 1;
    if (copy > 0 && src != nullptr)
        std::memcpy(outBuffer, src, static_cast<size_t>(copy) * sizeof(wchar_t));
    outBuffer[copy] = L'\0';
    return AstronomyXisfStatus_Ok;
}

extern "C" int32_t AstronomyXisf_Add(int32_t a, int32_t b)
{
    return a + b;
}

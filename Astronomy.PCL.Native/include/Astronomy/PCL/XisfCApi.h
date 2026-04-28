#ifndef ASTRONOMY_PCL_XISF_C_API_H
#define ASTRONOMY_PCL_XISF_C_API_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef void* AstronomyXisfHandle;

enum AstronomyXisfStatus
{
    AstronomyXisfStatus_Ok                = 0,
    AstronomyXisfStatus_NotOpen           = 1,
    AstronomyXisfStatus_InvalidArgument   = 2,
    AstronomyXisfStatus_BufferTooSmall    = 3,
    AstronomyXisfStatus_OutOfRange        = 4,
    AstronomyXisfStatus_PclException      = 100,
    AstronomyXisfStatus_StdException      = 101,
    AstronomyXisfStatus_UnknownException  = 102
};

typedef struct AstronomyXisfImageInfo
{
    int32_t  width;
    int32_t  height;
    int32_t  numberOfChannels;
    int32_t  bitsPerSample;
    int32_t  ieeefpSampleFormat;
    int32_t  colorSpace;
    int32_t  reserved0;
    int32_t  reserved1;
} AstronomyXisfImageInfo;

int32_t AstronomyXisf_Open(const wchar_t* utf16Path, AstronomyXisfHandle* outHandle);
int32_t AstronomyXisf_Close(AstronomyXisfHandle handle);
int32_t AstronomyXisf_NumberOfImages(AstronomyXisfHandle handle, int32_t* outCount);
int32_t AstronomyXisf_SelectImage(AstronomyXisfHandle handle, int32_t index);
int32_t AstronomyXisf_GetImageInfo(AstronomyXisfHandle handle, AstronomyXisfImageInfo* outInfo);
int32_t AstronomyXisf_ReadImageF32(AstronomyXisfHandle handle, float* outSamples, int64_t samplesCount);
int32_t AstronomyXisf_GetLastErrorMessage(wchar_t* outBuffer, int32_t bufferCharCount, int32_t* outRequiredCharCount);

int32_t AstronomyXisf_Add(int32_t a, int32_t b);

#ifdef __cplusplus
}
#endif

#endif

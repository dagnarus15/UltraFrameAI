#pragma once

#include <cstddef>
#include <cstdint>

#if defined(_WIN32)
#  if defined(AE_BUILD_DLL)
#    define AE_API extern "C" __declspec(dllexport)
#  else
#    define AE_API extern "C" __declspec(dllimport)
#  endif
#else
#  define AE_API extern "C"
#endif

struct ae_config
{
    int width;
    int height;
    int channels;
    int has_audio;
    int has_subtitles;
};

AE_API void* ae_create_session(const ae_config* config);
AE_API void ae_destroy_session(void* handle);
AE_API void ae_reset_session(void* handle);
AE_API int ae_submit_frame(void* handle, const std::uint8_t* input_bgr, int byte_count, double pts_seconds);
AE_API int ae_flush(void* handle);
AE_API const char* ae_get_last_error(void* handle);

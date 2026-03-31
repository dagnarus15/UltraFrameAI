#pragma once

#include <cstddef>
#include <cstdint>

#if defined(_WIN32)
#  if defined(UFE_BUILD_DLL)
#    define UFE_API extern "C" __declspec(dllexport)
#  else
#    define UFE_API extern "C" __declspec(dllimport)
#  endif
#else
#  define UFE_API extern "C"
#endif

enum ufe_status : int
{
    UFE_STATUS_OK = 0,
    UFE_STATUS_INVALID_ARGUMENT = -1,
    UFE_STATUS_NOT_SUPPORTED = -2,
    UFE_STATUS_INTERNAL_ERROR = -3
};

struct ufe_open_config
{
    int width;
    int height;
    int channels;
    int fps_num;
    int fps_den;
    const char* codec_name;
    const char* output_path;
};

struct ufe_frame_packet
{
    const std::uint8_t* data;
    std::size_t size;
    std::int64_t pts_num;
    std::int64_t pts_den;
};

UFE_API void* ufe_session_create();
UFE_API void ufe_session_destroy(void* handle);

UFE_API int ufe_session_open(void* handle, const ufe_open_config* config);
UFE_API int ufe_session_submit_frame(void* handle, const ufe_frame_packet* frame);
UFE_API int ufe_session_submit_timestamp(void* handle, std::int64_t pts_num, std::int64_t pts_den);
UFE_API int ufe_session_flush(void* handle);
UFE_API int ufe_session_close(void* handle);

UFE_API const char* ufe_session_get_last_error(void* handle);

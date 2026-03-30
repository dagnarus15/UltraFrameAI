#pragma once

#include <cstddef>
#include <cstdint>

#if defined(_WIN32)
#  if defined(AF_BUILD_DLL)
#    define AF_API extern "C" __declspec(dllexport)
#  else
#    define AF_API extern "C" __declspec(dllimport)
#  endif
#else
#  define AF_API extern "C"
#endif

struct af_config
{
    int width;
    int height;
    int channels;
    int downscale_factor;
    int block_size;
    int search_radius;
    int content_mode;
    float blend_strength;
    float max_blend;
    float edge_guard;
    float strength_scale;
};

AF_API void* af_create(const af_config* config);
AF_API void af_destroy(void* handle);
AF_API void af_reset(void* handle);
AF_API int af_process(void* handle, const std::uint8_t* input_bgr, std::uint8_t* output_bgr, int byte_count);

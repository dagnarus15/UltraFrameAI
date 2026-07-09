# Third-Party Notes

## UltraFrame AI

License: MIT

## RealESRGAN / realesrgan-ncnn-vulkan

UltraFrame AI uses RealESRGAN models and a pipe-enabled `realesrgan-ncnn-vulkan` fork as its native upscaling engine.

The engine is kept as a separate submodule:

```text
realesrgan-ncnn-vulkan-fork/
```

The bundled runtime assets and models are kept in:

```text
realesrgan-ncnn-vulkan-20220424/
```

See the license and notice files in those directories and their upstream projects.

## FFmpeg

FFmpeg is required at runtime, but FFmpeg binaries should be treated as a separate external component.

The source repository does not commit FFmpeg binaries such as:

- `ffmpeg.exe`
- `ffprobe.exe`
- `ffplay.exe`
- FFmpeg shared libraries such as `avcodec`, `avformat`, `avutil`, `swscale`, and `swresample`

The app can:

- use FFmpeg found on `PATH`;
- use a user-selected FFmpeg folder;
- download/install FFmpeg only after the user confirms the setup flow.

FFmpeg is licensed separately under FFmpeg's own GPL/LGPL terms, depending on the exact build used.

UltraFrame AI is not affiliated with FFmpeg.

If a release package includes FFmpeg binaries, the release must also include the license notices, source/build information, and any other obligations required by that specific FFmpeg build.

# FFmpeg Distribution Notes

UltraFrameAI may ship with FFmpeg shared libraries to support the native encoder backend.

## Expected license model

- FFmpeg binaries: LGPL v2.1 or later
- Linking: dynamic only
- No GPL builds
- No nonfree builds

## What should be included with the app

- `UltraFrameAI.exe`
- required FFmpeg shared libraries:
  - `avcodec-*.dll`
  - `avformat-*.dll`
  - `avutil-*.dll`
  - `swscale-*.dll`
  - `swresample-*.dll`
- `NOTICE.txt`
- `THIRD_PARTY.md`
- the corresponding FFmpeg source bundle or a precise source reference

## What should not be included

- statically linked FFmpeg binaries
- GPL-enabled FFmpeg builds
- nonfree FFmpeg builds

## Notes for packaging

- Keep FFmpeg DLLs separate from the application source tree.
- Load them dynamically at runtime.
- Record the exact version and build flags in the release notes or third-party notices.

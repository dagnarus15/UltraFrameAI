# Third-Party Notices

## UltraFrameAI

License: MIT

Copyright (c) 2026 OpenAI

## FFmpeg

This distribution may include FFmpeg shared libraries used by the application at runtime.

- Version: `<FFMPEG_VERSION>`
- License: LGPL v2.1 or later
- Linking model: dynamic
- Included components:
  - avcodec
  - avformat
  - avutil
  - swscale
  - swresample

### Build and source information

- Source: `<FFMPEG_SOURCE_URL_OR_ARCHIVE_LOCATION>`
- Build flags:
  - `--disable-static`
  - `--enable-shared`
  - `--disable-gpl`
  - `--disable-nonfree`
  - `<any additional safe flags used for the build>`

### Notes

- UltraFrameAI itself remains MIT-licensed.
- FFmpeg is a separate third-party dependency with its own license terms.
- If any FFmpeg source modifications are distributed, those modifications must be made available under the corresponding FFmpeg license.
- Users may replace the bundled FFmpeg DLLs with a compatible build of their choice, subject to the applicable FFmpeg license terms.

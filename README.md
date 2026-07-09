# UltraFrame AI

UltraFrame AI is a Windows desktop app for batch video upscaling with RealESRGAN. It is focused on animated and anime-style video, where RealESRGAN usually works best.

The app provides a localized GUI for building a render queue from files or folders, benchmarking the current GPU, choosing practical render settings, resuming interrupted jobs when possible, and opening result folders after processing.

Image rendering is planned for future versions.

## Features

- Batch video queue from individual files or whole folders.
- RealESRGAN-based upscaling through a native Vulkan pipeline.
- GPU selection and startup benchmark for recommended settings.
- Resume/recovery flow for interrupted renders when recovery is possible.
- Render preview window with original/result comparison.
- Result summary window with per-file actions.
- Multilingual UI: English, Russian, German, Japanese, and Chinese.
- Optional automatic FFmpeg setup if FFmpeg is not found.

## FFmpeg

FFmpeg is required for video scanning, decoding, encoding, benchmarking, and rendering.

This repository does not commit FFmpeg binaries. The app treats FFmpeg as an external component:

- users can point the app to an existing folder containing `ffmpeg.exe` and `ffprobe.exe`;
- or the app can download/install FFmpeg after explicit user confirmation;
- installed FFmpeg files are kept separately from the application code.

FFmpeg is licensed separately under FFmpeg's own GPL/LGPL terms, depending on the build used. UltraFrame AI is not affiliated with FFmpeg.

If you distribute a release package that includes FFmpeg binaries, include the matching FFmpeg license notices and source/build information as required by that FFmpeg build.

See [THIRD_PARTY.md](THIRD_PARTY.md) for third-party notes.

## Requirements

- Windows 10/11 x64.
- .NET 8 Desktop Runtime for framework-dependent builds.
- Vulkan-capable GPU supported by the bundled/native RealESRGAN engine.
- FFmpeg available through the app setup flow, a configured folder, or `PATH`.

## Repository Layout

- `UltraFrameAI/` - main WPF application.
- `UltraFrameAI.Tests/` - integration and support tests.
- `UltraFrameAI.Native/` - native support code.
- `realesrgan-ncnn-vulkan-fork/` - pipe-enabled native RealESRGAN engine submodule.
- `realesrgan-ncnn-vulkan-20220424/` - RealESRGAN runtime assets and models used by the app.
- `THIRD_PARTY.md` - third-party notes and FFmpeg distribution guidance.

## Build

Clone with submodules:

```powershell
git clone --recurse-submodules https://github.com/dagnarus15/UltraFrameAI.git
cd UltraFrameAI
```

Build the app:

```powershell
dotnet build .\UltraFrameAI\UltraFrameAI.csproj -c Release -r win-x64
```

Publish a portable build:

```powershell
.\UltraFrameAI\publish-portable.ps1
```

The portable output is generated under `dist/`.

## Development Notes

- The streaming pipeline is preferred because it avoids huge temporary frame folders.
- Benchmark results are recommendations for this machine, not global hardware requirements.
- RealESRGAN can create anime-like details on non-animated videos because the models are strongest on animation/anime sources.
- Keep generated build output, benchmark logs, local FFmpeg folders, and downloaded FFmpeg binaries out of git.

## License

UltraFrame AI project code is intended to be distributed under the MIT license. Third-party components keep their own licenses.

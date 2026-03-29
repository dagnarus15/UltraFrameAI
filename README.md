# UltraFrame AI

UltraFrame AI is a portable Windows GUI for batch video upscaling with streaming pipeline processing, progress reporting, and cancellation support.

## Forked engine

The project uses the `realesrgan-ncnn-vulkan-fork` submodule as the native streaming upscaling engine. That fork adds raw pipe support and keeps the original file-based mode as a fallback.

## Repositories

- Main app: `UltraFrameAI`
- Engine fork: `UltraFrameAI-Realesrgan-Pipe`


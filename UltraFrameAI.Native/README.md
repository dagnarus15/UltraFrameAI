# UltraFrameAI.Native

This folder contains native C++ components used by the main `UltraFrameAI` app.

- `Encoder` ABI seam: a stub C ABI surface for future FFmpeg API integration.

The encoder layer is intentionally a no-op stub right now:

- it compiles and exposes a stable C ABI,
- it does not depend on FFmpeg yet,
- it returns explicit `UFE_STATUS_NOT_SUPPORTED` for encode actions.

## Licensing notes

`UltraFrameAI` is MIT-licensed. Any future FFmpeg API integration in this folder must remain MIT/LGPL-friendly:

- prefer dynamic FFmpeg libraries,
- avoid GPL/nonfree FFmpeg builds,
- avoid static FFmpeg linkage.

# External Refiner API

This project can run an optional external refiner stage between the built-in upscaler and the encoder:

`decode -> upscaler -> external refiner -> anti-flicker -> encode`

The refiner is not bundled with the app. This is intentional:

- the app stays MIT-licensed
- restricted tools can be installed and managed by the user separately
- the app only provides a generic pipe contract

## Contract

An external refiner must:

- read raw `BGR24` frames from `stdin`
- write raw `BGR24` frames to `stdout`
- preserve the input frame count
- preserve the frame size
- preserve the frame pixel format

The refiner must not:

- emit text or logs to `stdout`
- change frame dimensions
- change the number of channels
- buffer the entire video before writing output

Logs should go to `stderr`.

## Expected Frame Shape

- input width: already upscaled width
- input height: already upscaled height
- pixel format: `BGR24`
- channels: `3`

Per-frame byte size:

`width * height * 3`

## Arguments Template Placeholders

The app can replace the following placeholders in the arguments template:

- `{width}`
- `{height}`
- `{frameBudget}`
- `{channels}`
- `{scale}`
- `{input}`
- `{output}`
- `{modelDir}`
- `{modelDirQ}`
- `{threads}`
- `{threadsQ}`
- `{tileSize}`
- `{gpuId}`

For external refiners, the most useful placeholders are usually:

- `{width}`
- `{height}`
- `{frameBudget}`
- `{channels}`
- `{input}`
- `{output}`
- `{modelDir}`
- `{modelDirQ}`
- `{threads}`
- `{threadsQ}`

`{input}` and `{output}` resolve to `-`, meaning stdin/stdout pipe mode.

## Minimal Behavior

A valid refiner can be as simple as:

- read one raw frame from stdin
- optionally modify it
- write one raw frame to stdout
- repeat until EOF

This means a pure pass-through wrapper is valid for smoke testing.

## Recommended Wrapper Design

For a real external tool, use a small user-owned wrapper that:

1. reads raw frames from stdin
2. converts them into the format expected by the real model
3. runs the model
4. converts the refined frame back to `BGR24`
5. writes the frame to stdout

That wrapper should live outside the MIT app if the model or upstream project has restrictive licensing.

## Sample Pass-Through Wrapper

For testing the API without any third-party model, a neutral sample wrapper is included:

- [tools/external-refiner-pass-through.ps1](/e:/Projects/AIUpscale/tools/external-refiner-pass-through.ps1)

Example setup:

- executable path:
  - `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`
- arguments template:
  - `-ExecutionPolicy Bypass -File "E:\Projects\AIUpscale\tools\external-refiner-pass-through.ps1" -W {width} -H {height} -N {frameBudget} -c {channels}`

This sample does not improve image quality. It only proves that the external refiner pipe stage is wired correctly.

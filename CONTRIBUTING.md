# Contributing

Thank you for your interest in improving UltraFrame AI.

## Good Ways to Help

- Report bugs with clear reproduction steps.
- Suggest small, focused improvements.
- Improve translations and UI wording.
- Test releases on different Windows/GPU setups.
- Submit fixes with a focused pull request.

## Before Opening an Issue

- Check whether the issue already exists.
- Include the app version, Windows version, GPU model, and selected settings when relevant.
- If rendering failed, include the visible error message and the file type if possible.
- Do not attach copyrighted videos unless you have permission to share them.

## Pull Requests

- Keep changes focused and easy to review.
- Explain what changed and why.
- Mention whether FFmpeg, RealESRGAN, rendering, localization, or release packaging is affected.
- Run a build before submitting when possible:

```powershell
dotnet build .\UltraFrameAI\UltraFrameAI.csproj -c Release -r win-x64
```

## Localization Notes

Be careful with localized files such as `.resx`, `.xaml`, `.cs`, `.json`, and `.md`.

- Preserve UTF-8 text correctly.
- Do not introduce mojibake.
- Keep placeholders such as `{0}`, `{1}`, and `\n` intact.
- Check Cyrillic, German umlauts, Japanese, and Chinese text after editing.

## FFmpeg Notes

Do not commit FFmpeg binaries to the repository.

FFmpeg is treated as an external runtime component. The app can use an existing FFmpeg installation or guide the user through setup.

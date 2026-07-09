param(
    [switch]$IncludeFfmpeg,
    [switch]$SingleFile,
    [string]$FfmpegBin = 'C:\ffmpeg\bin'
)

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $projectDir
$distDir = Join-Path $repoRoot 'dist\UltraFrameAI'
$realesrganSrc = Join-Path $repoRoot 'realesrgan-ncnn-vulkan-20220424'
$realesrganDst = Join-Path $distDir 'realesrgan-ncnn-vulkan-20220424'
$realesrganForkExe = Join-Path $repoRoot 'realesrgan-ncnn-vulkan-fork\build\Release\realesrgan-ncnn-vulkan.exe'
$publishSingleFile = $SingleFile.IsPresent.ToString().ToLowerInvariant()

if (Test-Path $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}

$objDir = Join-Path $projectDir 'obj'
# Keep bin intact so a running Debug session or open Visual Studio instance
# does not block portable rebuilds.
if (Test-Path $objDir) {
    Remove-Item -LiteralPath $objDir -Recurse -Force
}

& dotnet restore (Join-Path $projectDir 'UltraFrameAI.csproj') `
    -r win-x64 `
    --force-evaluate
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

& dotnet publish (Join-Path $projectDir 'UltraFrameAI.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:SelfContained=true `
    -p:PublishSingleFile=$publishSingleFile `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --no-restore `
    -o $distDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $projectDir 'RUN_ME.txt') -Destination (Join-Path $distDir 'RUN_ME.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY.md') -Destination (Join-Path $distDir 'THIRD_PARTY.md') -Force

if (-not (Test-Path $realesrganForkExe)) {
    throw "Fork build not found at $realesrganForkExe"
}

Copy-Item -LiteralPath $realesrganForkExe -Destination (Join-Path $realesrganSrc 'realesrgan-ncnn-vulkan.exe') -Force

if ($IncludeFfmpeg) {
    $ffmpegExe = Join-Path $FfmpegBin 'ffmpeg.exe'
    $ffprobeExe = Join-Path $FfmpegBin 'ffprobe.exe'

    if (-not (Test-Path $ffmpegExe) -or -not (Test-Path $ffprobeExe)) {
        throw "ffmpeg.exe / ffprobe.exe not found in $FfmpegBin"
    }

    Copy-Item -LiteralPath $ffmpegExe -Destination (Join-Path $distDir 'ffmpeg.exe') -Force
    Copy-Item -LiteralPath $ffprobeExe -Destination (Join-Path $distDir 'ffprobe.exe') -Force
    Write-Host "Included external FFmpeg binaries from: $FfmpegBin"
}
else {
    Write-Host "FFmpeg binaries were not bundled. The app will ask the user to configure or download FFmpeg on first run if needed."
}

Copy-Item -LiteralPath $realesrganSrc -Destination $realesrganDst -Recurse -Force

Write-Host "Portable build ready at: $distDir"

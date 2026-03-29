$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $projectDir
$distDir = Join-Path $repoRoot 'dist\UltraFrameAI'
$realesrganSrc = Join-Path $repoRoot 'realesrgan-ncnn-vulkan-20220424'
$realesrganDst = Join-Path $distDir 'realesrgan-ncnn-vulkan-20220424'
$realesrganForkExe = Join-Path $repoRoot 'realesrgan-ncnn-vulkan-fork\build\Release\realesrgan-ncnn-vulkan.exe'

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

& dotnet build (Join-Path $projectDir 'UltraFrameAI.csproj') `
    -c Release `
    -r win-x64 `
    --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

& dotnet publish (Join-Path $projectDir 'UltraFrameAI.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --no-build `
    --no-restore `
    -o $distDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$ffmpegBin = 'C:\ffmpeg\bin'
$ffmpegExe = Join-Path $ffmpegBin 'ffmpeg.exe'
$ffprobeExe = Join-Path $ffmpegBin 'ffprobe.exe'
Copy-Item -LiteralPath (Join-Path $projectDir 'RUN_ME.txt') -Destination (Join-Path $distDir 'RUN_ME.txt') -Force

if (-not (Test-Path $ffmpegExe) -or -not (Test-Path $ffprobeExe)) {
    throw "ffmpeg.exe / ffprobe.exe not found in $ffmpegBin"
}

if (-not (Test-Path $realesrganForkExe)) {
    throw "Fork build not found at $realesrganForkExe"
}

Copy-Item -LiteralPath $realesrganForkExe -Destination (Join-Path $realesrganSrc 'realesrgan-ncnn-vulkan.exe') -Force

Copy-Item -LiteralPath $ffmpegExe -Destination (Join-Path $distDir 'ffmpeg.exe') -Force
Copy-Item -LiteralPath $ffprobeExe -Destination (Join-Path $distDir 'ffprobe.exe') -Force
Copy-Item -LiteralPath $realesrganSrc -Destination $realesrganDst -Recurse -Force

Write-Host "Portable build ready at: $distDir"

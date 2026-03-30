$ErrorActionPreference = 'Stop'

$nativeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $nativeDir 'build'
$vcvarsall = 'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat'

if (-not (Test-Path $vcvarsall)) {
    throw "Could not find vcvarsall.bat at $vcvarsall"
}

New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

$cmakeConfigure = 'cmake -S "{0}" -B "{1}" -G "Visual Studio 17 2022" -A x64' -f $nativeDir, $buildDir
$cmakeBuild = 'cmake --build "{0}" --config Release' -f $buildDir

$command = '"{0}" x64 && {1} && {2}' -f $vcvarsall, $cmakeConfigure, $cmakeBuild

& cmd /c $command
if ($LASTEXITCODE -ne 0) {
    throw "Native anti-flicker build failed with exit code $LASTEXITCODE"
}

Write-Host "Native anti-flicker build ready at: $(Join-Path $buildDir 'Release')"

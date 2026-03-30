param(
    [string]$SourceFile = "",
    [string]$OutputDir = "",
    [int]$SampleSeconds = 20,
    [string]$ExePath = ""
)

$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $projectDir "..")

function Get-DefaultSourceFile {
    param([string]$Root)

    $patterns = @('*.mkv', '*.mp4', '*.mov', '*.m4v', '*.avi', '*.webm', '*.ts', '*.m2ts', '*.flv', '*.wmv')
    $files = Get-ChildItem -Path $Root -Recurse -File -Include $patterns |
        Where-Object {
            $_.FullName -notmatch '\\bin\\' -and
            $_.FullName -notmatch '\\obj\\' -and
            $_.FullName -notmatch '\\dist\\' -and
            $_.FullName -notmatch 'realesrgan-ncnn-vulkan-20220424\\onepiece_demo\.mp4$'
        } |
        Sort-Object Length -Descending

    if (-not $files) {
        throw "No video source file was found under $Root."
    }

    return $files[0].FullName
}

if ([string]::IsNullOrWhiteSpace($SourceFile)) {
    $SourceFile = Get-DefaultSourceFile -Root $repoRoot.Path
}
elseif (-not (Test-Path -LiteralPath $SourceFile)) {
    throw "Source file not found: $SourceFile"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $projectDir "benchmark-output"
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $projectDir "bin\Release\net8.0-windows\win-x64\UltraFrameAI.exe"
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Building UltraFrameAI..."
    dotnet build (Join-Path $projectDir "UltraFrameAI.csproj") -c Release /p:UseSharedCompilation=false
}

if (-not (Test-Path $ExePath)) {
    throw "UltraFrameAI.exe was not found at $ExePath."
}

$bakDir = $null
if (Test-Path -LiteralPath $OutputDir) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $bakDir = "${OutputDir}_bak_$timestamp"
    Move-Item -LiteralPath $OutputDir -Destination $bakDir -Force
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$doneFile = Join-Path $OutputDir "benchmark.done"
if (Test-Path -LiteralPath $doneFile) {
    Remove-Item -LiteralPath $doneFile -Force
}

Write-Host "Source: $SourceFile"
Write-Host "Output: $OutputDir"
if ($bakDir) {
    Write-Host "Previous output moved to: $bakDir"
}
Write-Host "Sample seconds: $SampleSeconds"
Write-Host ""

$args = @(
    '--benchmark-source', $SourceFile,
    '--benchmark-output', $OutputDir,
    '--benchmark-seconds', $SampleSeconds,
    '--benchmark-done-file', $doneFile
)

Write-Host "Benchmark running..."
$quotedArgs = ($args | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
$process = Start-Process -FilePath $ExePath -ArgumentList $quotedArgs -PassThru -Wait -NoNewWindow
if ($process.ExitCode -ne 0) {
    throw "UltraFrameAI benchmark failed with exit code $($process.ExitCode)"
}

if (-not (Test-Path -LiteralPath $doneFile)) {
    throw "UltraFrameAI benchmark exited without writing completion marker: $doneFile"
}

$resultsMd = Join-Path $OutputDir "benchmark-results.md"
$resultsCsv = Join-Path $OutputDir "benchmark-results.csv"
$resultsLog = Join-Path $OutputDir "benchmark.log"

Write-Host ""
Write-Host "Benchmark finished."
Write-Host "Markdown: $resultsMd"
Write-Host "CSV: $resultsCsv"
Write-Host "Log: $resultsLog"

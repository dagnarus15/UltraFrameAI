param(
    [Parameter(Mandatory = $true)]
    [int]$W,

    [Parameter(Mandatory = $true)]
    [int]$H,

    [Parameter(Mandatory = $false)]
    [int]$N = 0,

    [Parameter(Mandatory = $false)]
    [int]$c = 3
)

$ErrorActionPreference = 'Stop'

$frameBytes = $W * $H * $c
if ($frameBytes -le 0) {
    throw "Invalid frame shape: ${W}x${H}x${c}"
}

$stdin = [Console]::OpenStandardInput()
$stdout = [Console]::OpenStandardOutput()
$buffer = New-Object byte[] $frameBytes

while ($true) {
    $offset = 0
    while ($offset -lt $frameBytes) {
        $read = $stdin.Read($buffer, $offset, $frameBytes - $offset)
        if ($read -le 0) {
            if ($offset -eq 0) {
                return
            }

            throw "Unexpected EOF inside a frame after $offset bytes."
        }

        $offset += $read
    }

    $stdout.Write($buffer, 0, $frameBytes)
    $stdout.Flush()
}

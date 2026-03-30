using UltraFrameAI;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesStdoutAndStderr()
    {
        var stdout = new List<string>();
        var stderr = new List<string>();

        var exitCode = await ProcessRunner.RunAsync(
            TestSupport.PowerShellExe,
            "-NoProfile -NonInteractive -Command \"Write-Output 'hello'; [Console]::Error.WriteLine('oops')\"",
            Environment.CurrentDirectory,
            stdout.Add,
            stderr.Add,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains(stdout, line => line.Contains("hello", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(stderr, line => line.Contains("oops", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCode()
    {
        var exitCode = await ProcessRunner.RunAsync(
            TestSupport.PowerShellExe,
            "-NoProfile -NonInteractive -Command \"exit 17\"",
            Environment.CurrentDirectory,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(17, exitCode);
    }

    [Fact]
    public async Task RunAsync_CancelsAndKillsTree()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync(
            TestSupport.PowerShellExe,
            "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 10\"",
            Environment.CurrentDirectory,
            null,
            null,
            cts.Token));
    }
}

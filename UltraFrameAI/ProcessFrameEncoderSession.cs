using System.Diagnostics;
using System.Text;

namespace UltraFrameAI;

internal sealed class ProcessFrameEncoderSession : IFrameEncoderSession
{
    private readonly string _fileName;
    private readonly string _arguments;
    private readonly string _workingDirectory;
    private readonly Action<string>? _onStderr;
    private readonly CancellationToken _cancellationToken;
    private Process? _process;
    private Task? _stderrPump;
    private CancellationTokenRegistration _registration;
    private string _lastError = string.Empty;
    private bool _opened;

    public ProcessFrameEncoderSession(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        Action<string>? onStderr)
    {
        _fileName = fileName;
        _arguments = arguments;
        _workingDirectory = workingDirectory;
        _cancellationToken = cancellationToken;
        _onStderr = onStderr;
    }

    public int ExitCode => _process?.HasExited == true ? _process.ExitCode : -1;

    public string? LastError => string.IsNullOrWhiteSpace(_lastError) ? null : _lastError;

    public bool SupportsPerFrameTimestamps => false;

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_opened)
        {
            return Task.CompletedTask;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _fileName,
            Arguments = _arguments,
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!_process.Start())
        {
            throw new InvalidOperationException($"Unable to start process: {_fileName}");
        }

        _registration = cancellationToken.Register(() => TryKillTree(_process));
        _stderrPump = PumpLinesAsync(_process.StandardError, line =>
        {
            _onStderr?.Invoke(line);
            if (LooksLikeProcessError(line))
            {
                _lastError = line;
            }
        }, cancellationToken);
        _opened = true;
        return Task.CompletedTask;
    }

    public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        EnsureOpened();
        await _process!.StandardInput.BaseStream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SubmitTimestampAsync(double timestampSeconds, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        EnsureOpened();
        try
        {
            _process!.StandardInput.Close();
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        EnsureOpened();
        try
        {
            await _process!.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (_stderrPump is not null)
            {
                await _stderrPump.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillTree(_process);
            throw;
        }
    }

    public void Dispose()
    {
        _registration.Dispose();
        try
        {
            if (_process is not null && !_process.HasExited)
            {
                TryKillTree(_process);
            }
        }
        catch
        {
        }

        _process?.Dispose();
        _process = null;
    }

    private void EnsureOpened()
    {
        if (!_opened || _process is null)
        {
            throw new InvalidOperationException("Encoder session is not open.");
        }
    }

    private static async Task PumpLinesAsync(StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                onLine(line);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static bool LooksLikeProcessError(string line)
    {
        return line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryKillTree(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}

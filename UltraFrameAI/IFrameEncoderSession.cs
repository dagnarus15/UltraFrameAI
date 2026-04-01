namespace UltraFrameAI;

internal interface IFrameEncoderSession : IDisposable
{
    int ExitCode { get; }
    string? LastError { get; }
    bool SupportsPerFrameTimestamps { get; }
    Task OpenAsync(CancellationToken cancellationToken);
    ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);
    ValueTask SubmitTimestampAsync(double timestampSeconds, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

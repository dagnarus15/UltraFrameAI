using System.Buffers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed class QueueItemViewModel : INotifyPropertyChanged
{
    private string _stage = string.Empty;
    private double _progress;
    private string _progressText = "0%";
    private string _elapsedText = "--:--:--";
    private string _etaText = "--:--:--";
    private string _outputState = string.Empty;
    private string _detail = string.Empty;
    private bool _isChecked;
    private bool _isBusy;
    private bool _forceOverwrite;
    private bool _skipRequested;
    private bool _isSkipped;
    private bool _isInterrupted;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; set; }

    public string Title { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public string Stage
    {
        get => _stage;
        set => SetField(ref _stage, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetField(ref _progress, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompleted)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            }
        }
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetField(ref _progressText, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        set => SetField(ref _elapsedText, value);
    }

    public string EtaText
    {
        get => _etaText;
        set => SetField(ref _etaText, value);
    }

    public string OutputState
    {
        get => _outputState;
        set => SetField(ref _outputState, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetField(ref _detail, value);
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public bool IsCompleted => Progress >= 100 && !IsSkipped && !IsInterrupted;

    public string StatusLabel => IsSkipped || IsInterrupted
        ? LocalizedStrings.QueueStatusInterrupted
        : IsCompleted
            ? LocalizedStrings.QueueStatusCompleted
            : LocalizedStrings.QueueStatusNew;

    public bool ForceOverwrite
    {
        get => _forceOverwrite;
        set => SetField(ref _forceOverwrite, value);
    }

    public bool SkipRequested
    {
        get => _skipRequested;
        set => SetField(ref _skipRequested, value);
    }

    public bool IsSkipped
    {
        get => _isSkipped;
        set
        {
            if (SetField(ref _isSkipped, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompleted)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            }
        }
    }

    public bool IsInterrupted
    {
        get => _isInterrupted;
        set
        {
            if (SetField(ref _isInterrupted, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompleted)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            }
        }
    }

    public bool IsInterruptedOrSkipped => IsInterrupted || IsSkipped;

    public void ResetUiState()
    {
        Stage = LocalizedStrings.QueueQueued;
        Progress = 0;
        ProgressText = "0%";
        ElapsedText = "--:--:--";
        EtaText = "--:--:--";
        OutputState = LocalizedStrings.QueueWaiting;
        Detail = string.Empty;
        IsBusy = false;
        IsSkipped = false;
        IsInterrupted = false;
        SkipRequested = false;
        ForceOverwrite = false;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public enum OutputConflictDecision
{
    Skip,
    Replace,
    SkipAll,
    ReplaceAll,
    Cancel
}

public sealed record OutputConflictRequest(
    QueueItemViewModel Item,
    string SourcePath,
    string OutputPath);

public sealed record RenderPreviewFrameUpdate(
    int ItemIndex,
    bool IsOriginal,
    RenderPreviewFramePayload Payload,
    int Width,
    int Height,
    int Stride);

public sealed class RenderPreviewFramePayload : IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private byte[]? _buffer;
    private readonly int _length;

    private RenderPreviewFramePayload(byte[] buffer, int length, ArrayPool<byte> pool)
    {
        _buffer = buffer;
        _length = length;
        _pool = pool;
    }

    public static RenderPreviewFramePayload From(ReadOnlySpan<byte> source)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(source.Length);
        source.CopyTo(buffer);
        return new RenderPreviewFramePayload(buffer, source.Length, pool);
    }

    public ReadOnlySpan<byte> Span
    {
        get
        {
            var buffer = _buffer;
            return buffer is null ? ReadOnlySpan<byte>.Empty : buffer.AsSpan(0, _length);
        }
    }

    public byte[]? Buffer => _buffer;

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            _pool.Return(buffer);
        }
    }
}

public enum RenderPreviewKind
{
    Original,
    Result
}

public sealed class PipelineOptions
{
    public required string RootFolder { get; init; }
    public required string OutputFolder { get; init; }
    public required bool Overwrite { get; init; }
    public required bool UseX265 { get; init; }
    public required int FfmpegThreads { get; init; }
    public required string UpscalerThreads { get; init; }
    public required int TileSize { get; init; }
    public required int? GpuId { get; init; }
    public required string FfmpegPath { get; init; }
    public required string FfprobePath { get; init; }
    public required string UpscalerPath { get; init; }
    public required string ModelDir { get; init; }
    public required bool UseAntiFlicker { get; init; }
    public required string ContentMode { get; init; }
    public required double AntiFlickerStrength { get; init; }
    public required string EncoderPreset { get; init; }
    public string OutputContainer { get; init; } = "mkv";
    public required bool UseNativeEncoderBackend { get; init; }
    public required bool PreserveIncompleteOutput { get; init; }
    public required bool RepairBrokenTimestamps { get; init; }
}

public sealed class AntiFlickerPresetState
{
    public bool Enabled { get; set; }

    public double Strength { get; set; }
}

public sealed record PipelineProgress(
    int ItemIndex,
    int ItemTotal,
    string ItemTitle,
    string Stage,
    double Progress,
    string ProgressText,
    string ElapsedText,
    string EtaText,
    string CurrentStatus,
    string CurrentDetail,
    string Summary,
    string? LogLine = null,
    bool IsHeartbeat = false,
    string StageElapsedText = "--:--:--");

public static class UiCollections
{
    public static ObservableCollection<LogEntryViewModel> CreateLogCollection() => new();
}

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(string time, string message)
    {
        Time = time;
        Message = message;
    }

    public string Time { get; }

    public string Message { get; }
}

public sealed class RecentFolderItem : INotifyPropertyChanged
{
    private bool _isCurrent;

    public RecentFolderItem(string folderPath)
    {
        FolderPath = folderPath;
        DisplayName = GetDisplayName(folderPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FolderPath { get; }

    public string DisplayName { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetField(ref _isCurrent, value);
    }

    private static string GetDisplayName(string folderPath)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folderPath);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

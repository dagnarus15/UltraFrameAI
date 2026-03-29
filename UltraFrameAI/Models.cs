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

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; set; }

    public string Title { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public string WorkPath { get; init; } = string.Empty;

    public string SrcPath => Path.Combine(WorkPath, "src");

    public string UpPath => Path.Combine(WorkPath, "up");

    public string Stage
    {
        get => _stage;
        set => SetField(ref _stage, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
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

    public void ResetUiState()
    {
        Stage = LocalizedStrings.QueueQueued;
        Progress = 0;
        ProgressText = "0%";
        ElapsedText = "--:--:--";
        EtaText = "--:--:--";
        OutputState = LocalizedStrings.QueueWaiting;
        Detail = string.Empty;
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

public sealed class PipelineOptions
{
    public required string RootFolder { get; init; }
    public required string OutputFolder { get; init; }
    public required bool Overwrite { get; init; }
    public required bool KeepTemp { get; init; }
    public required bool UseX265 { get; init; }
    public required int FfmpegThreads { get; init; }
    public required string UpscalerThreads { get; init; }
    public required int TileSize { get; init; }
    public required int? GpuId { get; init; }
    public required string FfmpegPath { get; init; }
    public required string FfprobePath { get; init; }
    public required string UpscalerPath { get; init; }
    public required string ModelDir { get; init; }
    public required bool UsePipeMode { get; init; }
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
    public static ObservableCollection<string> CreateLogCollection() => new();
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

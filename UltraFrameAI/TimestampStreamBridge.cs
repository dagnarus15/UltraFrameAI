using System.Globalization;
using System.Text.RegularExpressions;

namespace UltraFrameAI;

internal sealed class TimestampStreamBridge
{
    private static readonly Regex ShowInfoPtsRegex = new(
        @"pts_time:(?<pts>-?\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Queue<double> _timestamps;
    private readonly List<double> _snapshot = new();
    private readonly object _sync = new();
    private readonly SemaphoreSlim _available = new(0);
    private bool _completed;

    public TimestampStreamBridge(int capacity = 0)
    {
        _timestamps = capacity > 0 ? new Queue<double>(capacity) : new Queue<double>();
    }

    public bool TryCaptureFromShowInfo(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = ShowInfoPtsRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups["pts"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pts))
        {
            return false;
        }

        lock (_sync)
        {
            if (_snapshot.Count == 0 || pts >= _snapshot[^1])
            {
                _timestamps.Enqueue(pts);
                _snapshot.Add(pts);
                _available.Release();
            }
        }

        return true;
    }

    public double[] Snapshot()
    {
        lock (_sync)
        {
            return _snapshot.ToArray();
        }
    }

    public bool TryDequeue(out double timestamp)
    {
        lock (_sync)
        {
            if (_timestamps.Count > 0)
            {
                timestamp = _timestamps.Dequeue();
                return true;
            }
        }

        timestamp = 0;
        return false;
    }

    public async ValueTask<double?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_sync)
            {
                if (_timestamps.Count > 0)
                {
                    return _timestamps.Dequeue();
                }

                if (_completed)
                {
                    return null;
                }
            }

            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Complete()
    {
        lock (_sync)
        {
            _completed = true;
        }

        _available.Release();
    }

    public bool HasValues
    {
        get
        {
            lock (_sync)
            {
                return _snapshot.Count > 0;
            }
        }
    }
}

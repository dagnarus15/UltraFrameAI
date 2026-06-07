using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace UltraFrameAI;

internal sealed record DetectedGpuDevice(
    int DeviceId,
    int? BackendGpuId,
    string Name,
    string? HardwareKey,
    long? MemoryMb,
    int? MaxGraphicsClockMhz,
    int? PowerLimitW,
    long SortScore);

internal static class GpuDeviceDetector
{
    public static IReadOnlyList<DetectedGpuDevice> DetectDevices()
    {
        var vulkanDevices = TryDetectVulkanDevices();
        var wmiDevices = TryDetectWmiDevices();
        var nvidiaDevices = TryDetectNvidiaDevices();

        if (vulkanDevices.Count > 0)
        {
            return MergeRenderableDevices(vulkanDevices, wmiDevices, nvidiaDevices);
        }

        if (wmiDevices.Count == 0)
        {
            return nvidiaDevices;
        }

        if (nvidiaDevices.Count == 0)
        {
            return wmiDevices;
        }

        return MergeDevices(wmiDevices, nvidiaDevices);
    }

    private static IReadOnlyList<DetectedGpuDevice> TryDetectVulkanDevices()
    {
        try
        {
            const string vulkanInfoPath = @"C:\WINDOWS\system32\vulkaninfo.exe";
            if (!File.Exists(vulkanInfoPath))
            {
                return Array.Empty<DetectedGpuDevice>();
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = vulkanInfoPath,
                    Arguments = "--summary",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<DetectedGpuDevice>();
            }

            var devices = new List<DetectedGpuDevice>();
            int? currentBackendGpuId = null;
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)
                    && line.EndsWith(":", StringComparison.Ordinal))
                {
                    var gpuToken = line[3..^1];
                    currentBackendGpuId = int.TryParse(gpuToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGpuId)
                        ? parsedGpuId
                        : null;
                    continue;
                }

                if (!line.StartsWith("deviceName", StringComparison.OrdinalIgnoreCase) || currentBackendGpuId is not int backendGpuId)
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
                {
                    continue;
                }

                var deviceName = line[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(deviceName))
                {
                    continue;
                }

                devices.Add(new DetectedGpuDevice(
                    devices.Count,
                    backendGpuId,
                    deviceName,
                    null,
                    null,
                    null,
                    null,
                    ComputeSortScore(deviceName, null, null, null)));
                currentBackendGpuId = null;
            }

            return devices
                .OrderByDescending(device => device.SortScore)
                .ThenBy(device => device.DeviceId)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DetectedGpuDevice>();
        }
    }

    private static IReadOnlyList<DetectedGpuDevice> TryDetectNvidiaDevices()
    {
        try
        {
            var nvidiaSmiPath = FindNvidiaSmiPath();
            if (string.IsNullOrWhiteSpace(nvidiaSmiPath))
            {
                return Array.Empty<DetectedGpuDevice>();
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = nvidiaSmiPath,
                    Arguments = "--query-gpu=index,name,memory.total,clocks.max.graphics,power.limit --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<DetectedGpuDevice>();
            }

            var devices = new List<DetectedGpuDevice>();
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawLine.Split(',');
                if (parts.Length < 5)
                {
                    continue;
                }

                if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var deviceId))
                {
                    continue;
                }

                var name = parts[1].Trim();
                var memoryMb = TryParseInt(parts[2]);
                var graphicsClock = TryParseInt(parts[3]);
                var powerLimit = TryParseInt(parts[4]);

                devices.Add(new DetectedGpuDevice(
                    deviceId,
                    deviceId,
                    name,
                    null,
                    memoryMb,
                    graphicsClock,
                    powerLimit,
                    ComputeSortScore(name, memoryMb, graphicsClock, powerLimit)));
            }

            return devices
                .OrderByDescending(device => device.SortScore)
                .ThenBy(device => device.DeviceId)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DetectedGpuDevice>();
        }
    }

    private static IReadOnlyList<DetectedGpuDevice> TryDetectWmiDevices()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name,AdapterRAM,PNPDeviceID FROM Win32_VideoController");
            using var results = searcher.Get();
            var devices = new List<DetectedGpuDevice>();
            var nextDeviceId = 0;

            foreach (var result in results.Cast<ManagementObject>())
            {
                var name = Convert.ToString(result["Name"], CultureInfo.InvariantCulture)?.Trim();
                if (string.IsNullOrWhiteSpace(name) || IsSoftwareAdapter(name))
                {
                    continue;
                }

                var adapterRamBytes = TryConvertToInt64(result["AdapterRAM"]);
                var memoryMb = adapterRamBytes > 0 ? adapterRamBytes / (1024 * 1024) : null;
                var hardwareKey = Convert.ToString(result["PNPDeviceID"], CultureInfo.InvariantCulture)?.Trim();

                devices.Add(new DetectedGpuDevice(
                    nextDeviceId++,
                    null,
                    name,
                    hardwareKey,
                    memoryMb,
                    null,
                    null,
                    ComputeSortScore(name, memoryMb, null, null)));
            }

            return devices
                .OrderByDescending(device => device.SortScore)
                .ThenBy(device => device.DeviceId)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DetectedGpuDevice>();
        }
    }

    private static bool IsSoftwareAdapter(string name)
    {
        return name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("RDP", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Citrix", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Miracast", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Parsec Virtual", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DetectedGpuDevice> MergeRenderableDevices(
        IReadOnlyList<DetectedGpuDevice> vulkanDevices,
        IReadOnlyList<DetectedGpuDevice> wmiDevices,
        IReadOnlyList<DetectedGpuDevice> nvidiaDevices)
    {
        var merged = vulkanDevices
            .Select((device, index) => device with { DeviceId = index })
            .ToList();

        EnrichFromDeviceGroups(
            merged,
            wmiDevices.Select(device => device with { HardwareKey = NormalizeHardwareKey(device.HardwareKey) }).ToArray(),
            preferExistingName: true);

        EnrichFromDeviceGroups(merged, nvidiaDevices, preferExistingName: true);

        return merged
            .OrderByDescending(device => device.SortScore)
            .ThenBy(device => device.DeviceId)
            .ToArray();
    }

    private static void EnrichFromDeviceGroups(
        List<DetectedGpuDevice> targetDevices,
        IReadOnlyList<DetectedGpuDevice> sourceDevices,
        bool preferExistingName)
    {
        var sourceGroups = sourceDevices
            .GroupBy(device => NormalizeDeviceName(device.Name), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new Queue<DetectedGpuDevice>(group.OrderBy(device => device.DeviceId)),
                StringComparer.Ordinal);

        for (var index = 0; index < targetDevices.Count; index++)
        {
            var targetDevice = targetDevices[index];
            var normalizedName = NormalizeDeviceName(targetDevice.Name);
            if (!sourceGroups.TryGetValue(normalizedName, out var matches) || matches.Count == 0)
            {
                continue;
            }

            var sourceDevice = matches.Dequeue();
            var resolvedName = preferExistingName ? targetDevice.Name : sourceDevice.Name;
            targetDevices[index] = targetDevice with
            {
                Name = resolvedName,
                HardwareKey = targetDevice.HardwareKey ?? NormalizeHardwareKey(sourceDevice.HardwareKey),
                MemoryMb = sourceDevice.MemoryMb ?? targetDevice.MemoryMb,
                MaxGraphicsClockMhz = sourceDevice.MaxGraphicsClockMhz ?? targetDevice.MaxGraphicsClockMhz,
                PowerLimitW = sourceDevice.PowerLimitW ?? targetDevice.PowerLimitW,
                SortScore = ComputeSortScore(
                    resolvedName,
                    sourceDevice.MemoryMb ?? targetDevice.MemoryMb,
                    sourceDevice.MaxGraphicsClockMhz ?? targetDevice.MaxGraphicsClockMhz,
                    sourceDevice.PowerLimitW ?? targetDevice.PowerLimitW)
            };
        }
    }

    private static IReadOnlyList<DetectedGpuDevice> MergeDevices(
        IReadOnlyList<DetectedGpuDevice> wmiDevices,
        IReadOnlyList<DetectedGpuDevice> nvidiaDevices)
    {
        var merged = wmiDevices
            .Select(device => device with { HardwareKey = NormalizeHardwareKey(device.HardwareKey) })
            .ToList();
        var nextDeviceId = merged.Count == 0 ? 0 : merged.Max(device => device.DeviceId) + 1;

        var wmiNvidiaGroups = merged
            .Where(device => LooksLikeNvidiaDevice(device.Name))
            .GroupBy(device => NormalizeDeviceName(device.Name), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new Queue<int>(group
                    .OrderBy(device => device.DeviceId)
                    .Select(device => device.DeviceId)),
                StringComparer.Ordinal);

        foreach (var nvidiaDevice in nvidiaDevices.OrderBy(device => device.DeviceId))
        {
            var normalizedName = NormalizeDeviceName(nvidiaDevice.Name);
            if (wmiNvidiaGroups.TryGetValue(normalizedName, out var matchingIds) && matchingIds.Count > 0)
            {
                var matchedDeviceId = matchingIds.Dequeue();
                var matchIndex = merged.FindIndex(device => device.DeviceId == matchedDeviceId);
                if (matchIndex >= 0)
                {
                    var wmiDevice = merged[matchIndex];
                    merged[matchIndex] = wmiDevice with
                    {
                        BackendGpuId = nvidiaDevice.BackendGpuId,
                        MemoryMb = nvidiaDevice.MemoryMb ?? wmiDevice.MemoryMb,
                        MaxGraphicsClockMhz = nvidiaDevice.MaxGraphicsClockMhz,
                        PowerLimitW = nvidiaDevice.PowerLimitW,
                        SortScore = ComputeSortScore(
                            wmiDevice.Name,
                            nvidiaDevice.MemoryMb ?? wmiDevice.MemoryMb,
                            nvidiaDevice.MaxGraphicsClockMhz,
                            nvidiaDevice.PowerLimitW)
                    };
                    continue;
                }
            }

            merged.Add(nvidiaDevice with
            {
                DeviceId = nextDeviceId++,
                HardwareKey = NormalizeHardwareKey(nvidiaDevice.HardwareKey)
            });
        }

        return merged
            .OrderByDescending(device => device.SortScore)
            .ThenBy(device => device.DeviceId)
            .ToArray();
    }

    private static string NormalizeDeviceName(string name)
    {
        return string.Join(
            ' ',
            name.Trim()
                .ToUpperInvariant()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeHardwareKey(string? hardwareKey)
    {
        if (string.IsNullOrWhiteSpace(hardwareKey))
        {
            return null;
        }

        return hardwareKey.Trim().ToUpperInvariant();
    }

    private static bool LooksLikeNvidiaDevice(string name)
    {
        return name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("GEFORCE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("QUADRO", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("TESLA", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("GTX", StringComparison.OrdinalIgnoreCase);
    }

    private static long ComputeSortScore(string? name, long? memoryMb, int? graphicsClockMhz, int? powerLimitW)
    {
        var architectureScore = GetArchitectureScore(name);
        var vramScore = GetVramScore(memoryMb);
        var clockScore = GetClockScore(graphicsClockMhz);
        var powerScore = GetPowerScore(powerLimitW);

        var weightedScore =
            (architectureScore * 0.45d) +
            (vramScore * 0.28d) +
            (clockScore * 0.17d) +
            (powerScore * 0.10d);

        return (long)Math.Round(weightedScore * 1000d, MidpointRounding.AwayFromZero);
    }

    private static double GetVramScore(long? memoryMb)
    {
        if (memoryMb is not > 0)
        {
            return 0d;
        }

        var vramGb = Math.Min(16d, memoryMb.Value / 1024d);
        return Math.Sqrt(vramGb / 16d) * 1000d;
    }

    private static double GetClockScore(int? graphicsClockMhz)
    {
        if (graphicsClockMhz is not > 0)
        {
            return 0d;
        }

        var normalized = Math.Clamp((graphicsClockMhz.Value - 800d) / 2200d, 0d, 1d);
        return normalized * 1000d;
    }

    private static double GetPowerScore(int? powerLimitW)
    {
        if (powerLimitW is not > 0)
        {
            return 0d;
        }

        var normalized = Math.Clamp((powerLimitW.Value - 60d) / 290d, 0d, 1d);
        return normalized * 1000d;
    }

    private static double GetArchitectureScore(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 350d;
        }

        var text = name.ToUpperInvariant();

        var classScore = GetGpuClassScore(text);
        var modernBonus = GetModernFamilyBonus(text);
        var legacyPenalty = GetLegacyPenalty(text);
        return Math.Clamp(classScore + modernBonus - legacyPenalty, 80d, 1000d);
    }

    private static double GetGpuClassScore(string text)
    {
        if (LooksLikeIntegratedGpu(text))
        {
            return 180d;
        }

        if (LooksLikeIntelArc(text))
        {
            return 780d;
        }

        if (LooksLikeModernDiscreteGpu(text))
        {
            return 860d;
        }

        if (LooksLikeLegacyDiscreteGpu(text))
        {
            return 460d;
        }

        if (LooksLikeDiscreteGpu(text))
        {
            return 620d;
        }

        return 350d;
    }

    private static double GetModernFamilyBonus(string text)
    {
        var bonus = 0d;

        if (text.Contains("RTX"))
        {
            bonus += 80d;
        }

        if (text.Contains("ARC"))
        {
            bonus += 60d;
        }

        if (text.Contains("RX "))
        {
            bonus += 50d;
        }

        if (text.Contains("GTX 16"))
        {
            bonus += 35d;
        }

        if (text.Contains("GTX 10"))
        {
            bonus += 15d;
        }

        return bonus;
    }

    private static double GetLegacyPenalty(string text)
    {
        var penalty = 0d;

        if (text.Contains("RADEON HD") || text.Contains(" HD "))
        {
            penalty += 220d;
        }

        if (text.Contains("R7") || text.Contains("R9"))
        {
            penalty += 90d;
        }

        if (text.Contains("GT ") || text.Contains("GTS"))
        {
            penalty += 140d;
        }

        if (text.Contains("UHD") || text.Contains("IRIS"))
        {
            penalty += 40d;
        }

        return penalty;
    }

    private static bool LooksLikeIntegratedGpu(string text)
    {
        return text.Contains("UHD") ||
               text.Contains("IRIS") ||
               text.Contains("RADEON(TM) GRAPHICS") ||
               text.Contains("RADEON GRAPHICS") ||
               text.Contains("VEGA ") ||
               text.Contains(" XE") ||
               text.StartsWith("XE ");
    }

    private static bool LooksLikeIntelArc(string text)
    {
        return text.Contains("INTEL ARC") || text.Contains(" ARC ");
    }

    private static bool LooksLikeModernDiscreteGpu(string text)
    {
        return text.Contains("RTX") ||
               text.Contains(" ARC ") ||
               text.Contains("INTEL ARC") ||
               text.Contains("RX ");
    }

    private static bool LooksLikeLegacyDiscreteGpu(string text)
    {
        return text.Contains("RADEON HD") ||
               text.Contains(" HD ") ||
               text.Contains("R7") ||
               text.Contains("R9") ||
               text.Contains("GTX") ||
               text.Contains("GT ") ||
               text.Contains("GTS") ||
               text.Contains("QUADRO") ||
               text.Contains("TESLA");
    }

    private static bool LooksLikeDiscreteGpu(string text)
    {
        return text.Contains("NVIDIA") ||
               text.Contains("GEFORCE") ||
               text.Contains("RADEON") ||
               text.Contains("ARC") ||
               text.Contains("QUADRO") ||
               text.Contains("TESLA");
    }

    private static int? TryParseInt(string raw)
    {
        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static long? TryConvertToInt64(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                int intValue => intValue,
                uint uintValue => uintValue,
                long longValue => longValue,
                ulong ulongValue when ulongValue <= long.MaxValue => (long)ulongValue,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? FindNvidiaSmiPath()
    {
        var candidates = new[]
        {
            "nvidia-smi.exe",
            Path.Combine(AppContext.BaseDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim(), "nvidia-smi.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }
}

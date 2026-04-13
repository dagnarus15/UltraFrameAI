using System.Globalization;
using System.Runtime.InteropServices;
using System.Linq;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed record HardwareAssessmentLine(string Title, string Verdict, string Detail);

public sealed record HardwareAssessment(
    IReadOnlyList<HardwareAssessmentLine> Lines,
    bool HasBlockingIssue);

public sealed record StartupBenchmarkPracticalRequirements(
    double MinimumRamGb,
    double ComfortableRamGb,
    double MinimumVramGb,
    double ComfortableVramGb,
    double PeakRamGb,
    double PeakVramGb);

internal static class HardwareAssessmentBuilder
{
    private const int MinimumCpuLogicalCores = 4;
    private const int RecommendedCpuLogicalCores = 8;
    private const double MinimumRamGb = 8;
    private const double RecommendedRamGb = 16;
    private const double MinimumVramGb = 4;
    private const double RecommendedVramGb = 6;
    private const double MinimumPracticalGpuFps = 8;

    public static HardwareAssessment BuildStatic(
        IReadOnlyList<StartupBenchmarkGpuCandidate> gpuCandidates,
        StartupBenchmarkPracticalRequirements? practicalRequirements = null)
    {
        var lines = new List<HardwareAssessmentLine>();
        var logicalCores = Environment.ProcessorCount;
        var totalRamGb = GetInstalledRamBytes() / 1024d / 1024d / 1024d;
        var strongestGpu = gpuCandidates.OrderByDescending(candidate => candidate.MemoryMb ?? 0).FirstOrDefault();
        var vramGb = ((strongestGpu?.MemoryMb) ?? 0) / 1024d;
        var gpuRecommendations = GetGpuRecommendations(strongestGpu?.Label);
        var minimumRamGb = practicalRequirements?.MinimumRamGb ?? MinimumRamGb;
        var comfortableRamGb = practicalRequirements?.ComfortableRamGb ?? RecommendedRamGb;
        var peakRamGb = practicalRequirements?.PeakRamGb;
        var minimumVramGb = practicalRequirements?.MinimumVramGb ?? MinimumVramGb;
        var comfortableVramGb = practicalRequirements?.ComfortableVramGb ?? RecommendedVramGb;
        var peakVramGb = practicalRequirements?.PeakVramGb;

        if (strongestGpu is null)
        {
            lines.Add(new HardwareAssessmentLine(
                LocalizedStrings.HardwareGpuTitle,
                LocalizedStrings.HardwareTooLow,
                string.Format(
                    CultureInfo.InvariantCulture,
                    LocalizedStrings.HardwareGpuMissingDetail,
                    gpuRecommendations.Minimum,
                    gpuRecommendations.Comfortable)));
        }
        else
        {
            lines.Add(new HardwareAssessmentLine(
                LocalizedStrings.HardwareGpuTitle,
                LocalizedStrings.HardwareEnough,
                string.Format(
                    CultureInfo.InvariantCulture,
                    LocalizedStrings.HardwareGpuStaticEnoughDetail,
                    strongestGpu.Label,
                    gpuRecommendations.Comfortable)));
        }

        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareVramTitle,
            vramGb >= minimumVramGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow,
            peakVramGb is > 0
                ? vramGb >= minimumVramGb
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        vramGb >= comfortableVramGb ? LocalizedStrings.HardwareVramBenchmarkEnoughDetail : LocalizedStrings.HardwareVramBenchmarkMinimumDetail,
                        vramGb,
                        peakVramGb.Value,
                        minimumVramGb,
                        comfortableVramGb)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        LocalizedStrings.HardwareVramBenchmarkLowDetail,
                        vramGb,
                        peakVramGb.Value,
                        minimumVramGb,
                        comfortableVramGb)
                : vramGb >= minimumVramGb
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        vramGb >= comfortableVramGb ? LocalizedStrings.HardwareVramEnoughDetail : LocalizedStrings.HardwareVramMinimumDetail,
                        vramGb,
                        comfortableVramGb)
                    : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareVramLowDetail, vramGb, minimumVramGb, comfortableVramGb)));

        var cpuVerdict = logicalCores >= MinimumCpuLogicalCores ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow;
        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareCpuTitle,
            cpuVerdict,
            logicalCores >= MinimumCpuLogicalCores
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    logicalCores >= RecommendedCpuLogicalCores ? LocalizedStrings.HardwareCpuEnoughDetail : LocalizedStrings.HardwareCpuMinimumDetail,
                    logicalCores,
                    RecommendedCpuLogicalCores)
                : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareCpuLowDetail, logicalCores, MinimumCpuLogicalCores, RecommendedCpuLogicalCores)));

        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareRamTitle,
            totalRamGb >= minimumRamGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow,
            peakRamGb is > 0
                ? totalRamGb >= minimumRamGb
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        totalRamGb >= comfortableRamGb ? LocalizedStrings.HardwareRamBenchmarkEnoughDetail : LocalizedStrings.HardwareRamBenchmarkMinimumDetail,
                        totalRamGb,
                        peakRamGb.Value,
                        minimumRamGb,
                        comfortableRamGb)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        LocalizedStrings.HardwareRamBenchmarkLowDetail,
                        totalRamGb,
                        peakRamGb.Value,
                        minimumRamGb,
                        comfortableRamGb)
                : totalRamGb >= minimumRamGb
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        totalRamGb >= comfortableRamGb ? LocalizedStrings.HardwareRamEnoughDetail : LocalizedStrings.HardwareRamMinimumDetail,
                        totalRamGb,
                        comfortableRamGb)
                    : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareRamLowDetail, totalRamGb, minimumRamGb, comfortableRamGb)));

        var hasBlockingIssue = strongestGpu is null || vramGb < minimumVramGb || logicalCores < MinimumCpuLogicalCores || totalRamGb < minimumRamGb;
        return new HardwareAssessment(lines, hasBlockingIssue);
    }

    public static HardwareAssessment Build(StartupBenchmarkReport report)
    {
        var lines = new List<HardwareAssessmentLine>();
        var logicalCores = Environment.ProcessorCount;
        var totalRamGb = GetInstalledRamBytes() / 1024d / 1024d / 1024d;
        var vramGb = (report.Recommendation.GpuMemoryMb ?? 0) / 1024d;
        var gpuFps = report.Recommendation.ThroughputFps;
        var gpuRecommendations = GetGpuRecommendations(report.Recommendation.GpuLabel);
        var requirements = DerivePracticalRequirements(report);

        var gpuVerdict = gpuFps >= MinimumPracticalGpuFps ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareWeak;
        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareGpuTitle,
            gpuVerdict,
            gpuFps >= MinimumPracticalGpuFps
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    LocalizedStrings.HardwareGpuEnoughDetail,
                    report.Recommendation.GpuLabel,
                    gpuFps,
                    gpuRecommendations.Comfortable)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    LocalizedStrings.HardwareGpuWeakDetail,
                    report.Recommendation.GpuLabel,
                    gpuFps,
                    MinimumPracticalGpuFps,
                    gpuRecommendations.Minimum,
                    gpuRecommendations.Comfortable)));

        var vramVerdict = vramGb >= MinimumVramGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow;
        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareVramTitle,
            vramGb >= requirements.MinimumVramGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow,
            vramGb >= requirements.MinimumVramGb
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    vramGb >= requirements.ComfortableVramGb ? LocalizedStrings.HardwareVramBenchmarkEnoughDetail : LocalizedStrings.HardwareVramBenchmarkMinimumDetail,
                    vramGb,
                    requirements.PeakVramGb,
                    requirements.MinimumVramGb,
                    requirements.ComfortableVramGb)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    LocalizedStrings.HardwareVramBenchmarkLowDetail,
                    vramGb,
                    requirements.PeakVramGb,
                    requirements.MinimumVramGb,
                    requirements.ComfortableVramGb)));

        var cpuVerdict = logicalCores >= MinimumCpuLogicalCores ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow;
        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareCpuTitle,
            cpuVerdict,
            logicalCores >= MinimumCpuLogicalCores
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    logicalCores >= RecommendedCpuLogicalCores ? LocalizedStrings.HardwareCpuEnoughDetail : LocalizedStrings.HardwareCpuMinimumDetail,
                    logicalCores,
                    RecommendedCpuLogicalCores)
                : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareCpuLowDetail, logicalCores, MinimumCpuLogicalCores, RecommendedCpuLogicalCores)));

        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareRamTitle,
            totalRamGb >= requirements.MinimumRamGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow,
            totalRamGb >= requirements.MinimumRamGb
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    totalRamGb >= requirements.ComfortableRamGb ? LocalizedStrings.HardwareRamBenchmarkEnoughDetail : LocalizedStrings.HardwareRamBenchmarkMinimumDetail,
                    totalRamGb,
                    requirements.PeakRamGb,
                    requirements.MinimumRamGb,
                    requirements.ComfortableRamGb)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    LocalizedStrings.HardwareRamBenchmarkLowDetail,
                    totalRamGb,
                    requirements.PeakRamGb,
                    requirements.MinimumRamGb,
                    requirements.ComfortableRamGb)));

        var hasBlockingIssue = gpuFps <= 0.1
            || vramGb < requirements.MinimumVramGb
            || logicalCores < MinimumCpuLogicalCores
            || totalRamGb < requirements.MinimumRamGb;
        return new HardwareAssessment(lines, hasBlockingIssue);
    }

    public static StartupBenchmarkPracticalRequirements DerivePracticalRequirements(StartupBenchmarkReport report)
    {
        var recommendedCase = report.Results.FirstOrDefault(result =>
            result.Success
            && result.GpuId == report.Recommendation.GpuId
            && string.Equals(result.GpuLabel, report.Recommendation.GpuLabel, StringComparison.Ordinal)
            && string.Equals(result.UpscalerThreads, report.Recommendation.UpscalerThreads, StringComparison.Ordinal)
            && string.Equals(result.EncoderPreset, report.Recommendation.EncoderPreset, StringComparison.Ordinal)
            && result.TileSize == report.Recommendation.TileSize);

        var fallbackCase = recommendedCase
            ?? report.Results
                .Where(result => result.Success)
                .OrderBy(result => result.Elapsed)
                .FirstOrDefault();

        var peakRamGb = ComputeAdditionalUsageGiB(fallbackCase?.Metrics.StartRam, fallbackCase?.Metrics.PeakRam, 1.5);
        var peakVramGb = ComputeAdditionalUsageGiB(fallbackCase?.Metrics.StartVram, fallbackCase?.Metrics.PeakVram, 1.0);

        var minimumRamGb = NormalizeHalfGb(Math.Max(2.0, peakRamGb + 0.5));
        var comfortableRamGb = NormalizeHalfGb(Math.Max(minimumRamGb + 1.0, peakRamGb * 1.6));
        var minimumVramGb = NormalizeHalfGb(Math.Max(1.5, peakVramGb + 0.5));
        var comfortableVramGb = NormalizeHalfGb(Math.Max(minimumVramGb + 0.5, peakVramGb * 1.5));

        return new StartupBenchmarkPracticalRequirements(
            minimumRamGb,
            comfortableRamGb,
            minimumVramGb,
            comfortableVramGb,
            peakRamGb,
            peakVramGb);
    }

    private static double NormalizeHalfGb(double value)
    {
        return Math.Ceiling(value * 2.0) / 2.0;
    }

    private static double ComputeAdditionalUsageGiB(double? startBytes, double? peakBytes, double fallbackGiB)
    {
        if (startBytes is > 0 && peakBytes is > 0)
        {
            var deltaBytes = Math.Max(0, peakBytes.Value - startBytes.Value);
            var deltaGiB = deltaBytes / 1024d / 1024d / 1024d;
            if (deltaGiB > 0.05)
            {
                return deltaGiB;
            }
        }

        return fallbackGiB;
    }

    private static ulong GetInstalledRamBytes()
    {
        var memoryStatus = new MEMORYSTATUSEX();
        memoryStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        return GlobalMemoryStatusEx(ref memoryStatus) ? memoryStatus.ullTotalPhys : 0UL;
    }

    private static (string Minimum, string Comfortable) GetGpuRecommendations(string? gpuLabel)
    {
        var brand = DetectGpuBrand(gpuLabel);
        return brand switch
        {
            GpuBrand.Nvidia => (LocalizedStrings.HardwareGpuMinimumNvidia, LocalizedStrings.HardwareGpuComfortableNvidia),
            GpuBrand.Amd => (LocalizedStrings.HardwareGpuMinimumAmd, LocalizedStrings.HardwareGpuComfortableAmd),
            GpuBrand.Intel => (LocalizedStrings.HardwareGpuMinimumIntel, LocalizedStrings.HardwareGpuComfortableIntel),
            _ => (LocalizedStrings.HardwareGpuMinimumNvidia, LocalizedStrings.HardwareGpuComfortableNvidia)
        };
    }

    private static GpuBrand DetectGpuBrand(string? gpuLabel)
    {
        if (string.IsNullOrWhiteSpace(gpuLabel))
        {
            return GpuBrand.Unknown;
        }

        var text = gpuLabel.ToUpperInvariant();
        if (text.Contains("NVIDIA") ||
            text.Contains("GEFORCE") ||
            text.Contains("RTX") ||
            text.Contains("GTX") ||
            text.Contains("QUADRO") ||
            text.Contains("TESLA"))
        {
            return GpuBrand.Nvidia;
        }

        if (text.Contains("AMD") ||
            text.Contains("RADEON") ||
            text.Contains("RX ") ||
            text.Contains("FIREPRO"))
        {
            return GpuBrand.Amd;
        }

        if (text.Contains("INTEL") ||
            text.Contains("ARC"))
        {
            return GpuBrand.Intel;
        }

        return GpuBrand.Unknown;
    }

    private enum GpuBrand
    {
        Unknown,
        Nvidia,
        Amd,
        Intel
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}

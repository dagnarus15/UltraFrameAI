using System.Globalization;
using System.Runtime.InteropServices;
using UltraFrameAI.Resources;

namespace UltraFrameAI;

public sealed record HardwareAssessmentLine(string Title, string Verdict, string Detail);

public sealed record HardwareAssessment(
    IReadOnlyList<HardwareAssessmentLine> Lines,
    bool HasBlockingIssue);

internal static class HardwareAssessmentBuilder
{
    private const int MinimumCpuLogicalCores = 4;
    private const int RecommendedCpuLogicalCores = 8;
    private const double MinimumRamGb = 8;
    private const double RecommendedRamGb = 16;
    private const double MinimumVramGb = 4;
    private const double RecommendedVramGb = 6;
    private const double MinimumPracticalGpuFps = 8;

    public static HardwareAssessment BuildStatic(IReadOnlyList<StartupBenchmarkGpuCandidate> gpuCandidates)
    {
        var lines = new List<HardwareAssessmentLine>();
        var logicalCores = Environment.ProcessorCount;
        var totalRamGb = GetInstalledRamBytes() / 1024d / 1024d / 1024d;
        var strongestGpu = gpuCandidates.OrderByDescending(candidate => candidate.MemoryMb ?? 0).FirstOrDefault();
        var vramGb = ((strongestGpu?.MemoryMb) ?? 0) / 1024d;
        var gpuRecommendations = GetGpuRecommendations(strongestGpu?.Label);

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

        var vramVerdict = vramGb >= MinimumVramGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow;
        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareVramTitle,
            vramVerdict,
            vramGb >= MinimumVramGb
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    vramGb >= RecommendedVramGb ? LocalizedStrings.HardwareVramEnoughDetail : LocalizedStrings.HardwareVramMinimumDetail,
                    vramGb,
                    RecommendedVramGb)
                : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareVramLowDetail, vramGb, MinimumVramGb, RecommendedVramGb)));

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

        var ramVerdict = totalRamGb >= MinimumRamGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow;
        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareRamTitle,
            ramVerdict,
            totalRamGb >= MinimumRamGb
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    totalRamGb >= RecommendedRamGb ? LocalizedStrings.HardwareRamEnoughDetail : LocalizedStrings.HardwareRamMinimumDetail,
                    totalRamGb,
                    RecommendedRamGb)
                : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareRamLowDetail, totalRamGb, MinimumRamGb, RecommendedRamGb)));

        var hasBlockingIssue = strongestGpu is null || vramGb < MinimumVramGb || logicalCores < MinimumCpuLogicalCores || totalRamGb < MinimumRamGb;
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
            vramVerdict,
            vramGb >= MinimumVramGb
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    vramGb >= RecommendedVramGb ? LocalizedStrings.HardwareVramEnoughDetail : LocalizedStrings.HardwareVramMinimumDetail,
                    vramGb,
                    RecommendedVramGb)
                : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareVramLowDetail, vramGb, MinimumVramGb, RecommendedVramGb)));

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

        var ramVerdict = totalRamGb >= MinimumRamGb ? LocalizedStrings.HardwareEnough : LocalizedStrings.HardwareTooLow;
        lines.Add(new HardwareAssessmentLine(
            LocalizedStrings.HardwareRamTitle,
            ramVerdict,
            totalRamGb >= MinimumRamGb
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    totalRamGb >= RecommendedRamGb ? LocalizedStrings.HardwareRamEnoughDetail : LocalizedStrings.HardwareRamMinimumDetail,
                    totalRamGb,
                    RecommendedRamGb)
                : string.Format(CultureInfo.InvariantCulture, LocalizedStrings.HardwareRamLowDetail, totalRamGb, MinimumRamGb, RecommendedRamGb)));

        var hasBlockingIssue = gpuFps <= 0.1 || vramGb < MinimumVramGb || logicalCores < MinimumCpuLogicalCores || totalRamGb < MinimumRamGb;
        return new HardwareAssessment(lines, hasBlockingIssue);
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

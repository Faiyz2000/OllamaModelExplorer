using System.Runtime.InteropServices;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Estimates whether a model is likely to fit in currently available physical RAM.
/// The estimate intentionally includes runtime overhead because model file size alone
/// is not the same as the memory required while the model is loaded by Ollama.
/// </summary>
public static class RamEstimator
{
    private const long MiB = 1024L * 1024L;
    private const long GiB = 1024L * MiB;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static long GetAvailableRamBytes()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? checked((long)status.ullAvailPhys) : 0;
    }

    public static long GetTotalRamBytes()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? checked((long)status.ullTotalPhys) : 0;
    }

    /// <summary>
    /// Conservative local estimate: model file size + approximately 15% runtime
    /// overhead + 512 MiB. This accounts for loader/runtime allocations and avoids
    /// treating the on-disk GGUF size as the exact RAM requirement.
    /// </summary>
    public static long EstimateRequiredRamBytes(ModelInfo model)
    {
        if (model.SizeBytes <= 0)
            return 0;

        var overhead = Math.Max(512L * MiB, (long)(model.SizeBytes * 0.15));
        return checked(model.SizeBytes + overhead);
    }

    public static RamAssessment Assess(ModelInfo model)
    {
        var required = EstimateRequiredRamBytes(model);
        var available = GetAvailableRamBytes();

        if (required <= 0 || available <= 0)
            return new RamAssessment(required, available, RamStatus.Unknown);

        return new RamAssessment(required, available,
            required <= available ? RamStatus.Fits : RamStatus.NotEnough);
    }

    public static string FormatGiB(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        return $"{bytes / (double)GiB:0.0} GB";
    }
}

public enum RamStatus
{
    Unknown,
    Fits,
    NotEnough
}

public readonly record struct RamAssessment(long RequiredBytes, long AvailableBytes, RamStatus Status)
{
    public string Display
    {
        get
        {
            if (Status == RamStatus.Unknown)
                return "Unknown";

            var required = RamEstimator.FormatGiB(RequiredBytes);
            var available = RamEstimator.FormatGiB(AvailableBytes);
            var status = Status == RamStatus.Fits ? "OK" : "NOT ENOUGH";
            return $"~{required} / {available} free • {status}";
        }
    }
}

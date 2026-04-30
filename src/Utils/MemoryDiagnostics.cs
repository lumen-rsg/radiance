using System.Diagnostics;

namespace Radiance.Utils;

/// <summary>
/// Lightweight memory diagnostics for profiling Radiance's RAM usage.
/// Enabled via RADIANCE_DIAG=1 environment variable.
/// </summary>
public static class MemoryDiagnostics
{
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("RADIANCE_DIAG") == "1";

    public static void Snapshot(string label)
    {
        if (!Enabled) return;

        var gcInfo = GC.GetGCMemoryInfo();
        var process = Process.GetCurrentProcess();

        Console.Error.WriteLine($"[MEM] {label}:");
        Console.Error.WriteLine($"  GC Heap:       {GC.GetTotalMemory(false) / 1024:N0} KB");
        Console.Error.WriteLine($"  Gen0 Size:     {gcInfo.GenerationInfo[0].SizeAfterBytes / 1024:N0} KB");
        Console.Error.WriteLine($"  Gen1 Size:     {gcInfo.GenerationInfo[1].SizeAfterBytes / 1024:N0} KB");
        Console.Error.WriteLine($"  Gen2 Size:     {gcInfo.GenerationInfo[2].SizeAfterBytes / 1024:N0} KB");
        Console.Error.WriteLine($"  LOH Size:      {gcInfo.GenerationInfo[3].SizeAfterBytes / 1024:N0} KB");
        Console.Error.WriteLine($"  Working Set:   {process.WorkingSet64 / 1024:N0} KB");
        Console.Error.WriteLine($"  Private Mem:   {process.PrivateMemorySize64 / 1024:N0} KB");
        Console.Error.WriteLine($"  GC Collections: G0={GC.CollectionCount(0)} G1={GC.CollectionCount(1)} G2={GC.CollectionCount(2)}");
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Windows.Media;
using Microsoft.Win32;

namespace ChurchSlides.Spike;

/// <summary>System facts for S8: Windows build, .NET, CPU, instruction sets, WPF render tier, GPU, monitors.</summary>
public static class SystemInfo
{
    public static IReadOnlyList<(string Name, string Value)> Collect()
    {
        var facts = new List<(string, string)>();

        var os = new Native.OSVERSIONINFOEX { dwOSVersionInfoSize = Marshal.SizeOf<Native.OSVERSIONINFOEX>() };
        Native.RtlGetVersion(ref os);
        const string nt = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        // ProductName says "Windows 10" on Windows 11 too, so the build number decides.
        var windows = os.dwBuildNumber >= 22000 ? "Windows 11" : "Windows 10";
        facts.Add(("Windows", $"{windows} {Reg(nt, "EditionID")} {Reg(nt, "DisplayVersion")}"));
        facts.Add(("Windows build", $"{os.dwMajorVersion}.{os.dwMinorVersion}.{os.dwBuildNumber}.{Reg(nt, "UBR")}"));
        facts.Add((".NET", $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.ProcessArchitecture})"));

        const string cpu = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        facts.Add(("CPU", $"{Reg(cpu, "ProcessorNameString").Trim()} ({Environment.ProcessorCount} logical)"));
        facts.Add(("Instruction sets", InstructionSets()));

        var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref mem))
            facts.Add(("RAM", $"{mem.ullTotalPhys / (1024 * 1024)} MB total, {mem.ullAvailPhys / (1024 * 1024)} MB free"));

        facts.Add(("WPF RenderCapability.Tier", $"{RenderCapability.Tier >> 16} (2 = hardware)"));
        facts.Add(("WPF pixel shader 2.0 / 3.0", $"{RenderCapability.IsPixelShaderVersionSupported(2, 0)} / {RenderCapability.IsPixelShaderVersionSupported(3, 0)}"));
        facts.Add(("WPF max texture size", $"{RenderCapability.MaxHardwareTextureSize.Width}×{RenderCapability.MaxHardwareTextureSize.Height}"));
        facts.Add(("WPF process render mode", RenderOptions.ProcessRenderMode.ToString()));

        const string gpu = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
        facts.Add(("GPU driver", $"{Reg(gpu, "DriverDesc")}, version {Reg(gpu, "DriverVersion")}, dated {Reg(gpu, "DriverDate")}"));

        foreach (var m in Monitors.All())
        {
            facts.Add(($"Monitor {m.DeviceName}",
                $"{m.Bounds}{(m.IsPrimary ? " primary" : "")}, work area {m.WorkArea}, {m.DpiX} dpi ({m.DpiX * 100 / 96}%), {m.RefreshHz} Hz, {m.MonitorName} on {m.Adapter}"));
        }
        return facts;
    }

    private static string InstructionSets()
    {
        var sets = new (string, bool)[]
        {
            ("SSE2", Sse2.IsSupported), ("SSE3", Sse3.IsSupported), ("SSSE3", Ssse3.IsSupported),
            ("SSE4.1", Sse41.IsSupported), ("SSE4.2", Sse42.IsSupported), ("POPCNT", Popcnt.IsSupported),
            ("AVX", Avx.IsSupported), ("AVX2", Avx2.IsSupported),
        };
        var level = Sse42.IsSupported && Popcnt.IsSupported ? (Avx2.IsSupported ? "x86-64-v3" : "x86-64-v2") : "x86-64-v1";
        return string.Join(" ", sets.Select(s => $"{s.Item1}:{(s.Item2 ? "yes" : "no")}")) + $" → {level}";
    }

    private static string Reg(string key, string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(key);
            return k?.GetValue(name)?.ToString() ?? "?";
        }
        catch (System.Security.SecurityException)
        {
            return "(no access)";
        }
    }
}

using System.Runtime.InteropServices;

namespace ChurchSlides.Spike;

public sealed record MonitorInfo(
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary,
    uint DpiX,
    uint DpiY,
    int RefreshHz,
    string Adapter,
    string MonitorName)
{
    public string Label => $"{DeviceName}{(IsPrimary ? " (primary)" : "")} {Bounds}";
    public override string ToString() => Label;
}

/// <summary>Monitor enumeration in physical pixels (ARCH 7.5, S1).</summary>
public static class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var handles = new List<IntPtr>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            handles.Add(h);
            return true;
        }, IntPtr.Zero);

        var adapters = Adapters();
        var result = new List<MonitorInfo>();
        foreach (var h in handles)
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (!Native.GetMonitorInfo(h, ref info))
                continue;
            Native.GetDpiForMonitor(h, 0, out var dpiX, out var dpiY);
            var mode = new Native.DEVMODE { dmSize = (ushort)Marshal.SizeOf<Native.DEVMODE>() };
            var hz = Native.EnumDisplaySettings(info.szDevice, Native.ENUM_CURRENT_SETTINGS, ref mode)
                ? (int)mode.dmDisplayFrequency
                : 0;
            adapters.TryGetValue(info.szDevice, out var names);
            result.Add(new MonitorInfo(
                info.szDevice,
                info.rcMonitor.ToPixelRect(),
                info.rcWork.ToPixelRect(),
                (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                dpiX,
                dpiY,
                hz,
                names.Adapter ?? "?",
                names.Monitor ?? "?"));
        }
        return result;
    }

    /// <summary>The first non-primary monitor, else the primary.</summary>
    public static MonitorInfo? DefaultOutput(IReadOnlyList<MonitorInfo> all) =>
        all.FirstOrDefault(m => !m.IsPrimary) ?? all.FirstOrDefault();

    private static Dictionary<string, (string? Adapter, string? Monitor)> Adapters()
    {
        var map = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; ; i++)
        {
            var adapter = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            if (!Native.EnumDisplayDevices(null, i, ref adapter, 0))
                break;
            var monitor = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            var monitorName = Native.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0) ? monitor.DeviceString : null;
            map[adapter.DeviceName] = (adapter.DeviceString, monitorName);
        }
        return map;
    }
}

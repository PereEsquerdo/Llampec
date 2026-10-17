using Llampec.Interop;

namespace Llampec.Platform;

/// <summary>One desktop display: its GDI device name, monitor handle and bounds in physical pixels.</summary>
public sealed record ActiveDisplay(string DeviceName, string Name, bool Internal, nint Monitor, User32.RECT Bounds);

/// <summary>
/// The displays that are part of the desktop now. CCD supplies the friendly name and whether the panel
/// is built in; GDI supplies the monitor handle and bounds. A cloned source appears once, as internal
/// when any of its targets is.
/// </summary>
public static class ActiveDisplays
{
    public static IReadOnlyList<ActiveDisplay> Read()
    {
        DisplayConfig.DISPLAYCONFIG_PATH_INFO[] paths;
        try { paths = DisplayConfig.QueryPaths(DisplayConfig.QDC_ONLY_ACTIVE_PATHS, out _); }
        catch (InvalidOperationException error)
        {
            Diagnostics.Log.Warn($"Displays are unavailable: {error.Message}");
            return [];
        }
        var targets = new Dictionary<string, (string Name, bool Internal)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (path.targetInfo.targetAvailable == 0) continue;
            var source = new DisplayOrientationNative.SourceName
            {
                Header = Header(1, Marshal.SizeOf<DisplayOrientationNative.SourceName>(), path.sourceInfo.adapterId, path.sourceInfo.id),
            };
            if (DisplayOrientationNative.GetSourceName(ref source) != 0) continue;
            var target = new DisplayConfig.DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = Header(DisplayConfig.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    Marshal.SizeOf<DisplayConfig.DISPLAYCONFIG_TARGET_DEVICE_NAME>(), path.targetInfo.adapterId, path.targetInfo.id),
            };
            string name = DisplayConfig.DisplayConfigGetDeviceInfo(ref target) == 0 ? target.GetFriendlyName() : "";
            bool builtIn = IsInternal(path.targetInfo.outputTechnology);
            string device = source.GetName();
            targets[device] = targets.TryGetValue(device, out var existing)
                ? (existing.Name, existing.Internal || builtIn)
                : (string.IsNullOrWhiteSpace(name) ? (builtIn ? "Built-in display" : "Display") : name, builtIn);
        }

        var displays = new List<ActiveDisplay>(targets.Count);
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new User32.MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<User32.MONITORINFOEXW>() };
            if (User32.GetMonitorInfo(monitor, ref info))
            {
                string device = DeviceName(info);
                if (targets.TryGetValue(device, out var target))
                    displays.Add(new(device, target.Name, target.Internal, monitor, info.rcMonitor));
            }
            return true;
        };
        EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        return displays;
    }

    /// <summary>Internal, embedded DisplayPort, embedded UDI or LVDS (DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY).</summary>
    public static bool IsInternal(int outputTechnology) => outputTechnology is 6 or 11 or 13 or unchecked((int)0x80000000);

    private static unsafe string DeviceName(User32.MONITORINFOEXW info) => new(info.szDevice);

    private static DisplayConfig.DISPLAYCONFIG_DEVICE_INFO_HEADER Header(int type, int size, DisplayConfig.LUID adapter, uint id) =>
        new() { type = type, size = (uint)size, adapterId = adapter, id = id };

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
}

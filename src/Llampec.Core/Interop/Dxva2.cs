// P/Invoke declarations written from the Microsoft Win32 documentation (physicalmonitorenumerationapi.h,
// lowlevelmonitorconfigurationapi.h): the Monitor Configuration API, which sends MCCS VCP commands to
// external monitors over DDC/CI through the display driver.
// https://learn.microsoft.com/windows/win32/monitor/monitor-configuration
namespace Llampec.Interop;

public static partial class Dxva2
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PHYSICAL_MONITOR
    {
        public nint hPhysicalMonitor;
        public fixed char szPhysicalMonitorDescription[128];
    }

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint hMonitor, out uint count);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPhysicalMonitorsFromHMONITOR(nint hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] monitors);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVCPFeatureAndVCPFeatureReply(nint monitor, byte code, out uint type, out uint current, out uint maximum);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetVCPFeature(nint monitor, byte code, uint value);
}

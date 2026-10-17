using Llampec.Diagnostics;
using Llampec.Interop;

namespace Llampec.Platform;

/// <summary>What agent mode needs from the screen while it is active. Created and used on the UI thread.</summary>
public interface IAgentScreen : IDisposable
{
    /// <summary>Replaces the black covers with one per display in <paramref name="displays"/>.</summary>
    void Cover(IReadOnlyList<ActiveDisplay> displays);

    /// <summary>A key, button, wheel, deliberate mouse movement, touch, pen or touchpad contact from a physical device.</summary>
    event EventHandler? PhysicalInput;

    /// <summary>Windows turned the console display back on after turning it off.</summary>
    event EventHandler? DisplayTurnedOn;
}

/// <summary>
/// Agent mode's windows, which exist only while the mode is active:
/// <list type="bullet">
/// <item>Black covers. On OLED panels black pixels emit no light, so a cover stops wear and most of the
/// panel's consumption without removing the display from the desktop. Covers are click-through and
/// excluded from screen capture (WDA_EXCLUDEFROMCAPTURE), so agents and remote-desktop tools keep
/// seeing and using the desktop beneath. They are re-raised when the foreground window changes.</item>
/// <item>Physical input. Raw input with RIDEV_INPUTSINK reports which device produced each event;
/// input synthesized with SendInput, as agents and remote tools do, has no device and is ignored.</item>
/// <item>GUID_CONSOLE_DISPLAY_STATE, to notice Windows turning the screen back on, for example at the
/// sign-in screen, where this desktop's raw input is not delivered.</item>
/// </list>
/// </summary>
public sealed class AgentScreen : IAgentScreen
{
    private const string ClassName = "Llampec.AgentScreen";
    private const int MovementThreshold = 150; // Mouse counts; a bumped desk moves the pointer a few counts.
    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private readonly User32.WndProc _wndProc;
    private readonly WinEventProc _foregroundProc;
    private readonly nint _host;
    private readonly nint _powerNotification;
    private readonly nint _foregroundHook;
    private readonly List<nint> _covers = [];
    private readonly long _ignoreInputUntil;
    private bool _inputRegistered;
    private bool _displayWasOff;
    private long _movement;
    private bool _disposed;

    public event EventHandler? PhysicalInput;
    public event EventHandler? DisplayTurnedOn;

    /// <param name="grace">Input ignored after activation, such as releasing the tap that started the mode.</param>
    public AgentScreen(TimeSpan grace)
    {
        _wndProc = WindowProcedure;
        _foregroundProc = (_, _, _, _, _, _, _) => Raise();
        _ignoreInputUntil = Environment.TickCount64 + (long)grace.TotalMilliseconds;
        nint instance = Kernel32.GetModuleHandle(null);
        var wc = new User32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<User32.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            hbrBackground = GetStockObject(4), // BLACK_BRUSH
            lpszClassName = Marshal.StringToHGlobalUni(ClassName),
        };
        try
        {
            if (User32.RegisterClassEx(in wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
        }
        finally { Marshal.FreeHGlobal(wc.lpszClassName); }

        _host = User32.CreateWindowEx((uint)User32.WS_EX_TOOLWINDOW, ClassName, "Llampec agent mode", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_host == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            User32.UnregisterClass(ClassName, instance);
            throw new InvalidOperationException($"CreateWindowEx failed: {error}");
        }
        try
        {
            var devices = new RawInputDevice[]
            {
                new(0x01, 0x06, _host), // keyboard
                new(0x01, 0x02, _host), // mouse
                new(0x0D, 0x04, _host), // touch screen
                new(0x0D, 0x02, _host), // pen
                new(0x0D, 0x05, _host), // precision touchpad
            };
            _inputRegistered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
            if (!_inputRegistered)
            {
                // Digitizer collections are optional; keyboard and mouse are what matter.
                _inputRegistered = RegisterRawInputDevices(devices[..2], 2, (uint)Marshal.SizeOf<RawInputDevice>());
                if (!_inputRegistered) throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastPInvokeError()}");
            }
            Guid setting = ConsoleDisplayState;
            _powerNotification = RegisterPowerSettingNotification(_host, ref setting, 0);
            if (_powerNotification == 0) Log.Warn($"Display state notifications are unavailable: {Marshal.GetLastPInvokeError()}");
            _foregroundHook = SetWinEventHook(3, 3, 0, _foregroundProc, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, out of context
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Cover(IReadOnlyList<ActiveDisplay> displays)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RemoveCovers();
        nint instance = Kernel32.GetModuleHandle(null);
        foreach (var display in displays)
        {
            var bounds = display.Bounds;
            nint cover = User32.CreateWindowEx(ExtendedCoverStyle, ClassName, "", Popup,
                bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0, 0, instance, 0);
            if (cover == 0) { Log.Warn($"Display cover failed: {Marshal.GetLastPInvokeError()}"); continue; }
            _covers.Add(cover);
            SetLayeredWindowAttributes(cover, 0, 255, 2); // LWA_ALPHA: opaque, still click-through
            if (!SetWindowDisplayAffinity(cover, 0x11)) // WDA_EXCLUDEFROMCAPTURE
                Log.Warn($"Display cover is not excluded from capture: {Marshal.GetLastPInvokeError()}");
            ShowWindow(cover, 4); // SW_SHOWNOACTIVATE
        }
    }

    /// <summary>TOPMOST | TOOLWINDOW | LAYERED | TRANSPARENT | NOACTIVATE.</summary>
    private const uint ExtendedCoverStyle = 0x8 | 0x80 | 0x80000 | 0x20 | 0x08000000;
    private const uint Popup = 0x80000000;

    private void Raise()
    {
        foreach (nint cover in _covers)
            User32.SetWindowPos(cover, -1, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
    }

    private nint WindowProcedure(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            if (msg == 0x00FF && hWnd == _host) // WM_INPUT
            {
                if (IsPhysicalInput(lParam) && Environment.TickCount64 >= _ignoreInputUntil) PhysicalInput?.Invoke(this, EventArgs.Empty);
            }
            else if (msg == 0x0218 && wParam == 0x8013 && lParam != 0) // WM_POWERBROADCAST, PBT_POWERSETTINGCHANGE
            {
                var setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
                if (setting.PowerSetting == ConsoleDisplayState && setting.DataLength >= 4)
                {
                    uint state = (uint)Marshal.ReadInt32(lParam, 20); // 0 off, 1 on, 2 dimmed
                    if (state == 0) _displayWasOff = true;
                    else if (state == 1 && _displayWasOff) { _displayWasOff = false; DisplayTurnedOn?.Invoke(this, EventArgs.Empty); }
                }
                return 1;
            }
            else if (msg == 0x0084 && hWnd != _host) return -1; // WM_NCHITTEST: HTTRANSPARENT
        }
        catch (Exception error) { Log.Error("Agent mode window failed", error); }
        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private bool IsPhysicalInput(nint rawInput)
    {
        uint size = 0;
        uint header = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInput, 0x10000003, 0, ref size, header) != 0 || size == 0) return false; // RID_INPUT
        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInput, 0x10000003, buffer, ref size, header) != size) return false;
            var head = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (head.Device == 0) return false; // SendInput: an agent, a remote-desktop tool, or Llampec itself
            int data = Marshal.SizeOf<RawInputHeader>();
            switch (head.Type)
            {
                case 0: // RIM_TYPEMOUSE
                    ushort flags = (ushort)Marshal.ReadInt16(buffer, data);
                    ushort buttons = (ushort)Marshal.ReadInt16(buffer, data + 4);
                    if (buttons != 0) return true;
                    if ((flags & 1) != 0) return false; // Absolute positions come from pens or remote sessions.
                    _movement += Math.Abs(Marshal.ReadInt32(buffer, data + 12)) + Math.Abs(Marshal.ReadInt32(buffer, data + 16));
                    return _movement >= MovementThreshold;
                case 1: // RIM_TYPEKEYBOARD: key down only
                    return (Marshal.ReadInt16(buffer, data + 2) & 1) == 0;
                default: // RIM_TYPEHID: touch screen, pen or touchpad report
                    return true;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void RemoveCovers()
    {
        foreach (nint cover in _covers) User32.DestroyWindow(cover);
        _covers.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveCovers();
        if (_foregroundHook != 0) UnhookWinEvent(_foregroundHook);
        if (_powerNotification != 0) UnregisterPowerSettingNotification(_powerNotification);
        if (_inputRegistered)
        {
            var remove = new RawInputDevice[]
            {
                new(0x01, 0x06, 0, 1), new(0x01, 0x02, 0, 1),
                new(0x0D, 0x04, 0, 1), new(0x0D, 0x02, 0, 1), new(0x0D, 0x05, 0, 1),
            };
            // Removing a collection that was never added fails without affecting the others.
            foreach (var device in remove) RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>());
        }
        if (_host != 0) User32.DestroyWindow(_host);
        // The class holds this instance's window procedure; the next activation registers its own.
        User32.UnregisterClass(ClassName, Kernel32.GetModuleHandle(null));
        GC.KeepAlive(_wndProc);
        GC.KeepAlive(_foregroundProc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawInputDevice
    {
        public readonly ushort UsagePage, Usage;
        public readonly uint Flags;
        public readonly nint Target;
        /// <param name="remove">RIDEV_REMOVE; otherwise RIDEV_INPUTSINK, which also delivers background input.</param>
        public RawInputDevice(ushort page, ushort usage, nint target, int remove = 0)
        { UsagePage = page; Usage = usage; Target = target; Flags = remove != 0 ? 0x1u : 0x100u; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type, Size;
        public nint Device, WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid setting, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(nint handle);
    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int index);
}

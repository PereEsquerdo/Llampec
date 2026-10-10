// Independent implementation from Microsoft Win32 documentation; no injected hooks or polling thread.
using System.Text;
using Llampec.Diagnostics;

namespace Llampec.Actions.AlwaysOnTop;

internal sealed class NativeAlwaysOnTopWindowSystem : IAlwaysOnTopWindowSystem
{
    private const uint ForegroundEvent = 0x0003;
    private const uint ObjectCreate = 0x8000, ObjectDestroy = 0x8001, ObjectShow = 0x8002,
        ObjectHide = 0x8003, ObjectReorder = 0x8004, ObjectStateChange = 0x800A,
        ObjectLocationChange = 0x800B, ObjectNameChange = 0x800C, ObjectCloaked = 0x8017, ObjectUncloaked = 0x8018;
    private const long TopmostStyle = 0x00000008, ToolWindowStyle = 0x00000080,
        NoActivateStyle = 0x08000000, ChildStyle = 0x40000000, LayeredStyle = 0x00080000;
    private const uint AlphaFlag = 0x2 /* LWA_ALPHA */, ColorKeyFlag = 0x1 /* LWA_COLORKEY */;
    private const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010, NoOwnerZOrder = 0x0200;
    private readonly string _marker = "Llampec.AlwaysOnTop." + Guid.NewGuid().ToString("N");
    private readonly uint _processId = (uint)Environment.ProcessId;
    // Only the events handled below. The full 0x8000–0x8018 range also carries focus,
    // selection and value changes, all marshalled to this process for nothing.
    private static readonly (uint First, uint Last)[] WindowEventRanges =
        [(ObjectCreate, ObjectReorder), (ObjectStateChange, ObjectNameChange), (ObjectCloaked, ObjectUncloaked)];
    private readonly WinEventProc _callback;
    private readonly List<nint> _windowHooks = [];
    private readonly Dictionary<uint, List<nint>> _pinHooks = [];
    private readonly List<nint> _destroyHook = [];
    private nint _foregroundHook;
    private bool _disposed;

    public event Action<WindowChange>? WindowChanged;
    public bool IsTrackingForeground => _foregroundHook != 0;
    public nint ForegroundWindow => GetForegroundWindow();
    public string? LastError { get; private set; }

    public ForegroundWindowResolution ResolveForeground(nint capturedHandle)
    {
        if (capturedHandle == 0 || !IsWindow(capturedHandle))
            return new(null, false, "No eligible window is active.");
        GetWindowThreadProcessId(capturedHandle, out uint process);
        if (process == _processId) return new(null, true);
        // The foreground HWND can be an owned dialog or a native host. Resolve its
        // root and visible owner chain without ever choosing an unrelated background app.
        nint root = GetAncestor(capturedHandle, 2 /* GA_ROOT */);
        var visited = new HashSet<nint>();
        WindowSnapshot? firstEligible = null;
        for (nint handle = root; handle != 0 && visited.Add(handle); handle = GetWindow(handle, 4 /* GW_OWNER */))
        {
            if (Read(handle) is not { IsEligible: true } window) continue;
            // Owned dialogs inherit a parent's TOPMOST state. Toggle the pin that
            // Llampec actually owns when one exists in the active owner group.
            if (HasClaim(window.Identity)) return new(window, false);
            firstEligible ??= window;
        }
        if (firstEligible is not null) return new(firstEligible, false);
        // Every foreground change passes here (taskbar, desktop, Start); only a rejected
        // shortcut logs the reason.
        return new(null, false, $"ineligible foreground hwnd=0x{capturedHandle:X}, pid={process}");
    }

    public NativeAlwaysOnTopWindowSystem()
    {
        _callback = OnWinEvent;
        // WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS. Callbacks use this thread's message loop.
        _foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, 0, _callback, 0, 0, 2);
        if (_foregroundHook == 0)
            Log.Warn($"Always on Top foreground tracking hook unavailable: {Marshal.GetLastWin32Error()}");
    }

    /// <summary>Call on the thread that created this instance, which owns the message loop.</summary>
    public void SetWindowTracking(bool all, IReadOnlyCollection<WindowIdentity> pinned)
    {
        if (_disposed) return;
        if (all)
        {
            UnhookPins(_pinHooks.Keys.ToArray());
            Unhook(_destroyHook);
            if (_windowHooks.Count == 0) Hook(_windowHooks, WindowEventRanges, 0);
            return;
        }
        Unhook(_windowHooks);
        var wanted = pinned.Select(identity => identity.ProcessId).ToHashSet();
        UnhookPins(_pinHooks.Keys.Where(process => !wanted.Contains(process)).ToArray());
        foreach (uint process in wanted)
            if (!_pinHooks.ContainsKey(process)) Hook(_pinHooks[process] = [], WindowEventRanges, process);
        // A process-scoped hook ends with its process, which can drop the destruction of a window
        // whose app exits on close. Destruction alone is still observed session-wide.
        if (wanted.Count == 0) Unhook(_destroyHook);
        else if (_destroyHook.Count == 0) Hook(_destroyHook, [(ObjectDestroy, ObjectDestroy)], 0);
    }

    private void Hook(List<nint> hooks, (uint First, uint Last)[] ranges, uint process)
    {
        // WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS, scoped to one process when given.
        foreach (var (first, last) in ranges)
            if (SetWinEventHook(first, last, 0, _callback, process, 0, 2) is var hook and not 0) hooks.Add(hook);
        if (hooks.Count != ranges.Length)
            Log.Warn($"Always on Top window tracking hook unavailable: {Marshal.GetLastWin32Error()}");
    }

    private void UnhookPins(IEnumerable<uint> processes)
    {
        foreach (uint process in processes)
            if (_pinHooks.Remove(process, out var hooks)) Unhook(hooks);
    }

    private static void Unhook(List<nint> hooks)
    {
        foreach (nint hook in hooks) _ = UnhookWinEvent(hook);
        hooks.Clear();
    }

    public bool IsTopmost(WindowIdentity identity) => Matches(identity) && IsTopmost(identity.Handle);

    public WindowSnapshot? Read(nint handle)
    {
        if (handle == 0 || !IsWindow(handle)) return null;
        uint threadId = GetWindowThreadProcessId(handle, out uint processId);
        if (threadId == 0 || processId == 0) return null;
        var identity = new WindowIdentity(handle, processId, threadId);
        long extendedStyle = GetWindowLongPtr(handle, -20).ToInt64();
        var title = new StringBuilder(Math.Clamp(GetWindowTextLength(handle) + 1, 2, 4096));
        _ = GetWindowText(handle, title, title.Capacity);
        string caption = title.ToString().Trim();
        bool eligible = processId != _processId && IsWindowVisible(handle)
            && !string.IsNullOrWhiteSpace(caption)
            && (extendedStyle & (ToolWindowStyle | NoActivateStyle)) == 0
            && (GetWindowLongPtr(handle, -16).ToInt64() & ChildStyle) == 0
            && GetAncestor(handle, 2 /* GA_ROOT */) == handle
            && (DwmGetWindowAttribute(handle, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) != 0 || cloaked == 0);
        if (eligible)
        {
            var className = new StringBuilder(256);
            _ = GetClassName(handle, className, className.Capacity);
            eligible = !Platform.ShellSurfaces.IsShellClass(className.ToString())
                && !Platform.ShellSurfaces.IsShellProcess(Platform.ProcessImage.Name(processId));
        }
        // Destruction/recreation during inspection must not yield a mixed snapshot.
        return Matches(identity) ? new(identity, caption, eligible, (extendedStyle & TopmostStyle) != 0) : null;
    }


    public IReadOnlyList<WindowSnapshot> Enumerate()
    {
        var windows = new List<WindowSnapshot>();
        _ = EnumWindows((handle, _) =>
        {
            if (Read(handle) is { } window) windows.Add(window);
            return true;
        }, 0);
        return windows;
    }

    public bool TryClaim(WindowIdentity identity)
    {
        LastError = null;
        var current = Read(identity.Handle);
        if (current is not { IsEligible: true, IsTopmost: false } || current.Identity != identity) return false;
        // Windows propagates TOPMOST changes through owner/owned-window groups. Decline a group
        // containing a preexisting topmost palette rather than later demoting another app's pin.
        if (HasPreexistingTopmostOwnedWindow(identity.Handle))
        {
            LastError = "This window has an always-on-top companion window. Close it before pinning.";
            Log.Warn("Always on Top target has a visible topmost owned window.");
            return false;
        }
        if (!SetProp(identity.Handle, _marker, 1))
        {
            SetOperationError("Could not pin this window.");
            return false;
        }
        return HasClaim(identity);
    }

    private static bool HasPreexistingTopmostOwnedWindow(nint handle)
    {
        nint rootOwner = GetAncestor(handle, 3 /* GA_ROOTOWNER */);
        if (rootOwner == 0) return true;
        if (rootOwner != handle && IsTopmost(rootOwner)) return true;
        var handles = new List<nint>();
        if (!EnumWindows((window, _) => { handles.Add(window); return true; }, 0)) return true;
        // Hidden tooltips and menus are commonly topmost too; they are not active
        // companion windows and must not prevent pinning the application.
        return HasTopmostOwnedWindow(rootOwner, handles, window => GetWindow(window, 4 /* GW_OWNER */),
            window => IsWindowVisible(window) && IsTopmost(window));
    }

    internal static bool HasTopmostOwnedWindow(nint rootOwner, IEnumerable<nint> windows,
        Func<nint, nint> ownerOf, Func<nint, bool> isTopmost)
    {
        foreach (nint window in windows)
        {
            if (window == rootOwner || !isTopmost(window)) continue;
            var visited = new HashSet<nint>();
            for (nint owner = ownerOf(window); owner != 0 && visited.Add(owner); owner = ownerOf(owner))
                if (owner == rootOwner) return true;
        }
        return false;
    }

    public bool HasClaim(WindowIdentity identity) => Matches(identity) && GetProp(identity.Handle, _marker) == 1;

    public void ReleaseClaim(WindowIdentity identity)
    {
        if (HasClaim(identity)) _ = RemoveProp(identity.Handle, _marker);
    }

    public bool TrySetTopmost(WindowIdentity identity, bool topmost)
    {
        LastError = null;
        if (!HasClaim(identity)) return false;
        // Do not take ownership if another actor pinned it after our preflight check.
        if (topmost && IsTopmost(identity.Handle))
        {
            ReleaseClaim(identity);
            return false;
        }
        if (IsHung(identity.Handle)) return false;
        bool result = SetWindowPos(identity.Handle, topmost ? -1 : -2, 0, 0, 0, 0,
            NoSize | NoMove | NoActivate | NoOwnerZOrder);
        if (!result) SetOperationError(topmost ? "Could not pin this window." : "Could not unpin this window.");
        return result && HasClaim(identity) && IsTopmost(identity.Handle) == topmost;
    }

    /// <summary>
    /// WS_EX_LAYERED plus SetLayeredWindowAttributes(LWA_ALPHA), as documented for layered windows.
    /// A window that is already layered keeps its color key; one drawn with UpdateLayeredWindow has no
    /// readable attributes and is declined rather than broken.
    /// https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes
    /// </summary>
    public bool TrySetOpacity(WindowIdentity identity, byte alpha, ref OpacityRestore? original)
    {
        LastError = null;
        if (!HasClaim(identity) || IsHung(identity.Handle)) return false;
        nint handle = identity.Handle;
        long style = GetWindowLongPtr(handle, -20).ToInt64();
        bool layered = (style & LayeredStyle) != 0;
        if (original is null)
        {
            if (!layered) original = new(false, 0, 255, 0);
            else if (GetLayeredWindowAttributes(handle, out uint key, out byte currentAlpha, out uint flags))
                original = new(true, key, currentAlpha, flags);
            else
            {
                LastError = AlwaysOnTopService.TransparencyError;
                return false;
            }
        }
        if (!layered)
        {
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLongPtr(handle, -20, (nint)(style | LayeredStyle)) == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                SetOperationError(AlwaysOnTopService.TransparencyError);
                return false;
            }
        }
        if (SetLayeredWindowAttributes(handle, original.ColorKey, alpha, AlphaFlag | (original.Flags & ColorKeyFlag))) return true;
        SetOperationError(AlwaysOnTopService.TransparencyError);
        if (!original.WasLayered) TryRestoreOpacity(identity, original);
        return false;
    }

    public bool TryRestoreOpacity(WindowIdentity identity, OpacityRestore original)
    {
        LastError = null;
        if (!Matches(identity)) return true;
        // Changing another window's style sends it messages synchronously; never wait on a hung one.
        if (IsHung(identity.Handle)) return false;
        nint handle = identity.Handle;
        if (original.WasLayered)
            return SetLayeredWindowAttributes(handle, original.ColorKey, original.Alpha, original.Flags);
        // Make it opaque before removing the style, so no partially transparent frame is drawn.
        SetLayeredWindowAttributes(handle, 0, 255, AlphaFlag);
        long style = GetWindowLongPtr(handle, -20).ToInt64();
        if ((style & LayeredStyle) == 0) return true;
        Marshal.SetLastPInvokeError(0);
        return SetWindowLongPtr(handle, -20, (nint)(style & ~LayeredStyle)) != 0 || Marshal.GetLastPInvokeError() == 0;
    }

    private bool IsHung(nint handle)
    {
        if (!IsHungAppWindow(handle)) return false;
        LastError = "The window is not responding. Try again when it responds.";
        return true;
    }

    private void SetOperationError(string fallback)
    {
        int error = Marshal.GetLastWin32Error();
        LastError = ErrorForNativeFailure(error, fallback);
        Log.Warn($"Always on Top native operation failed: {error}; {LastError}");
    }

    internal static string ErrorForNativeFailure(int error, string fallback) => error == 5
        ? "This window requires administrator permissions. Restart Llampec as administrator to pin it."
        : fallback;

    private static bool IsTopmost(nint handle) => (GetWindowLongPtr(handle, -20).ToInt64() & TopmostStyle) != 0;

    private static bool Matches(WindowIdentity identity)
    {
        uint thread = GetWindowThreadProcessId(identity.Handle, out uint process);
        return thread != 0 && process == identity.ProcessId && thread == identity.ThreadId;
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        // The hooked ranges already limit the event types; window events must concern the window itself.
        if (_disposed || hwnd == 0 || (eventType != ForegroundEvent && (objectId != 0 || childId != 0))) return;
        try { WindowChanged?.Invoke(new(hwnd, eventType == ForegroundEvent, eventType == ObjectLocationChange)); }
        catch (Exception ex) { Log.Warn($"Always on Top window event failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_foregroundHook != 0) _ = UnhookWinEvent(_foregroundHook);
        Unhook(_windowHooks);
        Unhook(_destroyHook);
        UnhookPins(_pinHooks.Keys.ToArray());
        _foregroundHook = 0;
        GC.KeepAlive(_callback);
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time);
    private delegate bool EnumWindowsProc(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsHungAppWindow(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int size);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, uint attribute, out int value, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", EntryPoint = "SetPropW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProp(nint handle, string name, nint data);
    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint handle, string name);
    [DllImport("user32.dll", EntryPoint = "RemovePropW", CharSet = CharSet.Unicode)] private static extern nint RemoveProp(nint handle, string name);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetLayeredWindowAttributes(nint handle, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLayeredWindowAttributes(nint handle, out uint colorKey, out byte alpha, out uint flags);
}

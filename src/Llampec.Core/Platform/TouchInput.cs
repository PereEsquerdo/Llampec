using System.Diagnostics;
using Llampec.Interop;
using Microsoft.Win32;

namespace Llampec.Platform;

/// <summary>Windows' "Touch keyboard" choice in Settings &gt; Personalization &gt; Taskbar &gt; System tray icons.</summary>
public enum KeyboardIconMode
{
    Never = 0,
    Always = 1,
    WithoutKeyboard = 2,
}

/// <summary>
/// Opens Windows' touch keyboard and virtual touchpad, and reads or changes whether their notification-area
/// buttons are shown. The two visibility values are the current user's existing Settings preferences
/// (TipbandDesiredVisibility and TouchpadDesiredVisibility), not a public settings API. Explorer watches
/// both keys and applies a change immediately; it is never restarted. Writes require the existing key and
/// are verified by read-back.
/// </summary>
public static class TouchInput
{
    private const string KeyboardKey = @"Software\Microsoft\TabletTip\1.7";
    private const string KeyboardIconValue = "TipbandDesiredVisibility";
    private const string TouchpadKey = @"Software\Microsoft\Touchpad";
    private const string TouchpadIconValue = "TouchpadDesiredVisibility";

    private static readonly string TabTipPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");

    /// <summary>The touch keyboard host ships with Windows; it is absent only on stripped-down editions.</summary>
    public static bool KeyboardAvailable { get; } = File.Exists(TabTipPath);

    /// <summary>Windows registers this protocol where the virtual touchpad exists.</summary>
    public static bool TouchpadAvailable { get; } = ProtocolRegistered("ms-virtualtouchpad");

    /// <summary>Null when the preference is missing, unrecognized or unreadable.</summary>
    public static KeyboardIconMode? ReadKeyboardIcon() =>
        ReadDword(KeyboardKey, KeyboardIconValue) is int value && Enum.IsDefined((KeyboardIconMode)value)
            ? (KeyboardIconMode)value : null;

    public static bool WriteKeyboardIcon(KeyboardIconMode mode) =>
        WriteDword(KeyboardKey, KeyboardIconValue, (int)mode) && ReadKeyboardIcon() == mode;

    /// <summary>Null when the preference is missing, unrecognized or unreadable.</summary>
    public static bool? ReadTouchpadIcon() => ReadDword(TouchpadKey, TouchpadIconValue) switch
    {
        0 => false,
        1 => true,
        _ => null,
    };

    public static bool WriteTouchpadIcon(bool visible) =>
        WriteDword(TouchpadKey, TouchpadIconValue, visible ? 1 : 0) && ReadTouchpadIcon() == visible;

    /// <summary>
    /// Shows or hides the touch keyboard like its taskbar button, also with a hardware keyboard attached.
    /// The documented InputPane.TryShow reports success from a desktop window without an edit field but
    /// leaves the keyboard hidden, so this uses the shell's undocumented ITipInvocation interface. Its
    /// out-of-process server belongs to TabTip; when it is not running, TabTip is started and the call
    /// retried briefly. A failure leaves the keyboard unchanged and is reported by the caller.
    /// </summary>
    public static async Task ToggleKeyboardAsync(CancellationToken cancellationToken)
    {
        bool started = false;
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tip = (ITipInvocation)Activator.CreateInstance(Type.GetTypeFromCLSID(UIHostNoLaunch, true)!)!;
                try { tip.Toggle(User32.GetDesktopWindow()); }
                finally { Marshal.ReleaseComObject(tip); }
                return;
            }
            catch (COMException) when (attempt < 20)
            {
                if (!started)
                {
                    started = true;
                    using var host = Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true });
                }
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Opens the virtual touchpad. It has no toggle: a second request leaves it open.</summary>
    public static void OpenTouchpad()
    {
        using var touchpad = Process.Start(new ProcessStartInfo("ms-virtualtouchpad:") { UseShellExecute = true });
    }

    private static int? ReadDword(string keyName, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyName, writable: false);
            return key?.GetValue(valueName) as int?;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            Diagnostics.Log.Warn($"Could not read {valueName}: {ex.Message}");
            return null;
        }
    }

    private static bool WriteDword(string keyName, string valueName, int value)
    {
        try
        {
            // Explorer watches the existing key. Do not create one it may not be observing.
            using var key = Registry.CurrentUser.OpenSubKey(keyName, writable: true);
            if (key is null) return false;
            key.SetValue(valueName, value, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            Diagnostics.Log.Warn($"Could not change {valueName}: {ex.Message}");
            return false;
        }
    }

    private static bool ProtocolRegistered(string scheme)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(scheme, writable: false);
            return key is not null;
        }
        catch (Exception ex) when (IsRegistryFailure(ex)) { return false; }
    }

    private static bool IsRegistryFailure(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Security.SecurityException;

    // CLSID_UIHostNoLaunch and IID_ITipInvocation, as used by the taskbar's touch keyboard button.
    private static readonly Guid UIHostNoLaunch = new("4CE576FA-83DC-4F88-951C-9D0782B4E376");

    [ComImport, Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(nint window);
    }
}

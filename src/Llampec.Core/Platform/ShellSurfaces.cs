namespace Llampec.Platform;

/// <summary>
/// Windows that belong to the shell (taskbars, desktop, menus, task switcher, Start, search, widgets).
/// Always on Top and Fullscreen never treat them as an application window.
/// </summary>
internal static class ShellSurfaces
{
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
        "Shell_NotificationOverflowWindow", "NotifyIconOverflowWindow", "XamlExplorerHostIslandWindow",
        "#32768" /* menu */, "#32769" /* desktop */, "MultitaskingViewFrame", "TaskSwitcherWnd",
    };

    private static readonly HashSet<string> Processes = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost", "SearchHost", "SearchApp",
        "TextInputHost", "LockApp", "Widgets", "WidgetService",
    };

    public static bool IsShellClass(string className) => Classes.Contains(className);

    /// <param name="processName">The image name without extension, as <see cref="ProcessImage.Name"/> returns it.</param>
    public static bool IsShellProcess(string? processName) => processName is not null && Processes.Contains(processName);
}

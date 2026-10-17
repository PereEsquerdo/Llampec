using Llampec.Actions;
using Llampec.Actions.DisplayOff;
using Llampec.Actions.Hdr;
using Llampec.Actions.Projection;
using Llampec.Actions.Taskbar;
using Llampec.Actions.Theme;
using Llampec.Interop;
using Llampec.Platform;
using Xunit;

namespace Llampec.Tests;

/// <summary>
/// Read-only checks against the real Windows session; CI excludes them by class name. None calls
/// ExecuteAsync, which would really change the developer's theme, displays or taskbar.
/// </summary>
// SystemEvents owns one process-wide native window class; these readers must not
// construct a second instance alongside SystemEventsDeviceTests.
[Collection("SystemEvents window")]
public class SystemActionTests
{
    [Fact]
    public void Display_off_is_a_stateless_button_with_single_display_options()
    {
        using var events = new SystemEvents();
        var displayOff = new DisplayOffAction(events);
        Assert.Equal(ActionKind.ButtonWithSubpage, displayOff.Kind);
        Assert.Equal(ActionState.None, displayOff.State);
    }

    [Fact]
    public void Theme_state_matches_the_real_system_theme()
    {
        var theme = new ThemeAction();
        theme.Refresh();
        bool expectedOn = !SystemTheme.IsAppsLightTheme();
        Assert.Equal(expectedOn ? ActionState.On : ActionState.Off, theme.State);
    }

    [Fact]
    public void Taskbar_state_is_readable()
    {
        uint state = TaskbarAutoHideAction.GetState();
        Assert.Equal(0u, state & ~(Shell32.ABS_AUTOHIDE | Shell32.ABS_ALWAYSONTOP));
    }

    [Fact]
    public void Hdr_subpage_has_one_tile_per_active_display()
    {
        var hdr = new HdrAction();
        var displays = HdrDisplays.GetDisplays();
        Assert.Equal(displays.Count, hdr.SubActions.Count);
        Assert.Equal(displays.Any(d => d.Supported), hdr.IsAvailable);
    }

    [Fact]
    public void Projection_subpage_has_exactly_one_active_mode()
    {
        var projection = new ProjectionAction();
        Assert.Equal(4, projection.SubActions.Count);
        Assert.Equal(ActionState.On, projection.State);
        Assert.Single(projection.SubActions, a => a.State == ActionState.On);
    }
}

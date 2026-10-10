using Llampec.Actions;
using Llampec.Actions.Touch;
using Llampec.Platform;
using Xunit;

namespace Llampec.Tests;

/// <summary>Injected backends: no keyboard, touchpad or taskbar preference is changed.</summary>
public sealed class TouchInputTests
{
    [Fact]
    public async Task Keyboard_button_toggles_and_offers_three_exclusive_icon_choices()
    {
        KeyboardIconMode? mode = KeyboardIconMode.WithoutKeyboard;
        int toggles = 0;
        var action = new TouchKeyboardAction(true, _ => { toggles++; return Task.CompletedTask; },
            () => mode, value => { mode = value; return true; });
        action.Refresh();

        Assert.Equal(ActionKind.ButtonWithSubpage, action.Kind);
        Assert.True(action.SubActionsAreExclusive);
        Assert.Equal(ActionState.Off, action.State);
        Assert.Equal("Icon without keyboard", action.Subtitle);
        Assert.Equal(new[] { ActionState.Off, ActionState.On, ActionState.Off }, action.SubActions.Select(o => o.State));

        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal(1, toggles);
        Assert.Equal(KeyboardIconMode.WithoutKeyboard, mode);

        await action.SubActions[0].ExecuteAsync(CancellationToken.None);
        Assert.Equal(KeyboardIconMode.Always, mode);
        Assert.Equal(ActionState.On, action.State);
        Assert.Equal("Icon in taskbar", action.Subtitle);
        Assert.Equal(new[] { ActionState.On, ActionState.Off, ActionState.Off }, action.SubActions.Select(o => o.State));

        await action.SubActions[2].ExecuteAsync(CancellationToken.None);
        Assert.Equal(KeyboardIconMode.Never, mode);
        Assert.Null(action.Subtitle);
    }

    [Fact]
    public async Task Keyboard_failures_are_reported_without_assuming_a_change()
    {
        KeyboardIconMode? mode = null;
        var action = new TouchKeyboardAction(true, _ => throw new System.Runtime.InteropServices.COMException("no host"),
            () => mode, _ => false);
        action.Refresh();
        Assert.All(action.SubActions, option => Assert.Equal(ActionState.Off, option.State));

        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal("Could not open the touch keyboard.", action.Subtitle);

        await action.SubActions[0].ExecuteAsync(CancellationToken.None);
        Assert.Equal("Could not change the taskbar icon.", action.Subtitle);
        Assert.Equal(ActionState.Off, action.State);
        Assert.Null(mode);
    }

    [Fact]
    public async Task Touchpad_button_opens_and_its_switch_flips_the_taskbar_icon()
    {
        bool? visible = null;
        int opened = 0;
        var action = new TouchpadAction(true, () => opened++, () => visible, value => { visible = value; return true; });
        action.Refresh();

        Assert.Equal(ActionKind.ButtonWithSubpage, action.Kind);
        Assert.False(action.SubActionsAreExclusive);
        var option = Assert.Single(action.SubActions);
        Assert.Equal(ActionState.Off, option.State);

        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal(1, opened);

        await option.ExecuteAsync(CancellationToken.None);
        Assert.True(visible);
        Assert.Equal(ActionState.On, action.State);
        Assert.Equal(ActionState.On, option.State);
        Assert.Equal("Icon in taskbar", action.Subtitle);

        await option.ExecuteAsync(CancellationToken.None);
        Assert.False(visible);
        Assert.Null(action.Subtitle);
    }

    [Fact]
    public async Task Unavailable_tools_do_not_run_but_keep_their_icon_choices()
    {
        int opened = 0;
        bool? visible = true;
        var action = new TouchpadAction(false, () => opened++, () => visible, value => { visible = value; return true; });
        action.Refresh();
        Assert.False(action.IsAvailable);
        Assert.Equal("Virtual touchpad unavailable", action.Subtitle);

        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal(0, opened);
        await action.SubActions[0].ExecuteAsync(CancellationToken.None);
        Assert.False(visible);
    }

    [Fact]
    public void Real_preferences_are_readable_without_changing_them()
    {
        // Read-only: values outside the known Settings choices are reported as unknown, never guessed.
        var keyboard = TouchInput.ReadKeyboardIcon();
        if (keyboard is { } mode) Assert.True(Enum.IsDefined(mode));
        _ = TouchInput.ReadTouchpadIcon();
    }
}

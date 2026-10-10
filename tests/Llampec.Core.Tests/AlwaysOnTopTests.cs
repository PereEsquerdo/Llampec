using System.Text.Json;
using Llampec.Actions;
using Llampec.Actions.AlwaysOnTop;
using Llampec.Settings;
using Xunit;

namespace Llampec.Tests;

public sealed class AlwaysOnTopTests
{
    private sealed class Border : IAlwaysOnTopBorder
    {
        public int Thickness;
        public bool Disposed;
        public uint? Color;
        public void Update(int thickness, uint? color) { Thickness = thickness; Color = color; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Windows : IAlwaysOnTopWindowSystem
    {
        public event Action<WindowChange>? WindowChanged;
        public bool IsTrackingForeground { get; set; } = true;
        public string? LastError { get; set; }
        public nint ForegroundWindow { get; set; } = 1;
        public Dictionary<nint, WindowSnapshot> Items = [];
        public HashSet<nint> OwnWindows = [];
        public Dictionary<nint, nint> ForegroundMappings = [];
        public HashSet<WindowIdentity> Claims = [];
        public List<(WindowIdentity Identity, bool Topmost)> Mutations = [];
        public bool FailClaim, FailPin, FailUnpin, PartialPin, Disposed, NotifyDuringMutation;
        public int Enumerations;

        public Windows()
        {
            Add(1, "Alpha");
            Add(2, "Bravo");
            Add(3, "Shell or Llampec", eligible: false);
        }

        public void Add(nint handle, string title, uint process = 42, uint thread = 24, bool eligible = true, bool topmost = false)
            => Items[handle] = new(new(handle, process, thread), title, eligible, topmost);
        public WindowSnapshot? Read(nint handle) => Items.GetValueOrDefault(handle);
        public ForegroundWindowResolution ResolveForeground(nint handle)
        {
            var window = Read(ForegroundMappings.GetValueOrDefault(handle, handle));
            return new(window is { IsEligible: true } ? window : null, OwnWindows.Contains(handle),
                window is { IsEligible: true } ? null : "ineligible synthetic foreground");
        }
        public IReadOnlyList<WindowSnapshot> Enumerate() { Enumerations++; return Items.Values.ToArray(); }
        public bool TryClaim(WindowIdentity identity) => !FailClaim && Claims.Add(identity);
        public bool HasClaim(WindowIdentity identity) => Read(identity.Handle)?.Identity == identity && Claims.Contains(identity);
        public void ReleaseClaim(WindowIdentity identity) => Claims.Remove(identity);
        public bool TrySetTopmost(WindowIdentity identity, bool topmost)
        {
            if (!HasClaim(identity)) throw new Xunit.Sdk.XunitException("Mutation without a valid claim.");
            Mutations.Add((identity, topmost));
            bool failed = topmost ? FailPin : FailUnpin;
            if (!failed || (topmost && PartialPin)) Items[identity.Handle] = Items[identity.Handle] with { IsTopmost = topmost };
            if (NotifyDuringMutation) Notify(identity.Handle);
            return !failed;
        }
        /// <summary>Current alpha per window; absent means opaque and untouched.</summary>
        public Dictionary<nint, byte> Alpha = [];
        public bool FailOpacity, FailRestore;
        public bool TrySetOpacity(WindowIdentity identity, byte alpha, ref OpacityRestore? original)
        {
            if (!HasClaim(identity)) throw new Xunit.Sdk.XunitException("Opacity without a valid claim.");
            if (FailOpacity) { LastError = "This window does not support transparency."; return false; }
            original ??= new(false, 0, 255, 0);
            Alpha[identity.Handle] = alpha;
            return true;
        }
        public bool TryRestoreOpacity(WindowIdentity identity, OpacityRestore original)
        {
            if (FailRestore) { LastError = "The window is not responding. Try again when it responds."; return false; }
            Alpha.Remove(identity.Handle);
            return true;
        }
        public void Foreground(nint handle) { ForegroundWindow = handle; WindowChanged?.Invoke(new(handle, true)); }
        public void Notify(nint handle) => WindowChanged?.Invoke(new(handle, false));
        public void Move(nint handle) => WindowChanged?.Invoke(new(handle, false, true));
        public bool Tracking;
        public void SetWindowTracking(bool all, IReadOnlyCollection<WindowIdentity> pinned) => Tracking = all || pinned.Count != 0;
        public void Dispose() => Disposed = true;
    }

    private static AlwaysOnTopService Create(Windows windows, AlwaysOnTopSettings? settings = null, List<Border>? borders = null)
    {
        settings ??= new();
        return new(() => settings, windows, _ =>
        {
            var border = new Border();
            borders?.Add(border);
            return border;
        });
    }

    [Fact]
    public void IdleServiceFollowsOnlyForegroundWithoutEnumeratingWindows()
    {
        var windows = new Windows();
        using var service = Create(windows);
        Assert.False(windows.Tracking);
        int enumerations = windows.Enumerations;
        windows.Foreground(2);
        windows.Foreground(3);
        Assert.Equal(enumerations, windows.Enumerations);
        service.CaptureTarget();
        Assert.True(windows.Tracking);
        Assert.Equal((nint)2, service.TargetWindow);
        service.ReleaseTarget();
        Assert.False(windows.Tracking);
    }

    [Fact]
    public void PinnedWindowKeepsWindowTrackingUntilUnpinned()
    {
        var windows = new Windows();
        using var service = Create(windows);
        service.CaptureTarget();
        service.ToggleTarget();
        service.ReleaseTarget();
        Assert.True(windows.Tracking);
        service.UnpinAll();
        Assert.False(windows.Tracking);
    }

    [Fact]
    public void HiddenPanelFollowsOnlyPinnedWindows()
    {
        var windows = new Windows();
        using var service = Create(windows);
        service.CaptureTarget();
        service.ToggleTarget();
        service.ReleaseTarget();
        int enumerations = windows.Enumerations;
        windows.Notify(2);
        windows.Move(2);
        Assert.Equal(enumerations, windows.Enumerations);
        windows.Items.Remove(1);
        windows.Notify(1);
        Assert.Equal(0, service.PinnedCount);
        Assert.False(windows.Tracking);
    }

    [Fact]
    public void RemembersExternalWindowAcrossShellAndFreezesTargetUntilPanelCloses()
    {
        var windows = new Windows();
        using var service = Create(windows);
        windows.Foreground(3);
        service.CaptureTarget();
        Assert.Equal((nint)1, service.TargetWindow);
        windows.Foreground(2);
        Assert.Equal((nint)1, service.TargetWindow);
        service.ToggleTarget();
        Assert.True(windows.Items[1].IsTopmost);
        Assert.False(windows.Items[2].IsTopmost);
        service.ReleaseTarget();
        Assert.Equal((nint)2, service.TargetWindow);
    }

    [Fact]
    public void MissingTargetOffersSelectorAndShortcutNeverUsesBackgroundFallback()
    {
        var windows = new Windows { ForegroundWindow = 3 };
        using var service = Create(windows);
        int requests = 0;
        service.SelectionRequested += (_, _) => requests++;
        service.ToggleTarget();
        windows.Foreground(1);
        windows.Foreground(3);
        service.ToggleForeground();
        Assert.Equal(1, requests);
        Assert.Equal("No eligible window is active.", service.Error);
        Assert.Empty(windows.Mutations);
    }

    [Fact]
    public void ShortcutResolvesOwnedDialogToItsApplicationWithoutOpeningSelector()
    {
        var windows = new Windows();
        windows.Add(4, "Owned dialog", eligible: false);
        windows.ForegroundMappings[4] = 2;
        using var service = Create(windows);
        int selections = 0;
        service.SelectionRequested += (_, _) => selections++;
        windows.Foreground(4);
        service.ToggleForeground();
        Assert.True(windows.Items[2].IsTopmost);
        Assert.False(windows.Items[1].IsTopmost);
        Assert.Equal(0, selections);
        Assert.Null(service.Error);
        Assert.Equal((nint)2, service.TargetWindow);
    }

    [Fact]
    public void ShortcutWhileLlampecHasFocusUsesFrozenExternalTarget()
    {
        var windows = new Windows();
        windows.OwnWindows.Add(3);
        using var service = Create(windows);
        service.CaptureTarget();
        windows.Foreground(2);
        windows.Foreground(3);
        int selections = 0;
        service.SelectionRequested += (_, _) => selections++;
        service.ToggleForeground();
        Assert.True(windows.Items[1].IsTopmost);
        Assert.False(windows.Items[2].IsTopmost);
        Assert.Equal(0, selections);
        Assert.Null(service.Error);
    }

    [Fact]
    public void ShortcutOnExternalWindowIgnoresDifferentFrozenPanelTarget()
    {
        var windows = new Windows();
        using var service = Create(windows);
        service.CaptureTarget();
        windows.Foreground(2);
        service.ToggleForeground();
        Assert.False(windows.Items[1].IsTopmost);
        Assert.True(windows.Items[2].IsTopmost);
    }

    [Fact]
    public void ShortcutWhileOwnPanelHasFocusDoesNotUseClosedOrRecycledTarget()
    {
        var windows = new Windows();
        windows.OwnWindows.Add(3);
        using var service = Create(windows);
        service.CaptureTarget();
        windows.Foreground(3);
        windows.Add(1, "Recycled", process: 987);
        service.ToggleForeground();
        Assert.Empty(windows.Mutations);
        Assert.Equal("No eligible window is active.", service.Error);
    }

    [Fact]
    public void MultiplePinsSortFirstAndTileStateOnlyRepresentsItsTarget()
    {
        var windows = new Windows();
        using var service = Create(windows);
        using var action = new AlwaysOnTopAction(service);
        service.CaptureTarget();
        service.GetWindows();
        service.Toggle(2);
        Assert.Equal(1, service.PinnedCount);
        Assert.Equal(ActionState.Off, action.State);
        Assert.Equal((nint)2, service.GetWindows()[0].Handle);
        service.ToggleTarget();
        Assert.Equal(2, service.PinnedCount);
        Assert.Equal(ActionState.On, action.State);
        service.UnpinAll();
        Assert.Equal(0, service.PinnedCount);
        Assert.All(windows.Items.Values, window => Assert.False(window.IsTopmost));
    }

    [Fact]
    public void ExternalTopmostWindowsAreNeverClaimedOrUnpinned()
    {
        var windows = new Windows();
        windows.Items[1] = windows.Items[1] with { IsTopmost = true };
        using var service = Create(windows);
        Assert.DoesNotContain(service.GetWindows(), window => window.Handle == 1);
        service.ToggleTarget();
        Assert.Equal("This window is already kept on top by another application.", service.Error);
        service.CaptureTarget(); // Showing the panel after a shortcut failure must retain the explanation.
        Assert.NotNull(service.Error);
        service.UnpinAll();
        service.Dispose();
        Assert.True(windows.Items[1].IsTopmost);
        Assert.Empty(windows.Claims);
        Assert.Empty(windows.Mutations);
    }

    [Fact]
    public void FailedClaimAndPinDoNotLeavePinsOrMarkers()
    {
        var windows = new Windows { FailClaim = true };
        using var service = Create(windows);
        service.ToggleTarget();
        Assert.NotNull(service.Error);
        Assert.Empty(windows.Mutations);
        windows.FailClaim = false;
        windows.FailPin = true;
        service.ToggleTarget();
        Assert.Equal(0, service.PinnedCount);
        Assert.Empty(windows.Claims);
        Assert.False(windows.Items[1].IsTopmost);
    }

    [Fact]
    public void PartialNativeFailureRemainsTrackedSoItCanBeRestored()
    {
        var windows = new Windows { FailPin = true, PartialPin = true };
        using var service = Create(windows);
        service.ToggleTarget();
        Assert.NotNull(service.Error);
        Assert.Equal(1, service.PinnedCount);
        service.UnpinAll();
        Assert.False(windows.Items[1].IsTopmost);
        Assert.Empty(windows.Claims);
    }

    [Fact]
    public void NativePermissionFailureIsPreservedForPinAndUnpinFeedback()
    {
        const string permissionError = "This window requires administrator permissions. Restart Llampec as administrator to pin it.";
        var windows = new Windows { FailClaim = true, LastError = permissionError };
        using var service = Create(windows);
        service.ToggleForeground();
        Assert.Equal(permissionError, service.Error);
        windows.FailClaim = false;
        windows.FailPin = true;
        service.ToggleForeground();
        Assert.Equal(permissionError, service.Error);
        windows.FailPin = false;
        windows.LastError = null;
        service.ToggleForeground();
        Assert.Null(service.Error);
        windows.LastError = permissionError;
        windows.FailUnpin = true;
        service.ToggleForeground();
        Assert.Equal(permissionError, service.Error);
        Assert.Equal(1, service.PinnedCount);
        service.UnpinAll();
        Assert.Equal(permissionError, service.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(1400)]
    public void OtherNativeFailuresPreserveTheOperationSpecificMessage(int error)
    {
        Assert.Equal("Could not pin this window.", NativeAlwaysOnTopWindowSystem.ErrorForNativeFailure(error, "Could not pin this window."));
        Assert.Equal("Could not unpin this window.", NativeAlwaysOnTopWindowSystem.ErrorForNativeFailure(error, "Could not unpin this window."));
    }

    [Fact]
    public void AccessDeniedProducesActionableAdministratorExplanation()
    {
        const string expected = "This window requires administrator permissions. Restart Llampec as administrator to pin it.";
        Assert.Equal(expected, NativeAlwaysOnTopWindowSystem.ErrorForNativeFailure(5, "Could not pin this window."));
        Assert.Equal(expected, NativeAlwaysOnTopWindowSystem.ErrorForNativeFailure(5, "Could not unpin this window."));
    }

    [Fact]
    public void FailedUnpinRetainsOwnershipAndBorderForRetry()
    {
        var windows = new Windows();
        var borders = new List<Border>();
        using var service = Create(windows, borders: borders);
        service.ToggleTarget();
        windows.FailUnpin = true;
        service.UnpinAll();
        Assert.Equal("Could not unpin this window.", service.Error);
        Assert.Equal(1, service.PinnedCount);
        Assert.False(Assert.Single(borders).Disposed);
        windows.FailUnpin = false;
        service.UnpinAll();
        Assert.Equal(0, service.PinnedCount);
        Assert.True(borders[0].Disposed);
    }

    [Fact]
    public void DeactivationFailureKeepsPinsAndBackendAliveUntilRecoverySucceeds()
    {
        var windows = new Windows();
        var borders = new List<Border>();
        using var service = Create(windows, borders: borders);
        service.ToggleTarget();
        windows.FailUnpin = true;

        Assert.False(service.TryDeactivate());
        Assert.Equal(1, service.PinnedCount);
        Assert.Single(windows.Claims);
        Assert.False(windows.Disposed);
        Assert.False(Assert.Single(borders).Disposed);

        windows.FailUnpin = false;
        Assert.True(service.TryDeactivate());
        Assert.Equal(0, service.PinnedCount);
        Assert.Empty(windows.Claims);
        Assert.True(borders[0].Disposed);
        Assert.False(windows.Disposed); // The host disposes only after successful preparation.
        Assert.True(service.TryDeactivate());
    }

    [Fact]
    public void ClosingWindowDropsPinAndBorderWithoutMutatingAnotherHandle()
    {
        var windows = new Windows();
        var borders = new List<Border>();
        using var service = Create(windows, borders: borders);
        service.CaptureTarget();
        service.ToggleTarget();
        windows.Items.Remove(1);
        windows.Notify(1);
        Assert.Equal(0, service.PinnedCount);
        Assert.False(service.HasTarget);
        Assert.True(Assert.Single(borders).Disposed);
        Assert.Single(windows.Mutations);
    }

    [Fact]
    public void RecycledPinnedHandleWithDifferentProcessOrMissingMarkerIsNeverUnpinned()
    {
        var windows = new Windows();
        using var service = Create(windows);
        service.ToggleTarget();
        windows.Add(1, "Different process", process: 99, topmost: true);
        service.UnpinAll();
        Assert.True(windows.Items[1].IsTopmost);
        Assert.Single(windows.Mutations);

        windows.Add(1, "Another same-thread window");
        service.ToggleTarget();
        windows.Claims.Clear(); // Window recreation on the same process/thread destroys its property list.
        service.UnpinAll();
        Assert.True(windows.Items[1].IsTopmost);
        Assert.Equal(2, windows.Mutations.Count);
    }

    [Fact]
    public void OldSelectorRowDoesNotPinRecycledHandleBeforeRowsRefresh()
    {
        var windows = new Windows();
        using var service = Create(windows);
        service.GetWindows();
        windows.Add(2, "Replacement", process: 999);
        windows.Notify(2);
        service.Toggle(2);
        Assert.Equal("This window is no longer available.", service.Error);
        Assert.Empty(windows.Mutations);
        service.GetWindows();
        service.Toggle(2);
        Assert.True(windows.Items[2].IsTopmost);
    }

    [Fact]
    public void ExternalUnpinReleasesOurMarkerAndBorder()
    {
        var windows = new Windows();
        var borders = new List<Border>();
        using var service = Create(windows, borders: borders);
        service.ToggleTarget();
        windows.Items[1] = windows.Items[1] with { IsTopmost = false };
        windows.Notify(1);
        Assert.Equal(0, service.PinnedCount);
        Assert.Empty(windows.Claims);
        Assert.True(Assert.Single(borders).Disposed);
    }

    [Fact]
    public void AppearanceAppliesLiveAndDisablingRetainsThickness()
    {
        var windows = new Windows();
        var preferences = new AlwaysOnTopSettings();
        var borders = new List<Border>();
        using var service = Create(windows, preferences, borders);
        service.ToggleTarget();
        Assert.Equal(3, Assert.Single(borders).Thickness);
        preferences.BorderThickness = 6;
        service.ApplyAppearance();
        Assert.Equal(6, borders[0].Thickness);
        preferences.ShowBorder = false;
        service.ApplyAppearance();
        Assert.True(borders[0].Disposed);
        Assert.True(windows.Items[1].IsTopmost);
        Assert.Equal(6, preferences.BorderThickness);
        preferences.ShowBorder = true;
        service.ApplyAppearance();
        Assert.Equal(2, borders.Count);
        Assert.Equal(6, borders[1].Thickness);
        Assert.Null(borders[1].Color);
        preferences.BorderColor = "#00FF80";
        service.ApplyAppearance();
        Assert.Equal(0x00FF80u, borders[1].Color);
        preferences.BorderColor = null;
        service.ApplyAppearance();
        Assert.Null(borders[1].Color);
    }

    [Fact]
    public void GlobalOpacityAppliesLiveAndIsRestoredOnUnpinAndShutdown()
    {
        var windows = new Windows();
        var preferences = new AlwaysOnTopSettings();
        var service = Create(windows, preferences);
        service.ToggleTarget();
        Assert.Empty(windows.Alpha); // 100 % leaves the window untouched
        preferences.Opacity = 50;
        service.ApplyAppearance();
        Assert.Equal(128, windows.Alpha[1]);
        preferences.Opacity = 100;
        service.ApplyAppearance();
        Assert.Empty(windows.Alpha);
        preferences.Opacity = 80;
        service.ApplyAppearance();
        service.ToggleTarget(); // unpin
        Assert.Empty(windows.Alpha);
        service.ToggleTarget();
        Assert.Equal(204, windows.Alpha[1]);
        service.Dispose();
        Assert.Empty(windows.Alpha);
    }

    [Fact]
    public void UnpinKeepsThePinWhileOpacityCannotBeRestored()
    {
        var windows = new Windows();
        using var service = Create(windows, new AlwaysOnTopSettings { Opacity = 50 });
        service.ToggleTarget();
        windows.FailRestore = true; // e.g. the window stopped responding
        service.ToggleTarget();
        Assert.True(service.IsTargetPinned);
        Assert.True(windows.Items[1].IsTopmost);
        Assert.Equal("The window is not responding. Try again when it responds.", service.Error);
        windows.FailRestore = false;
        service.ToggleTarget();
        Assert.False(service.IsTargetPinned);
        Assert.Empty(windows.Alpha);
    }

    [Fact]
    public void ExternallyUnpinnedWindowIsMadeOpaqueAgain()
    {
        var windows = new Windows();
        using var service = Create(windows, new AlwaysOnTopSettings { Opacity = 60 });
        service.ToggleTarget();
        Assert.True(windows.Alpha.ContainsKey(1));
        windows.Items[1] = windows.Items[1] with { IsTopmost = false };
        windows.Notify(1);
        Assert.Equal(0, service.PinnedCount);
        Assert.Empty(windows.Alpha);
    }

    [Fact]
    public void WindowAppearanceOverridesGlobalUntilCleared()
    {
        var windows = new Windows();
        var preferences = new AlwaysOnTopSettings { BorderThickness = 3, Opacity = 90 };
        var borders = new List<Border>();
        using var service = Create(windows, preferences, borders);
        service.ToggleTarget();
        windows.Foreground(2);
        service.ToggleTarget();
        Assert.Equal(2, service.PinnedCount);
        Assert.All(service.GetPinnedAppearances(), window => Assert.Null(window.Appearance));

        service.SetWindowAppearance(2, new WindowAppearance(true, 7, 0x00FF80, 40));
        var bravo = Assert.Single(service.GetPinnedAppearances(), window => window.Handle == 2);
        Assert.Equal(7, bravo.Appearance!.BorderThickness);
        Assert.Equal(102, windows.Alpha[2]);
        Assert.Equal(230, windows.Alpha[1]);
        Assert.Equal(7, borders[1].Thickness);
        Assert.Equal(0x00FF80u, borders[1].Color);
        Assert.Equal(3, borders[0].Thickness);

        // Global changes leave the window with its own appearance alone.
        preferences.Opacity = 100;
        service.ApplyAppearance();
        Assert.False(windows.Alpha.ContainsKey(1));
        Assert.Equal(102, windows.Alpha[2]);

        service.SetWindowAppearance(2, new WindowAppearance(false, 7, null, 100));
        Assert.True(borders[1].Disposed);
        Assert.False(windows.Alpha.ContainsKey(2));

        service.SetWindowAppearance(2, null);
        Assert.Null(Assert.Single(service.GetPinnedAppearances(), window => window.Handle == 2).Appearance);
        Assert.Equal(3, borders[^1].Thickness);
    }

    [Fact]
    public void UnsupportedTransparencyKeepsThePinAndReportsIt()
    {
        var windows = new Windows { FailOpacity = true };
        var borders = new List<Border>();
        using var service = Create(windows, new AlwaysOnTopSettings { Opacity = 50 }, borders);
        service.ToggleTarget();
        Assert.True(service.IsTargetPinned);
        Assert.Single(borders);
        Assert.Equal("This window does not support transparency.", service.Error);
        windows.FailOpacity = false;
        service.ApplyAppearance();
        Assert.Null(service.Error);
        Assert.Equal(128, windows.Alpha[1]);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(55, 55)]
    [InlineData(500, 100)]
    public void StoredOpacityIsNormalized(int value, int expected)
        => Assert.Equal(expected, new AlwaysOnTopSettings { Opacity = value }.Opacity);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("blue", null)]
    [InlineData("#12345", null)]
    [InlineData("#1234567", null)]
    [InlineData("8b6f5c", "#8B6F5C")]
    [InlineData(" #00ff80 ", "#00FF80")]
    public void StoredBorderColorIsNormalized(string? value, string? expected)
    {
        var settings = new AlwaysOnTopSettings { BorderColor = value };
        Assert.Equal(expected, settings.BorderColor);
        Assert.Equal(expected is null, settings.BorderRgb is null);
    }

    [Fact]
    public void BorderFailureDoesNotAbandonSuccessfulPin()
    {
        var windows = new Windows();
        using var service = new AlwaysOnTopService(() => new(), windows, _ => throw new IOException("test"));
        service.ToggleTarget();
        Assert.Equal("Could not show the window border.", service.Error);
        Assert.Equal(1, service.PinnedCount);
        service.UnpinAll();
        Assert.False(windows.Items[1].IsTopmost);
    }

    [Fact]
    public void ReentrantNativeAndChangedEventsCannotRecursivelyRefresh()
    {
        var windows = new Windows { NotifyDuringMutation = true };
        using var service = Create(windows);
        int notifications = 0;
        service.Changed += (_, _) => { notifications++; service.Refresh(); service.GetWindows(); };
        service.ToggleTarget();
        service.Refresh();
        Assert.Equal(1, notifications);
        service.ToggleTarget();
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void WindowMotionDoesNotRebuildSelectorButDetectsRemovedTopmostStyle()
    {
        var windows = new Windows();
        using var service = Create(windows);
        using var action = new AlwaysOnTopAction(service);
        service.ToggleTarget();
        int enumerations = windows.Enumerations;
        for (int i = 0; i < 30; i++) { windows.Move(1); windows.Move(2); }
        Assert.Equal(enumerations, windows.Enumerations);
        windows.Items[1] = windows.Items[1] with { IsTopmost = false };
        windows.Move(1);
        Assert.Equal(0, service.PinnedCount);
        Assert.Equal(ActionState.Off, action.State);
        Assert.Equal(enumerations + 1, windows.Enumerations);
    }

    [Fact]
    public void DisposeRestoresAllOwnedPinsOnceAndIgnoresLateEvents()
    {
        var windows = new Windows();
        var borders = new List<Border>();
        var service = Create(windows, borders: borders);
        service.GetWindows();
        service.Toggle(1);
        service.Toggle(2);
        service.Dispose();
        service.Dispose();
        windows.Notify(1);
        Assert.True(windows.Disposed);
        Assert.Equal(0, service.PinnedCount);
        Assert.Empty(windows.Claims);
        Assert.All(borders, border => Assert.True(border.Disposed));
        Assert.All(windows.Items.Values, window => Assert.False(window.IsTopmost));
        Assert.Equal(4, windows.Mutations.Count);
    }

    [Fact]
    public void UnavailableForegroundHookRequiresSelectorAfterFocusIsLost()
    {
        var windows = new Windows { IsTrackingForeground = false };
        using var service = Create(windows);
        Assert.Equal("Window tracking is unavailable. Choose a window from the list.", service.Error);
        windows.ForegroundWindow = 3;
        service.CaptureTarget();
        Assert.False(service.HasTarget);
        Assert.NotEmpty(service.GetWindows());
    }

    [Theory]
    [InlineData(-500, 1)]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(800, 8)]
    public void StoredThicknessIsNormalized(int value, int expected)
    {
        var settings = new AlwaysOnTopSettings { BorderThickness = value };
        Assert.Equal(expected, settings.BorderThickness);
    }

    [Theory]
    [InlineData(null, "Ctrl+Alt+T")]
    [InlineData("not-a-shortcut", "Ctrl+Alt+T")]
    [InlineData("  ", "")]
    [InlineData("ctrl+shift+a", "Ctrl+Shift+A")]
    public void StoredHotkeyIsNormalized(string? value, string expected)
        => Assert.Equal(expected, new AlwaysOnTopSettings { Hotkey = value! }.Hotkey);

    [Fact]
    public void AppearanceRoundTripsAndNullFeatureSettingsUseDefaults()
    {
        var settings = new AppSettings { AlwaysOnTop = new() { ShowBorder = false, BorderThickness = 7, BorderColor = "#00FF80", Hotkey = "Win+Ctrl+T" } };
        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        Assert.False(restored.AlwaysOnTop.ShowBorder);
        Assert.Equal(7, restored.AlwaysOnTop.BorderThickness);
        Assert.Equal("Win+Ctrl+T", restored.AlwaysOnTop.Hotkey);
        Assert.Equal("#00FF80", restored.AlwaysOnTop.BorderColor);
        Assert.DoesNotContain("borderRgb", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"appearance\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100, restored.AlwaysOnTop.Opacity);
        var nullSettings = JsonSerializer.Deserialize("{\"alwaysOnTop\":null}", SettingsJsonContext.Default.AppSettings)!;
        Assert.True(nullSettings.AlwaysOnTop.ShowBorder);
        Assert.Equal(3, nullSettings.AlwaysOnTop.BorderThickness);
        Assert.Null(nullSettings.AlwaysOnTop.BorderColor);
    }

    [Fact]
    public void ExistingTopmostPaletteProtectsItsOwnerIncludingNestedOwnedWindows()
    {
        var owners = new Dictionary<nint, nint> { [1] = 0, [2] = 1, [3] = 2, [4] = 0, [5] = 6, [6] = 5 };
        var topmost = new HashSet<nint> { 3, 4, 5 };
        Assert.True(NativeAlwaysOnTopWindowSystem.HasTopmostOwnedWindow(1, owners.Keys, h => owners[h], topmost.Contains));
        topmost.Remove(3);
        // An unrelated topmost window and a malformed ownership cycle cannot block this owner.
        Assert.False(NativeAlwaysOnTopWindowSystem.HasTopmostOwnedWindow(1, owners.Keys, h => owners[h], topmost.Contains));
    }
}

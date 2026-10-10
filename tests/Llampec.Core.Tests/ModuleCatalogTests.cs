using Llampec.Actions;
using Xunit;

namespace Llampec.Tests;

public sealed class ModuleCatalogTests
{
    [Fact]
    public void StaticCatalogKeepsEveryExistingModuleIdAndOrderIncludingInactiveServices()
    {
        Assert.Equal(new[]
        {
            "hdr", "display-off", "theme", "projection", "taskbar-autohide", "touch-taskbar", "caffeine",
            "always-on-top", "rotation", "screenshot", "fullscreen", "power", "mouse", "camera",
            "touch-keyboard", "touchpad"
        }, ModuleCatalog.All.Select(module => module.Id));
        Assert.Equal(16, ModuleCatalog.All.Select(module => module.Id).Distinct().Count());
        Assert.All(ModuleCatalog.All, module =>
        {
            Assert.False(string.IsNullOrWhiteSpace(module.Title));
            Assert.Equal(1, module.Glyph.Length);
            Assert.InRange(module.Glyph[0], '\uE000', '\uF8FF');
            if (module.GlyphBadge is not null)
            {
                Assert.Equal(1, module.GlyphBadge.Length);
                Assert.InRange(module.GlyphBadge[0], '\uE000', '\uF8FF');
            }
        });
        Assert.Equal("\uE708", Assert.Single(ModuleCatalog.All, module => module.Id == "theme").GlyphBadge);
        Assert.Equal(new[] { "hdr", "theme", "projection", "caffeine", "always-on-top", "rotation", "fullscreen", "power", "mouse",
            "touch-keyboard", "touchpad" },
            ModuleCatalog.All.Where(module => module.HasSubpage).Select(module => module.Id));
    }

    [Fact]
    public void DescriptorsMatchTheActionsTheyStandFor()
    {
        // Actions whose construction neither changes nor waits on the system.
        using var caffeine = new Llampec.Actions.Caffeine.CaffeineAction(() => new());
        IQuickAction[] actions =
        [
            new Llampec.Actions.Theme.ThemeAction(), new Llampec.Actions.Taskbar.TaskbarAutoHideAction(),
            new Llampec.Actions.Taskbar.TouchTaskbarAction(), caffeine, new Llampec.Actions.Screenshot.ScreenshotAction(),
            new Llampec.Actions.Power.PowerAction(), new Llampec.Actions.Touch.TouchKeyboardAction(),
            new Llampec.Actions.Touch.TouchpadAction(),
        ];
        foreach (var action in actions)
        {
            var module = Assert.Single(ModuleCatalog.All, module => module.Id == action.Id);
            Assert.Equal(action.Title, module.Title);
            Assert.Equal(action.Glyph, module.Glyph);
            Assert.Equal(action.GlyphBadge, module.GlyphBadge);
            Assert.Equal(action.Kind is ActionKind.ToggleWithSubpage or ActionKind.ButtonWithSubpage, module.HasSubpage);
        }
    }

    [Fact]
    public void MetadataCollectionCannotBeModifiedByAnEditor()
    {
        var collection = Assert.IsAssignableFrom<ICollection<ModuleDescriptor>>(ModuleCatalog.All);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Equal(16, ModuleCatalog.All.Count);
    }
}

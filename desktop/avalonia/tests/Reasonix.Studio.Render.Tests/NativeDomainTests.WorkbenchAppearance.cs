using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Matches the reference's UI and code faces, weights and semantic inks in the Git explorer.</summary>
    /// <param name="dark">Requested palette.</param>
    /// <returns>The workbench appearance.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WorkbenchUsesReferenceTypeAndInk(bool dark) => WithSettings("workbench", (w, _) =>
    {
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var root = w.FindControl<StackPanel>("GitRepositories")!;
        var board = root.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Board");
        var changesTitle = Assert.IsType<TextBlock>(board.Content);
        Assert.Equal(13, changesTitle.LineHeight);
        Assert.Equal(13, changesTitle.TextLayout.Height);
        Assert.Equal(13, changesTitle.FontSize);
        Assert.Equal(FontWeight.Bold, changesTitle.FontWeight);
        foreach (var button in root.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "StageAll" or "Draft" or "Push"))
        {
            var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(button.FontSize, label.FontSize);
            Assert.Equal(button.FontWeight, label.FontWeight);
            Assert.Equal(button.FontFamily, label.FontFamily);
        }

        var text = root.GetVisualDescendants().OfType<TextBlock>().ToArray();
        var names = text.Where(t => t.Classes.Contains("git-path")).ToArray();
        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            Assert.Equal(12, name.FontSize);
            Assert.Equal(FontWeight.Medium, name.FontWeight);
            Assert.Equal(Color.Parse(dark ? "#B4B7B0" : "#5E625C"), Assert.IsAssignableFrom<ISolidColorBrush>(name.Foreground).Color);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(name.FontFamily, name.FontStyle, name.FontWeight), out var face));
            Assert.Equal("Reasonix Workbench UI", face.FamilyName);
            Assert.True(face.CharacterToGlyphMap.ContainsGlyph(0x2026));
        }

        foreach (var (role, light, night) in new[] { ("git-added", "#2A78D6", "#3987E5"), ("git-removed", "#E87BA4", "#D55181") })
        {
            var counts = text.Where(t => t.Classes.Contains(role)).ToArray();
            Assert.NotEmpty(counts);
            foreach (var count in counts)
            {
                Assert.Equal(11, count.FontSize);
                Assert.Equal(FontWeight.SemiBold, count.FontWeight);
                Assert.Equal(Color.Parse(dark ? night : light), Assert.IsAssignableFrom<ISolidColorBrush>(count.Foreground).Color);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(count.FontFamily, count.FontStyle, count.FontWeight), out var face));
                Assert.Equal("Reasonix Workbench Mono", face.FamilyName);
                Assert.Equal(FontWeight.SemiBold, face.Weight);
            }
        }

        var title = w.FindControl<TextBlock>("ExplorerHeading")!;
        Assert.Equal(FontWeight.Bold, title.FontWeight);
        Assert.Equal(13, title.FontSize);
        Assert.Contains(text, t => t.Classes.Contains("git-history-subject") && t.FontSize == 12 && t.FontWeight == FontWeight.SemiBold);
        Assert.Contains(text, t => t.Classes.Contains("git-history-hash") && t.FontSize == 11 && t.FontWeight == FontWeight.SemiBold);
    });
}

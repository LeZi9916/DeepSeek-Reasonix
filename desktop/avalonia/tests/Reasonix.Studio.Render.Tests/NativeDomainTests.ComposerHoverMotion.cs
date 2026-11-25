using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the composer mode pill's reference 120ms hover transitions.</summary>
    /// <returns>The composer pill hover check.</returns>
    [Fact]
    public Task ComposerPillsCarryTheReferenceHoverTransitions() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var plan = window.FindControl<Button>("PlanMode")!;
                Assert.Contains("compose-pill", plan.Classes);
                var brushes = plan.Transitions!.OfType<BrushTransition>().ToArray();
                var transform = Assert.Single(plan.Transitions!.OfType<TransformOperationsTransition>());
                Assert.Equal(2, brushes.Length);
                Assert.All(brushes, transition => Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration));
                Assert.Equal(TimeSpan.FromMilliseconds(120), transform.Duration);
            },
            allowMotion: true);
}

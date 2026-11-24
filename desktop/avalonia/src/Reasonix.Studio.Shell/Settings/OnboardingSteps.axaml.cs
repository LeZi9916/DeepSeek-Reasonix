using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System.Linq;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Shows the current step of the provider and workspace setup.</summary>
internal sealed partial class OnboardingSteps : UserControl
{
    /// <summary>The ordered stage labels from the setup flow.</summary>
    private static readonly string[] s_labels =
    [
        "onboarding.connect_a_model_service",
        "onboarding.choose_the_default_model",
        "onboarding.open_a_workspace",
    ];

    /// <summary>Initializes a new instance of the <see cref="OnboardingSteps"/> class.</summary>
    public OnboardingSteps() => InitializeComponent();

    /// <summary>Draws progress through the setup stages.</summary>
    /// <param name="current">The zero-based current stage.</param>
    /// <param name="language">The shell's language.</param>
    internal void Show(int current, InterfaceLanguage language) => Stages.ItemsSource = s_labels.Select((key, index) =>
        new OnboardingStep(index + 1, MessageCatalog.Get(key, language), index == current, index < current, index > 0)).ToArray();
}

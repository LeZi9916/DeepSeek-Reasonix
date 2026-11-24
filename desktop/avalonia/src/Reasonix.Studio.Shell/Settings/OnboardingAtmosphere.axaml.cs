using Avalonia;
using Avalonia.Controls;
using System;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Positions the masked grid and the breathing light beneath onboarding.</summary>
internal sealed partial class OnboardingAtmosphere : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="OnboardingAtmosphere"/> class.</summary>
    public OnboardingAtmosphere()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            Halo.Width = Halo.Height = Math.Max(360, Bounds.Width * 0.38);
            Halo.Margin = new Thickness(0, Bounds.Height * 0.07, Bounds.Width * 0.1, 0);
        };
    }
}

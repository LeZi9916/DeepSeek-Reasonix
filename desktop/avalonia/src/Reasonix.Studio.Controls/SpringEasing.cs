using Avalonia.Animation.Easings;

namespace Reasonix.Studio.Controls;

/// <summary>The reference spring curve, a piecewise linear settle with a small overshoot.</summary>
public sealed class SpringEasing : Easing
{
    /// <summary>The stops of the reference `--ease-spring` token.</summary>
    private static readonly (double At, double Value)[] s_stops =
    [
        (0.000, 0.000), (0.014, 0.006), (0.028, 0.025), (0.061, 0.101),
        (0.189, 0.539), (0.253, 0.721), (0.315, 0.849), (0.381, 0.937),
        (0.418, 0.968), (0.457, 0.991), (0.501, 1.006), (0.550, 1.015),
        (0.639, 1.017), (0.842, 1.001), (1.000, 1.000),
    ];

    /// <inheritdoc/>
    public override double Ease(double progress)
    {
        if (progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        for (var index = 1; index < s_stops.Length; index++)
        {
            if (progress <= s_stops[index].At)
            {
                var (fromAt, fromValue) = s_stops[index - 1];
                var (toAt, toValue) = s_stops[index];
                return fromValue + ((progress - fromAt) / (toAt - fromAt) * (toValue - fromValue));
            }
        }

        return 1;
    }
}

using Avalonia.Controls;
using Avalonia.Media;
using System.Text;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The reference avatar shared by subagent rows, headers and workbench tabs.</summary>
internal sealed partial class SubagentAvatar : UserControl
{
    /// <summary>The reference name-hash palette.</summary>
    private static readonly Color[] s_hues = [Color.Parse("#7c5cd6"), Color.Parse("#2f9e6e"), Color.Parse("#c27a1d")];

    /// <summary>The name rendered by this avatar.</summary>
    private string? _name;

    /// <summary>Initializes a new instance of the <see cref="SubagentAvatar"/> class.</summary>
    public SubagentAvatar() => InitializeComponent();

    /// <summary>Uses the same unsigned name hash as the Electron avatar.</summary>
    /// <param name="name">The recorded subagent name.</param>
    /// <param name="iconSize">The reference icon size for this placement.</param>
    internal void Show(string name, double iconSize = 13)
    {
        Icon.Width = Icon.Height = iconSize;
        if (_name == name)
        {
            return;
        }

        _name = name;
        uint hash = 0;
        foreach (var character in name.EnumerateRunes())
        {
            hash = unchecked((hash * 31) + (uint)character.Value);
        }

        var ink = s_hues[hash % 3];
        Bot.Stroke = new SolidColorBrush(ink);
        Tint.Background = new SolidColorBrush(ink) { Opacity = 0.16 };
    }
}

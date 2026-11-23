using Avalonia.Input;
using Reasonix.Studio.Shell.Host;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Exercises the Linux registrar's translation of X11 keyboard maps.</summary>
public sealed class X11ShortcutTests
{
    /// <summary>Remapped Super and lock keys follow the current X server map.</summary>
    [Fact]
    public void ResolvesServerModifierSlotsAndEveryLockCombination()
    {
        uint[] keysyms =
        [
            0xFFE1, 0, 0xFFE5, 0, 0xFFE3, 0, 0xFFE9, 0,
            0xFF7F, 0, 0xFFEB, 0, 0xFF14, 0, 0xFFC5, 0,
        ];
        byte[] modifiers = [8, 9, 10, 11, 13, 14, 0, 12];

        var chord = new ShortcutChord(PhysicalKey.F8, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);
        var binding = X11Shortcut.CreateBinding(chord, 8, keysyms, 2, modifiers, 1);
        Assert.NotNull(binding);
        Assert.Equal(15, binding.Value.Keycode);
        Assert.Equal(0x1C, binding.Value.Modifiers);
        Assert.Equal(0xA2, binding.Value.IgnoredLocks);
        Assert.Equal(new ushort[] { 0x1C, 0x1E, 0x3C, 0x3E, 0x9C, 0x9E, 0xBC, 0xBE }, binding.Value.Grabs().Order());
    }

    /// <summary>A missing key leaves the binding unavailable instead of grabbing any key.</summary>
    [Fact]
    public void RejectsMissingAndAnyKeyMappings()
    {
        var chord = new ShortcutChord(PhysicalKey.Z, KeyModifiers.Control);
        Assert.Null(X11Shortcut.CreateBinding(chord, 8, [0x61, 0x41], 2, [0, 0, 0, 0, 0, 0, 0, 0], 1));
        Assert.Null(X11Shortcut.CreateBinding(default, 8, [0, 0], 2, [0, 0, 0, 0, 0, 0, 0, 0], 1));
    }
}

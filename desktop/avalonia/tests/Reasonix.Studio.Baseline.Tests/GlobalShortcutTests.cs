using Avalonia.Input;
using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Exercises the desktop accelerator parser and registration lifecycle.</summary>
public sealed class GlobalShortcutTests
{
    /// <summary>Compares accelerator aliases and input with no modifier.</summary>
    [Fact]
    public void ParseKeepsTheSameAcceleratorAcrossAliases()
    {
        Assert.True(ShortcutChord.TryParse("CommandOrControl+Shift+Space", mac: true, out var mac));
        Assert.Equal(new ShortcutChord(PhysicalKey.Space, KeyModifiers.Meta | KeyModifiers.Shift), mac);
        Assert.True(ShortcutChord.TryParse("Ctrl+Shift+Space", mac: false, out var win));
        Assert.Equal(new ShortcutChord(PhysicalKey.Space, KeyModifiers.Control | KeyModifiers.Shift), win);
        Assert.False(ShortcutChord.TryParse("Shift+A", mac: false, out _));
        Assert.False(ShortcutChord.TryParse("Control+NotAKey", mac: false, out _));
        Assert.False(ShortcutChord.TryParse("Control+Ctrl+A", mac: false, out _));
    }

    /// <summary>Conflicts and failed writes keep the active registration alive.</summary>
    [Fact]
    public void RebindingIsTransactionalAndReleasesTheSystemSlot()
    {
        var active = new Dictionary<ShortcutChord, Action>();
        var saved = "Control+F10";
        var failWrite = false;
        var presses = 0;
        using var service = new GlobalShortcut(saved, Register, Save, () => presses++);
        Assert.Equal(GlobalShortcutStatus.Registered, service.Status);
        Assert.Equal(GlobalShortcutStatus.Registered, service.Apply("Ctrl+F10"));
        Assert.Equal("Control+F10", saved);
        Assert.Equal(GlobalShortcutStatus.Unavailable, service.Apply("Control+F11"));
        failWrite = true;
        Assert.Equal(GlobalShortcutStatus.SaveFailed, service.Apply("Control+F12"));
        Assert.Single(active).Value();
        Assert.Equal(1, presses);
        Assert.Equal("Control+F10", service.Binding);

        failWrite = false;
        Assert.Equal(GlobalShortcutStatus.Registered, service.Apply("Control+F12"));
        Assert.Equal("Control+F12", saved);
        Assert.Equal(PhysicalKey.F12, Assert.Single(active).Key.Key);
        Assert.Equal(GlobalShortcutStatus.Disabled, service.Apply(string.Empty));
        Assert.Empty(active);
        Assert.Empty(saved);
        Assert.Equal(GlobalShortcutStatus.Registered, service.Apply("Control+F10"));
        service.Dispose();
        Assert.Empty(active);
        Assert.Equal(GlobalShortcutStatus.Quitting, service.Apply("Control+F12"));

        Action? Register(ShortcutChord chord, Action press)
        {
            if (chord.Key == PhysicalKey.F11)
            {
                return null;
            }

            active.Add(chord, press);
            return () => active.Remove(chord);
        }

        void Save(string value)
        {
            if (failWrite)
            {
                throw new IOException();
            }

            saved = value;
        }
    }
}

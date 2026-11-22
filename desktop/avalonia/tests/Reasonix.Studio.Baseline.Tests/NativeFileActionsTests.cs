using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Checks the final operating-system acknowledgment of workspace file actions.</summary>
public sealed class NativeFileActionsTests
{
    /// <summary>The production desktop entry uses the apartment required by the embedded Windows browser.</summary>
    [Fact]
    public void DesktopEntryUsesTheWindowsBrowserApartment()
    {
        var entry = typeof(Reasonix.Studio.Shell.Program).GetMethod("Main", BindingFlags.Static | BindingFlags.Public)!;
        Assert.NotNull(entry.GetCustomAttribute<STAThreadAttribute>());
    }

    /// <summary>Reveal selects the file, while Open invokes its registered application.</summary>
    /// <returns>The acknowledged file actions.</returns>
    [Fact]
    public async Task RevealSelectsTheEntryWithoutOpeningItsApplication()
    {
        var root = Directory.CreateTempSubdirectory("studio-file-actions-");
        try
        {
            var path = Path.Combine(root.FullName, "note.txt");
            await File.WriteAllTextAsync(path, "fixture");
            var calls = new List<string>();
            var host = new AvaloniaHost(null, new NativeFileActions(
                folder => Record("folder:" + folder.FullName),
                file => Record("open:" + file.FullName),
                file => Record("select:" + file.FullName)));
            Assert.Null(await host.RevealPathAsync(root.FullName, "note.txt"));
            Assert.Null(await host.OpenFileAsync(root.FullName, "note.txt"));
            Assert.Equal(new[] { "select:" + path, "open:" + path }, calls);

            Task<bool> Record(string value)
            {
                calls.Add(value);
                return Task.FromResult(true);
            }
        }
        finally
        {
            root.Delete(true);
        }
    }

    /// <summary>A platform refusal is returned after the asynchronous operation completes.</summary>
    /// <param name="reveal">Whether the requested operation selects or opens.</param>
    /// <returns>The final platform refusal.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlatformRefusalIsAwaitedAndVisible(bool reveal)
    {
        var root = Directory.CreateTempSubdirectory("studio-file-refusal-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "note.txt"), "fixture");
            var completion = new TaskCompletionSource<bool>();
            var host = new AvaloniaHost(null, new NativeFileActions(_ => completion.Task, _ => completion.Task, _ => completion.Task));
            var operation = reveal ? host.RevealPathAsync(root.FullName, "note.txt") : host.OpenFileAsync(root.FullName, "note.txt");
            Assert.False(operation.IsCompleted);
            completion.SetResult(false);
            Assert.Equal("shell.launch.failed", (await operation)!.Code);
            Assert.Equal("shell.path.outside", (await host.OpenFileAsync(root.FullName, "../outside.txt"))!.Code);
            Assert.Equal("shell.path.missing", (await host.OpenFileAsync(root.FullName, "missing.txt"))!.Code);
        }
        finally
        {
            root.Delete(true);
        }
    }
}

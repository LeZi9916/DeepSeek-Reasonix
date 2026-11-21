using Avalonia.Input;
using Avalonia.Platform.Storage;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Chooses an optional project after the model has been saved.</summary>
public sealed partial class ProjectChoice : SettingsFormView
{
    /// <summary>Selected folder in this visit.</summary>
    private string? _path;

    /// <summary>Initializes a new instance of the <see cref="ProjectChoice"/> class.</summary>
    public ProjectChoice() => InitializeComponent();

    /// <summary>Connects folder selection, drop, skip and activation.</summary>
    /// <param name="pick">The platform folder picker.</param>
    /// <param name="start">Starts a project on the selected root.</param>
    /// <param name="skip">Returns to the current workspace.</param>
    /// <param name="describe">Localizes failures.</param>
    internal void Configure(Func<Task<string?>> pick, Func<string, Task> start, Action skip, Func<Exception, string> describe)
    {
        Pick.Click += async (_, _) => await RunAsync(async () =>
        {
            if (await pick().ConfigureAwait(true) is { } path)
            {
                Choose(path);
            }
        }).ConfigureAwait(true);
        Start.Click += async (_, _) =>
        {
            if (_path is not null)
            {
                await RunAsync(() => start(_path)).ConfigureAwait(true);
            }
        };
        Skip.Click += (_, _) => skip();
        DropZone.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        DropZone.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.DataTransfer.TryGetFiles()?.OfType<IStorageFolder>().FirstOrDefault()?.TryGetLocalPath() is { } path)
            {
                Choose(path);
            }

            e.Handled = true;
        });

        async Task RunAsync(Func<Task> action)
        {
            if (!IsEnabled)
            {
                return;
            }

            IsEnabled = false;
            Error.IsVisible = false;
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                Error.Text = describe(error);
                Error.IsVisible = true;
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }

    /// <summary>Starts a fresh folder selection without retaining a prior visit.</summary>
    /// <param name="language">Current shell language.</param>
    internal void Begin(InterfaceLanguage language)
    {
        Translate(language);
        _path = null;
        Error.IsVisible = false;
        Start.IsEnabled = false;
        PathLabel.Text = Text("onboarding.drop_a_folder_here");
        Pick.Focus();
    }

    /// <summary>Selects the folder before the user explicitly starts it.</summary>
    /// <param name="path">The platform's local folder path.</param>
    private void Choose(string path)
    {
        _path = path;
        PathLabel.Text = path;
        Pick.Content = Text("onboarding.choose_another");
        Error.IsVisible = false;
        Start.IsEnabled = true;
    }
}

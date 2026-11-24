using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Chooses an optional project after the model has been saved.</summary>
public sealed partial class ProjectChoice : SettingsFormView
{
    /// <summary>Progress of the reference folder landing.</summary>
    private static readonly StyledProperty<double> s_folderLanding = AvaloniaProperty.Register<ProjectChoice, double>("FolderLanding", 1);

    /// <summary>Selected folder in this visit.</summary>
    private string? _path;

    /// <summary>Initializes a new instance of the <see cref="ProjectChoice"/> class.</summary>
    public ProjectChoice() => InitializeComponent();

    /// <summary>Connects folder selection, drop, skip and activation.</summary>
    /// <param name="pick">The platform folder picker.</param>
    /// <param name="start">Starts a project on the selected root.</param>
    /// <param name="skip">Leaves setup from the final form.</param>
    /// <param name="defer">Skips setup from the step header.</param>
    /// <param name="describe">Localizes failures.</param>
    internal void Configure(Func<Task<string?>> pick, Func<string, Task> start, Action skip, Action defer, Func<Exception, string> describe)
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
        Skip.Click += (_, _) => defer();
        SkipProject.Click += (_, _) => skip();
        DropZone.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var allowed = IsEnabled && e.DataTransfer.Formats.Contains(DataFormat.File);
            SetDragOver(allowed);
            e.DragEffects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        DropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDragOver(false));
        DropZone.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            SetDragOver(false);
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
        Progress.Show(2, language);
        _path = null;
        DropZone.Classes.Remove("chosen");
        Chosen.IsVisible = false;
        DropIcon.IsVisible = PathLabel.IsVisible = true;
        Pick.Content = Text("onboarding.choose_a_folder");
        SetDragOver(false);
        Error.IsVisible = false;
        Start.IsEnabled = false;
        PathLabel.Text = Text("onboarding.drop_a_folder_here");
        Pick.Focus();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == s_folderLanding && Chosen?.RenderTransform is TransformGroup transform)
        {
            var progress = GetValue(s_folderLanding);
            Chosen.Opacity = progress;
            var scale = (ScaleTransform)transform.Children[0];
            scale.ScaleX = scale.ScaleY = 0.96 + (0.04 * progress);
            ((TranslateTransform)transform.Children[1]).Y = -12 * (1 - progress);
        }
    }

    /// <summary>Switches the drop field to the accepted drag appearance.</summary>
    /// <param name="over">Whether a file drag is over the drop field.</param>
    private void SetDragOver(bool over)
    {
        DropZone.Classes.Set("over", over);
        DropStripes.IsVisible = over;
    }

    /// <summary>Selects the folder before the user explicitly starts it.</summary>
    /// <param name="path">The platform's local folder path.</param>
    private void Choose(string path)
    {
        _path = path;
        FolderPath.Text = path;
        Avalonia.Controls.ToolTip.SetTip(FolderPath, path);
        var name = path.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault();
        FolderName.Text = string.IsNullOrEmpty(name) ? path : name;
        DropIcon.IsVisible = PathLabel.IsVisible = false;
        var first = !Chosen.IsVisible;
        Chosen.IsVisible = true;
        DropZone.Classes.Add("chosen");
        if (first)
        {
            PlayFolderLanding();
        }

        Pick.Content = Text("onboarding.choose_another");
        Error.IsVisible = false;
        Start.IsEnabled = true;
    }

    /// <summary>Plays the reference landing when the first chosen folder replaces the prompt.</summary>
    private async void PlayFolderLanding()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        using var motion = new CancellationTokenSource();
        DetachedFromVisualTree += Cancel;
        SetCurrentValue(s_folderLanding, 0d);
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(s_folderLanding, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(s_folderLanding, 1d) } },
            },
        };

        try
        {
            await animation.RunAsync(this, motion.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            DetachedFromVisualTree -= Cancel;
            SetCurrentValue(s_folderLanding, 1d);
        }

        void Cancel(object? sender, VisualTreeAttachmentEventArgs e) => motion.Cancel();
    }
}

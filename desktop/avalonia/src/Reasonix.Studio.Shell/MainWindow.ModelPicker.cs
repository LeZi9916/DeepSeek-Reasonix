using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The chat catalog the composer's labels and picker read.</summary>
    private IReadOnlyList<ModelChoice> _modelCatalog = [];

    /// <summary>Names a model the way the picker does.</summary>
    /// <param name="model">The model.</param>
    /// <returns>Alias, short name, model id, or the reference tail.</returns>
    private static string ModelText(ModelChoice model)
    {
        if (!string.IsNullOrWhiteSpace(model.ModelAlias))
        {
            return model.ModelAlias.Trim();
        }

        if (!string.IsNullOrWhiteSpace(model.ModelShort))
        {
            return model.ModelShort.Trim();
        }

        return string.IsNullOrWhiteSpace(model.Model) ? ShortModelName(model.Ref) : model.Model;
    }

    /// <summary>Drops the provider from a model reference.</summary>
    /// <param name="reference">Provider-qualified reference.</param>
    /// <returns>The model id, or the whole string.</returns>
    private static string ShortModelName(string reference)
    {
        var slash = reference.IndexOf('/');
        return slash >= 0 && slash + 1 < reference.Length ? reference[(slash + 1)..] : reference;
    }

    /// <summary>Wires the model trigger, effort trigger and their shared popup.</summary>
    private void InitializeModelPicker()
    {
        ModelPicker.Anchor = ModelButton;
        ModelPicker.ModelPicked += OnModelPicked;
        ModelPicker.EffortPicked += OnEffortPicked;
        ModelPicker.ModePicked += OnModePicked;
        ModelPicker.ManageRequested += OnManageModels;
        _ = RefreshModelCatalogAsync();
    }

    /// <summary>Opens the model panel from either half of the trigger.</summary>
    /// <param name="focusEffort">Whether the reasoning column should take focus first.</param>
    /// <returns>The completed read.</returns>
    private async Task OpenModelPickerAsync(bool focusEffort)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var draft = _draftKey;
        await TryAnswerAsync(async () =>
        {
            var models = await engine.Pages.ModelsAsync(_closing.Token).ConfigureAwait(true);
            var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (engine != _engine || draft != _draftKey || _closing.IsCancellationRequested)
            {
                return;
            }

            _modelCatalog = models;
            if (status is not null)
            {
                ShowComposeStatus(status);
            }

            ModelPicker.Show(models, _composerStatus, Language, focusEffort);
        }).ConfigureAwait(true);
    }

    /// <summary>Reads the catalog again so the trigger names the model the kernel chose.</summary>
    /// <returns>The completed read.</returns>
    private async Task RefreshModelCatalogAsync()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        try
        {
            var models = await engine.Pages.ModelsAsync(_closing.Token).ConfigureAwait(true);
            if (engine != _engine || _closing.IsCancellationRequested)
            {
                return;
            }

            _modelCatalog = models;
            BindModelTrigger(_composerStatus);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // The trigger keeps its last catalog; the next open reads again.
        }
    }

    /// <summary>Names the active model, its mark and effort on the composer trigger.</summary>
    /// <param name="status">The last authoritative session status.</param>
    private void BindModelTrigger(SessionStatus status)
    {
        var active = _modelCatalog.FirstOrDefault(model => model.Ref == status.ModelRef)
            ?? _modelCatalog.FirstOrDefault(model => model.Active);
        var name = active is null ? ShortModelName(status.ModelRef) : ModelText(active);
        ModelLabel.Text = name;
        var mark = active?.Mark ?? active?.ServiceMark ?? string.Empty;
        ModelMark.IsVisible = mark.Equals("deepseek", StringComparison.OrdinalIgnoreCase);
        ModelInitial.IsVisible = !ModelMark.IsVisible && mark.Length > 0;
        if (ModelInitial.IsVisible)
        {
            ModelInitialText.Text = mark[..1].ToUpperInvariant();
            var tone = new SolidColorBrush(Color.Parse("#8A8F87"), 0.18);
            ModelInitial.Background = tone;
            ModelInitialText.Foreground = new SolidColorBrush(Color.Parse("#8A8F87"));
        }

        EffortLabel.Text = ModelPicker.EffortText(status.Effort);
        var provider = string.IsNullOrWhiteSpace(status.ProviderDisplayName) ? string.Empty : status.ProviderDisplayName + " / ";
        ToolTip.SetTip(ModelButton, string.IsNullOrEmpty(status.ModelRef) ? MessageCatalog.Get(MessageKey.Composer.ChooseModel, Language) : provider + name + " · " + EffortLabel.Text);
    }

    /// <summary>Applies a model choice and reads back the state the kernel settled on.</summary>
    /// <param name="reference">Chosen model reference.</param>
    private async void OnModelPicked(string reference)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var draft = _draftKey;
        await TryAnswerAsync(async () =>
        {
            await engine.Pages.SetModelAsync(reference, _closing.Token).ConfigureAwait(true);
            if (engine != _engine || draft != _draftKey)
            {
                return;
            }

            var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is not null && engine == _engine && draft == _draftKey)
            {
                ShowComposeStatus(status);
                await RefreshModelCatalogAsync().ConfigureAwait(true);
                await LoadComposeTelemetryAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Applies an effort choice and reads back the settled label.</summary>
    /// <param name="level">Chosen kernel effort value.</param>
    private async void OnEffortPicked(string level)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var draft = _draftKey;
        await TryAnswerAsync(async () =>
        {
            await engine.Pages.SetEffortAsync(level, _closing.Token).ConfigureAwait(true);
            var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is not null && engine == _engine && draft == _draftKey)
            {
                ShowComposeStatus(status);
                await RefreshModelCatalogAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Turns a model mode on, or off with an empty identity.</summary>
    /// <param name="mode">Mode identity, or empty to turn the active one off.</param>
    private async void OnModePicked(string mode)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var draft = _draftKey;
        await TryAnswerAsync(async () =>
        {
            await engine.Pages.SetModelModeAsync(mode, _closing.Token).ConfigureAwait(true);
            var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is not null && engine == _engine && draft == _draftKey)
            {
                ShowComposeStatus(status);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Opens the settings destination that manages models and connections.</summary>
    private async void OnManageModels()
    {
        CloseComposeCards();
        OpenSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ShowSectionAsync("model").ConfigureAwait(true);
    }
}

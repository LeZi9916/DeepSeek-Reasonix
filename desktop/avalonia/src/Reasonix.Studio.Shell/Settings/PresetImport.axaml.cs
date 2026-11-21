using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Loads an explicitly chosen JSON file for review in a fresh preset draft.</summary>
public sealed partial class PresetImport : SettingsFormView
{
    /// <summary>The validated preview handler for the current editor.</summary>
    private Func<JsonObject, Task>? _preview;

    /// <summary>Lifetime of the window containing the picker.</summary>
    private CancellationToken _token;

    /// <summary>Initializes a new instance of the <see cref="PresetImport"/> class.</summary>
    public PresetImport() => InitializeComponent();

    /// <summary>Connects the native picker to the kernel's preview endpoint.</summary>
    /// <param name="api">Active kernel.</param>
    /// <param name="language">Reader's language.</param>
    /// <param name="opened">Opens a validated draft.</param>
    /// <param name="fail">Displays read or validation failure.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(StudioPageClient api, InterfaceLanguage language, Action<JsonObject> opened, Action<Exception> fail, CancellationToken token)
    {
        Translate(language);
        _token = token;
        _preview = async definition => opened(await api.PreviewPresetAsync(definition, token).ConfigureAwait(true));
        Browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage || !IsEnabled)
            {
                return;
            }

            IsEnabled = false;
            try
            {
                var chosen = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Text("persona.import_a_preset_file_preview"),
                    FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
                    AllowMultiple = false,
                }).ConfigureAwait(true);
                if (chosen.FirstOrDefault() is { } file)
                {
                    await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
                    await PreviewAsync(stream).ConfigureAwait(true);
                }
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    fail(error);
                }
            }
            finally
            {
                IsEnabled = true;
            }
        };
    }

    /// <summary>Validates the same bounded file stream supplied by the native picker.</summary>
    /// <param name="stream">The user-selected file.</param>
    /// <returns>The opening of a draft, without saving any definition.</returns>
    internal async Task PreviewAsync(Stream stream)
    {
        var buffer = new byte[(4 << 20) + 1];
        var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, _token).ConfigureAwait(true);
        if (length == buffer.Length || JsonNode.Parse(buffer.AsSpan(0, length)) is not JsonObject definition)
        {
            throw new InvalidDataException(Text("agent_composition.invalid"));
        }

        if (_preview is not null)
        {
            await _preview(definition).ConfigureAwait(true);
        }
    }
}

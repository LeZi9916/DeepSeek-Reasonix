using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The single completion request that still owns this draft.</summary>
    private CancellationTokenSource? _completing;

    /// <summary>The replacement span for the currently displayed choices.</summary>
    private ComposerCompletion? _suggestions;

    /// <summary>Wires file intake, completion and the session model chooser.</summary>
    private void InitializeIntake()
    {
        Label(Attach, MessageKey.Composer.AddAnAttachment);
        Label(ModelButton, MessageKey.Composer.ChooseModel);
        ModelLabel.Text = MessageCatalog.Get(MessageKey.Composer.ChooseModel, Language);
        Attach.Click += OnPickFiles;
        ModelButton.Click += async (_, _) => await OpenModelPickerAsync(focusEffort: false).ConfigureAwait(true);
        EffortButton.Click += async (_, _) => await OpenModelPickerAsync(focusEffort: true).ConfigureAwait(true);
        EffortLabel.Text = MessageCatalog.Get(MessageKey.Common.Auto, Language);
        Label(EffortButton, MessageKey.Composer.ReasoningEffort);
        InitializeModelPicker();
        InitializeComposeTools();
        Composer.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.TextProperty)
            {
                ScheduleSuggestions();
            }
        };
        Composer.AddHandler(KeyDownEvent, OnPasteShortcut, RoutingStrategies.Tunnel);
        Suggestions.Tapped += (_, _) => AcceptSuggestion(descend: false);
        DragDrop.SetAllowDrop(ComposerArea, true);
        DragDrop.AddDragOverHandler(ComposerArea, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        DragDrop.AddDropHandler(ComposerArea, OnDropFiles);
    }

    /// <summary>Invalidates outstanding reads and removes the current suggestion menu.</summary>
    private void DismissSuggestions()
    {
        _completing?.Cancel();
        _completing = null;
        _suggestions = null;
        Suggestions.ItemsSource = null;
        Suggestions.IsVisible = false;
    }

    /// <summary>Requests completions for the latest draft and caret only.</summary>
    private async void ScheduleSuggestions()
    {
        DismissSuggestions();
        var line = Composer.Text ?? string.Empty;
        if (_engine is null || !(line.StartsWith('/') || line.Contains('@') || line.Contains('#')))
        {
            return;
        }

        using var request = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _completing = request;
        var caret = Composer.CaretIndex;
        try
        {
            await Task.Delay(65, request.Token).ConfigureAwait(true);
            var result = await _engine.Pages.CompleteAsync(line, caret, Language == InterfaceLanguage.Zh ? "zh" : "en", request.Token).ConfigureAwait(true);
            if (_completing != request || result.From < 0 || result.To < result.From || result.To > line.Length || result.Items is not { Length: > 0 })
            {
                return;
            }

            _suggestions = result;
            Suggestions.ItemsSource = result.Typed is null ? result.Items : result.Items.Prepend(result.Typed).ToArray();
            Suggestions.SelectedIndex = 0;
            Suggestions.IsVisible = true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (_completing == request)
            {
                ShowRefusal(error);
            }
        }
        finally
        {
            if (_completing == request)
            {
                _completing = null;
            }
        }
    }

    /// <summary>Routes selection keys before the composer considers sending.</summary>
    /// <param name="e">The key press.</param>
    /// <returns>Whether the menu consumed it.</returns>
    private bool HandleSuggestionKey(KeyEventArgs e)
    {
        if (!Suggestions.IsVisible || e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                var count = Suggestions.ItemCount;
                Suggestions.SelectedIndex = (Suggestions.SelectedIndex + (e.Key == Key.Up ? count - 1 : 1)) % count;
                Suggestions.ScrollIntoView(Suggestions.SelectedIndex);
                break;
            case Key.Escape:
                DismissSuggestions();
                break;
            case Key.Tab:
            case Key.Enter:
                if (e.Key == Key.Enter && _suggestions?.Typed is { } typed && Equals(Suggestions.SelectedItem, typed))
                {
                    DismissSuggestions();
                    return false;
                }

                AcceptSuggestion(e.Key == Key.Tab);
                break;
            default:
                return false;
        }

        e.Handled = true;
        return true;
    }

    /// <summary>Splices the kernel's suggestion at its UTF-16 offsets.</summary>
    /// <param name="descend">Whether Tab should continue into a directory.</param>
    private void AcceptSuggestion(bool descend)
    {
        if (_suggestions is not { } menu || Suggestions.SelectedItem is not CompletionChoice choice)
        {
            return;
        }

        var text = Composer.Text ?? string.Empty;
        if (menu.To > text.Length)
        {
            DismissSuggestions();
            return;
        }

        var needsSpace = menu.Kind == "ref" && !(descend && choice.Descend)
            && (menu.To == text.Length || !char.IsWhiteSpace(text[menu.To]));
        var separator = needsSpace ? " " : string.Empty;
        var replacement = choice.Insert + separator;
        Composer.SelectionStart = menu.From;
        Composer.SelectionEnd = menu.To;
        Composer.SelectedText = replacement;
        Composer.CaretIndex = menu.From + replacement.Length;
        Composer.Focus();
        if (!choice.Descend || !descend)
        {
            DismissSuggestions();
        }
    }

    /// <summary>Opens the platform picker and resolves the selected files.</summary>
    /// <param name="sender">The attachment button.</param>
    /// <param name="e">The click.</param>
    private async void OnPickFiles(object? sender, RoutedEventArgs e)
    {
        var draft = _draftKey;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = MessageCatalog.Get(MessageKey.Composer.AddAnAttachment, Language),
                AllowMultiple = true,
            }).ConfigureAwait(true);
            await InsertFilesAsync(files, draft).ConfigureAwait(true);
        }
        catch (Exception error) when (!_closing.IsCancellationRequested)
        {
            ShowRefusal(error);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Resolves native file drops through the same endpoint as the picker.</summary>
    /// <param name="sender">The composer.</param>
    /// <param name="e">The drop.</param>
    private async void OnDropFiles(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.DataTransfer.TryGetFiles() is { } files)
        {
            await InsertFilesAsync(files, _draftKey).ConfigureAwait(true);
        }
    }

    /// <summary>Preserves the editor's normal paste for non-file clipboard content.</summary>
    /// <param name="sender">The editor.</param>
    /// <param name="e">The key press.</param>
    private async void OnPasteShortcut(object? sender, KeyEventArgs e)
    {
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key != Key.V || e.KeyModifiers != primary || _engine is null || Clipboard is not { } clipboard)
        {
            return;
        }

        e.Handled = true;
        var draft = _draftKey;
        try
        {
            if (await clipboard.TryGetFilesAsync().ConfigureAwait(true) is { Length: > 0 } paths)
            {
                await InsertFilesAsync(paths, draft).ConfigureAwait(true);
            }
            else if (await clipboard.TryGetBitmapAsync().ConfigureAwait(true) is { } bitmap)
            {
                using (bitmap)
                {
                    using var bytes = new MemoryStream();
                    bitmap.Save(bytes, PngBitmapEncoderOptions.Default);
                    bytes.Position = 0;
                    if (draft == _draftKey && !_closing.IsCancellationRequested)
                    {
                        var pasted = await _engine.Pages.PasteImageAsync(bytes, _closing.Token).ConfigureAwait(true);
                        if (draft == _draftKey && pasted.Ref is { Length: > 0 } reference)
                        {
                            AddAttachmentReference(reference);
                        }
                    }
                }
            }
            else if (draft == _draftKey && !_closing.IsCancellationRequested && Composer.IsKeyboardFocusWithin)
            {
                Composer.Paste();
            }
        }
        catch (Exception error) when (!_closing.IsCancellationRequested)
        {
            ShowRefusal(error);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Inserts host-issued references only into the originating conversation.</summary>
    /// <param name="files">Native storage items chosen by the user.</param>
    /// <param name="draft">Draft identity captured before the interaction.</param>
    /// <returns>The completed drop.</returns>
    private async Task InsertFilesAsync(IEnumerable<IStorageItem> files, string? draft)
    {
        if (_engine is null || _closing.IsCancellationRequested || draft != _draftKey)
        {
            return;
        }

        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        try
        {
            var refs = await _engine.Pages.DropAsync(paths, _closing.Token).ConfigureAwait(true);
            if (_closing.IsCancellationRequested || draft != _draftKey)
            {
                return;
            }

            AddAttachments(refs);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ShowRefusal(error);
        }
    }

    /// <summary>Places references at the current selection with a token boundary.</summary>
    /// <param name="insert">The serialized reference text.</param>
    private void InsertReferences(string insert)
    {
        var text = Composer.Text ?? string.Empty;
        var start = Math.Min(Composer.SelectionStart, Composer.SelectionEnd);
        var prefix = start > 0 && !char.IsWhiteSpace(text[start - 1]) ? " " : string.Empty;
        Composer.SelectionStart = start;
        Composer.SelectionEnd = start;
        Composer.SelectedText = prefix + insert + " ";
        Composer.Focus();
    }
}

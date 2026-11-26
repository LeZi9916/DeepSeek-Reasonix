using Avalonia.Controls;
using Avalonia.Input;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Installs desktop editing and window commands in the macOS menu bar.</summary>
    private void ConfigureMacMenu()
    {
        var edit = new NativeMenu();
        Edit("workbench.undo", Key.Z, box => box.Undo(), box => box.CanUndo, editor => editor.Undo(), editor => !editor.IsReadOnly && editor.CanUndo);
        Edit("workbench.redo", Key.Z, box => box.Redo(), box => box.CanRedo, editor => editor.Redo(), editor => !editor.IsReadOnly && editor.CanRedo, shift: true);
        edit.Add(new NativeMenuItemSeparator());
        Edit("workbench.cut", Key.X, box => box.Cut(), box => box.CanCut, editor => editor.Cut(), editor => !editor.IsReadOnly && editor.SelectionLength > 0);
        Edit("common.copy", Key.C, box => box.Copy(), box => box.CanCopy, editor => editor.Copy(), editor => editor.SelectionLength > 0, text => text.Copy());
        Edit("workbench.paste", Key.V, box => box.Paste(), box => box.CanPaste, editor => editor.Paste(), editor => !editor.IsReadOnly);
        edit.Add(new NativeMenuItemSeparator());
        Edit("workbench.select_all", Key.A, box => box.SelectAll(), _ => true, editor => editor.SelectAll(), _ => true, text => text.SelectAll());

        var window = new NativeMenu();
        window.Add(Item("app.minimize", Key.M, KeyModifiers.Meta, () => WindowState = WindowState.Minimized));
        window.Add(Item("common.close", Key.W, KeyModifiers.Meta, Close));
        NativeMenu.SetMenu(this, new NativeMenu
        {
            new NativeMenuItem(MessageCatalog.Get("common.edit", Language)) { Menu = edit },
            new NativeMenuItem(MessageCatalog.Get("common.window", Language)) { Menu = window },
        });

        void Edit(
            string key,
            Key shortcut,
            Action<TextBox> run,
            Func<TextBox, bool> enabled,
            Action<WorkbenchFileEditor> runInEditor,
            Func<WorkbenchFileEditor, bool> editorEnabled,
            Action<SelectableTextBlock>? select = null,
            bool shift = false)
        {
            var item = Item(key, shortcut, KeyModifiers.Meta | (shift ? KeyModifiers.Shift : KeyModifiers.None), () =>
            {
                if (FileEditor.IsKeyboardFocusWithin && editorEnabled(FileEditor))
                {
                    runInEditor(FileEditor);
                    return;
                }

                switch (FocusManager?.GetFocusedElement())
                {
                    case TextBox box when enabled(box):
                        run(box);
                        break;
                    case SelectableTextBlock text:
                        select?.Invoke(text);
                        break;
                }
            });
            edit.NeedsUpdate += (_, _) =>
            {
                if (FileEditor.IsKeyboardFocusWithin)
                {
                    item.IsEnabled = editorEnabled(FileEditor);
                    return;
                }

                item.IsEnabled = FocusManager?.GetFocusedElement() switch
                {
                    TextBox box => enabled(box),
                    SelectableTextBlock => select is not null,
                    _ => false,
                };
            };
            edit.Add(item);
        }

        NativeMenuItem Item(string key, Key shortcut, KeyModifiers modifiers, Action run)
        {
            var item = new NativeMenuItem(MessageCatalog.Get(key, Language)) { Gesture = new KeyGesture(shortcut, modifiers) };
            item.Click += (_, _) => run();
            return item;
        }
    }
}

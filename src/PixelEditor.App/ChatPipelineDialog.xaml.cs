// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Windows;
using PixelEditor.ViewModels;

namespace PixelEditor.App;

/// <summary>Pipeline from an ordinary AI chat: prompt, paste the answer, edit, preview. All logic lives in ChatPipelineViewModel.</summary>
public partial class ChatPipelineDialog : Window
{
    public ChatPipelineDialog()
    {
        InitializeComponent();
        // Ctrl+V / "Paste" in the box menu: the pasted text becomes clean JSON and is remembered as a variant at once
        DataObject.AddPastingHandler(Editor, (_, e) =>
        {
            if (DataContext is ChatPipelineViewModel vm && e.DataObject.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
            {
                e.CancelCommand();
                vm.PasteText(text);
            }
        });
        // the box lost focus after a manual edit: tidy the text
        Editor.LostKeyboardFocus += (_, _) => (DataContext as ChatPipelineViewModel)?.Normalize();
    }
}

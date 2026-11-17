// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.ViewModels;

public enum MessageKind { Info, Warning, Error, Question }

/// <summary>A notification button. Key is returned as the result; IsCancel fires on Esc and when the window is closed.</summary>
public sealed record MessageButton(string Text, string Key, bool IsPrimary = false, bool IsDefault = false, bool IsCancel = false);

/// <summary>
/// A notification or question in the application's shared style (instead of system windows). Shown through IDialogService.ShowDialog,
/// the chosen button is in ChosenKey. Closing with the cross or Esc is equivalent to the cancel button (or to the first button if there is no cancel).
/// </summary>
public sealed class MessageViewModel : DialogViewModel
{
    public const string Ok = "ok", Yes = "yes", No = "no", Save = "save", Discard = "discard", Cancel = "cancel";

    public MessageViewModel(string title, string message, MessageKind kind, IReadOnlyList<MessageButton> buttons, string? details = null)
    {
        Title = title;
        Message = message;
        Kind = kind;
        Buttons = buttons;
        Details = details;
        ChooseCommand = new RelayCommand(p => Choose((string)p!));
    }

    public override string Title { get; }
    public string Message { get; }
    public MessageKind Kind { get; }
    public IReadOnlyList<MessageButton> Buttons { get; }

    /// <summary>Technical details (for example the exception text) under the message; null if there are none.</summary>
    public string? Details { get; }
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public string? ChosenKey { get; private set; }
    public RelayCommand ChooseCommand { get; }

    /// <summary>The icon symbol from the Segoe MDL2 Assets font.</summary>
    public string Glyph => Kind switch
    {
        MessageKind.Error => "",
        MessageKind.Warning => "",
        MessageKind.Question => "",
        _ => "",
    };

    /// <summary>The key of the button that fires when the window is closed without a choice.</summary>
    public string DismissKey => (Buttons.FirstOrDefault(b => b.IsCancel) ?? Buttons[0]).Key;

    private void Choose(string key)
    {
        ChosenKey = key;
        var button = Buttons.FirstOrDefault(b => b.Key == key);
        Close(button is { IsCancel: false });
    }

    /// <summary>Called when the window is closed with the cross: the result is the same as the cancel button.</summary>
    public void Dismiss() => ChosenKey ??= DismissKey;

    // ---- ready-made notifications ----

    public static MessageViewModel Info(string title, string message) =>
        new(title, message, MessageKind.Info, [new MessageButton("OK", Ok, IsPrimary: true, IsDefault: true, IsCancel: true)]);

    public static MessageViewModel Warning(string title, string message) =>
        new(title, message, MessageKind.Warning, [new MessageButton("OK", Ok, IsPrimary: true, IsDefault: true, IsCancel: true)]);

    public static MessageViewModel Error(string message, string? details = null) =>
        new(Loc.T("batch.error_3"), message, MessageKind.Error, [new MessageButton("OK", Ok, IsPrimary: true, IsDefault: true, IsCancel: true)], details);

    public static MessageViewModel Confirm(string title, string message, string? yes = null, string? no = null) =>
        new(title, message, MessageKind.Question,
        [
            new MessageButton(yes ?? Loc.T("msg.yes"), Yes, IsPrimary: true, IsDefault: true),
            new MessageButton(no ?? Loc.T("msg.no"), No, IsCancel: true),
        ]);

    /// <summary>A question with a dangerous action (deletion): the main button is named after the action, cancel is selected by default.</summary>
    public static MessageViewModel ConfirmDestructive(string title, string message, string? action = null) =>
        new(title, message, MessageKind.Warning,
        [
            new MessageButton(action ?? Loc.T("common.delete"), Yes, IsPrimary: true),
            new MessageButton(Loc.T("common.cancel"), No, IsDefault: true, IsCancel: true),
        ]);

    public static MessageViewModel SaveChanges() =>
        new("Pixel Editor", Loc.T("msg.save_changes_before_continuing"), MessageKind.Question,
        [
            new MessageButton(Loc.T("colors.save"), Save, IsPrimary: true, IsDefault: true),
            new MessageButton(Loc.T("msg.don_t_save"), Discard),
            new MessageButton(Loc.T("common.cancel"), Cancel, IsCancel: true),
        ]);
}

/// <summary>Showing ready-made notifications through the shared dialog mechanism.</summary>
public static class MessageExtensions
{
    /// <summary>The key of the chosen button (when the window is closed without a choice, the key of the cancel button).</summary>
    public static string Ask(this IDialogService dialogs, MessageViewModel message)
    {
        dialogs.ShowDialog(message);
        message.Dismiss();
        return message.ChosenKey!;
    }

    /// <summary>Confirmation of a dangerous action (deletion): true only if the main button was pressed.</summary>
    public static bool ConfirmDestructive(this IDialogService dialogs, string title, string message, string? action = null) =>
        dialogs.Ask(MessageViewModel.ConfirmDestructive(title, message, action)) == MessageViewModel.Yes;
}

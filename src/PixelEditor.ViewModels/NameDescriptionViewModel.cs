// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.ViewModels;

/// <summary>
/// A general "name and description" dialog: save a palette to the library, rename, save a template.
/// validate returns the error text for the name or null if it is acceptable. Colors (if given) are shown as a strip.
/// </summary>
public sealed class NameDescriptionViewModel : DialogViewModel
{
    private readonly Func<string, string?>? _validate;
    private string _name, _description, _error = "";

    public NameDescriptionViewModel(string title, string name, string description, Func<string, string?>? validate = null,
        IReadOnlyList<uint>? colors = null, string? okText = null, string? hint = null)
    {
        Title = title;
        _name = name;
        _description = description;
        _validate = validate;
        Colors = colors;
        OkText = okText ?? Loc.T("colors.save");
        Hint = hint;
        OkCommand = new RelayCommand(Ok);
    }

    public override string Title { get; }
    public string OkText { get; }
    public string? Hint { get; }
    public IReadOnlyList<uint>? Colors { get; }
    public bool HasColors => Colors is { Count: > 0 };
    public string ColorsInfo => Colors == null ? "" : Loc.T("name_desc.colors", Colors.Count);

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) Error = ""; }
    }

    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    public RelayCommand OkCommand { get; }

    private void Ok()
    {
        var name = (_name ?? "").Trim();
        if (name.Length == 0) { Error = Loc.T("name_desc.enter_a_name"); return; }
        var problem = _validate?.Invoke(name);
        if (problem != null) { Error = problem; return; }
        _name = name;
        Close(true);
    }
}

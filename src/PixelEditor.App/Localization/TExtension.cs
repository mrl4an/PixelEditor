// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Windows.Data;
using System.Windows.Markup;
using PixelEditor.ViewModels;

namespace PixelEditor.App.Localization;

/// <summary>
/// XAML markup for localized text: <c>Header="{loc:T menu.file}"</c>.
/// Provides a binding to the <see cref="Loc"/> indexer, so the text changes on the fly when the language changes.
/// Works for any property (Header, Content, ToolTip, Text, Title).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) { Key = key; }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("[" + Key + "]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}

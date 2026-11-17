// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace PixelEditor.App;

/// <summary>Color strip. Display only: two bindable settings (the color list and the tile size).</summary>
public partial class SwatchStrip : UserControl
{
    public static readonly DependencyProperty ColorsProperty =
        DependencyProperty.Register(nameof(Colors), typeof(IEnumerable), typeof(SwatchStrip));

    public static readonly DependencyProperty SwatchSizeProperty =
        DependencyProperty.Register(nameof(SwatchSize), typeof(double), typeof(SwatchStrip), new PropertyMetadata(14.0));

    public SwatchStrip() => InitializeComponent();

    public IEnumerable? Colors { get => (IEnumerable?)GetValue(ColorsProperty); set => SetValue(ColorsProperty, value); }
    public double SwatchSize { get => (double)GetValue(SwatchSizeProperty); set => SetValue(SwatchSizeProperty, value); }
}

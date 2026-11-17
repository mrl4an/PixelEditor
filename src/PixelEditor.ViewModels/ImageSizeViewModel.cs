// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

public sealed record ImageSizeResult(int Width, int Height, ResizeMethod Method);

/// <summary>
/// Image size: scaling the drawing (all layers) to a new size with the chosen method. Differs from "Canvas size", which
/// changes the field without scaling. You can set width and height, percent or the ×2, ÷2 buttons; the method affects quality when shrinking.
/// </summary>
public sealed class ImageSizeViewModel : DialogViewModel
{
    private readonly int _w0, _h0;
    private string _width, _height, _percent = "100", _error = "";
    private bool _keepAspect = true, _syncing;
    private Choice _method;

    public ImageSizeViewModel(int width, int height)
    {
        _w0 = width;
        _h0 = height;
        _width = width.ToString();
        _height = height.ToString();
        Methods =
        [
            new Choice(nameof(ResizeMethod.Mode), Loc.T("image_size.most_frequent_color_mode_pixel")),
            new Choice(nameof(ResizeMethod.Smart), Loc.T("image_size.edge_preserving_smart_thin_lines")),
            new Choice(nameof(ResizeMethod.Box), Loc.T("image_size.averaging_box_photos_and_gradients")),
            new Choice(nameof(ResizeMethod.Nearest), Loc.T("image_size.nearest_nearest_fast_for_enlarging")),
        ];
        _method = Methods[0];
        OkCommand = new RelayCommand(Ok);
        ScaleCommand = new RelayCommand(p => SetFactor(Convert.ToDouble(p, CultureInfo.InvariantCulture)));
        ResetCommand = new RelayCommand(() => SetSize(_w0, _h0));
    }

    public override string Title => Loc.T("image_size.image_size");
    public string CurrentInfo => Loc.T("image_size.currently_px", _w0, _h0);
    public IReadOnlyList<Choice> Methods { get; }

    public Choice SelectedMethod
    {
        get => _method;
        set { if (value != null && SetProperty(ref _method, value)) OnPropertyChanged(nameof(Hint)); }
    }

    public bool KeepAspect { get => _keepAspect; set => SetProperty(ref _keepAspect, value); }

    public string WidthText
    {
        get => _width;
        set
        {
            if (!SetProperty(ref _width, value) || _syncing) return;
            if (_keepAspect && int.TryParse(value, out var w) && w > 0)
                SetOther(h: Math.Max(1, (int)Math.Round((double)_h0 * w / _w0)));
            UpdateDerived();
        }
    }

    public string HeightText
    {
        get => _height;
        set
        {
            if (!SetProperty(ref _height, value) || _syncing) return;
            if (_keepAspect && int.TryParse(value, out var h) && h > 0)
                SetOther(w: Math.Max(1, (int)Math.Round((double)_w0 * h / _h0)));
            UpdateDerived();
        }
    }

    /// <summary>Percent of the current size (50 halves, 200 doubles).</summary>
    public string PercentText
    {
        get => _percent;
        set
        {
            if (!SetProperty(ref _percent, value) || _syncing) return;
            if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p > 0)
                SetFactor(p / 100.0);
        }
    }

    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>A hint: what will result and what the chosen method risks.</summary>
    public string Hint
    {
        get
        {
            if (!int.TryParse(_width, out var w) || !int.TryParse(_height, out var h)) return "";
            string size = Loc.T("image_size.will_become_px", w, h);
            if (w >= _w0 && h >= _h0) return size + Loc.T("image_size.enlarging_nearest_is_better_pixels");
            return _method.Key switch
            {
                nameof(ResizeMethod.Box) => size + Loc.T("image_size.averaging_smooths_edges_and_adds"),
                nameof(ResizeMethod.Nearest) => size + Loc.T("image_size.nearest_is_fast_but_thin"),
                nameof(ResizeMethod.Smart) => size + Loc.T("image_size.edge_preserving_picks_the_main"),
                _ => size + Loc.T("image_size.most_frequent_color_keeps_edges"),
            };
        }
    }

    public ImageSizeResult? Result { get; private set; }
    public RelayCommand OkCommand { get; }
    public RelayCommand ScaleCommand { get; }
    public RelayCommand ResetCommand { get; }

    private void SetFactor(double k) =>
        SetSize(Math.Max(1, (int)Math.Round(_w0 * k)), Math.Max(1, (int)Math.Round(_h0 * k)));

    private void SetSize(int w, int h)
    {
        _syncing = true;
        WidthText = w.ToString();
        HeightText = h.ToString();
        _syncing = false;
        UpdateDerived();
    }

    private void SetOther(int? w = null, int? h = null)
    {
        _syncing = true;
        if (w != null) WidthText = w.Value.ToString();
        if (h != null) HeightText = h.Value.ToString();
        _syncing = false;
    }

    private void UpdateDerived()
    {
        if (int.TryParse(_width, out var w) && w > 0)
        {
            _syncing = true;
            PercentText = Math.Round(100.0 * w / _w0, 1).ToString(CultureInfo.InvariantCulture);
            _syncing = false;
        }
        Error = "";
        OnPropertyChanged(nameof(Hint));
    }

    private void Ok()
    {
        if (!int.TryParse(_width, out var w) || !int.TryParse(_height, out var h) || w < 1 || h < 1 || w > 8192 || h > 8192)
        {
            Error = Loc.T("image_size.the_size_must_be_a");
            return;
        }
        Result = new ImageSizeResult(w, h, Enum.Parse<ResizeMethod>(_method.Key));
        Close(true);
    }
}

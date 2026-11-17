// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>
/// A real-size preview window: the drawing (all visible layers) is shown in three variants side by side: on white, on a transparent grid and on black.
/// It updates together with the edits. The default zoom is 1×; larger for small sprites (whole multiples, no blur).
/// </summary>
public sealed class PreviewViewModel : DialogViewModel
{
    private readonly EditorSession _s;
    private PixelImage _image;
    private int _scale = 1;

    public PreviewViewModel(EditorSession session)
    {
        _s = session;
        _image = _s.Doc.Flatten();
        _s.ContentChanged += Refresh;
        _s.DocumentChanged += Refresh;
        _s.CanvasResized += Refresh;
        _s.LayersChanged += Refresh;
    }

    public override string Title => Loc.T("preview.real_size_preview");

    public bool IsOpen { get; private set; } = true;

    public PixelImage Image { get => _image; private set => SetProperty(ref _image, value); }

    public int Scale
    {
        get => _scale;
        set
        {
            if (!SetProperty(ref _scale, Math.Clamp(value, 1, 8))) return;
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
            OnPropertyChanged(nameof(Info));
        }
    }

    public int DisplayWidth => _image.Width * _scale;
    public int DisplayHeight => _image.Height * _scale;
    public string Info => Loc.T("preview.px_zoom", _image.Width, _image.Height, _scale);

    private void Refresh()
    {
        Image = _s.Doc.Flatten();
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
        OnPropertyChanged(nameof(Info));
    }

    protected override void OnClosed()
    {
        IsOpen = false;
        _s.ContentChanged -= Refresh;
        _s.DocumentChanged -= Refresh;
        _s.CanvasResized -= Refresh;
        _s.LayersChanged -= Refresh;
    }
}

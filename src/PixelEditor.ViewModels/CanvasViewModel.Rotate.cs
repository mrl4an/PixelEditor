// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>Rotation by an arbitrary angle: of the selection (shapes included) or, if there is no selection, of the whole layer.</summary>
public sealed partial class CanvasViewModel
{
    // rotation is always crisp (even edges, no new colors); the old "crisp rotation" option was removed
    private RelayCommand? _rotateBy, _rotateAsk;


    /// <summary>Rotate by an angle in degrees (clockwise; negative means counterclockwise). The command parameter is a number.</summary>
    public RelayCommand RotateByCommand => _rotateBy ??= new RelayCommand(p => ApplyRotation(Convert.ToDouble(p, CultureInfo.InvariantCulture)));

    /// <summary>Asks for the angle and rotates.</summary>
    public RelayCommand RotateAskCommand => _rotateAsk ??= new RelayCommand(AskRotation);

    private void AskRotation()
    {
        var text = _s.Dialogs.Prompt(Loc.T("main.rotation"), HasSelection
            ? Loc.T("canvas.angle_in_degrees_clockwise_can")
            : Loc.T("canvas.angle_in_degrees_clockwise_can_2"), "15");
        if (text == null) return;
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
        {
            _s.Status(Loc.T("canvas.could_not_understand_the_angle"));
            return;
        }
        ApplyRotation(angle);
    }

    /// <summary>Rotation of the selection (it stays floating, a click outside commits it) or of the whole layer around the center.</summary>
    public void ApplyRotation(double degrees)
    {
        if (Math.Abs(degrees % 360) < 1e-9) { _s.Status(Loc.T("canvas.angle_0_nothing_to_rotate")); return; }
        const bool smooth = true;
        if (HasSelection)
        {
            TransformSelection(img => Rotation.Rotate(img, degrees, smooth, expand: true), Loc.T("canvas.rotated_by", degrees));
            return;
        }
        if (!_s.CanEditActiveLayer()) return;
        _s.ApplyResult(Rotation.Rotate(_s.ActiveLayer.ToImage(), degrees, smooth, expand: false), Loc.T("canvas.rotation_by", degrees));
    }

    // ---- rotation by the handle, as in Paint: drag the handle above the selection, the angle is measured from the center ----

    private (PixelImage Pixels, bool[]? Mask, int X, int Y)? _rotBase;
    private double _rotAngle;

    /// <summary>The selection center (to compute the angle in the UI) or null if there is no selection.</summary>
    public (double X, double Y)? SelectionCenter => (_float != null ? _float.Bounds : _selRect) is PixelRect r && !r.IsEmpty
        ? (r.X + r.Width / 2.0, r.Y + r.Height / 2.0) : null;

    public bool IsRotating => _rotBase != null;

    /// <summary>The selection frame at the moment rotation starts (a rotated frame is drawn around it) and the current angle.</summary>
    public PixelRect? RotateBaseRect => _rotBase is { } b ? new PixelRect(b.X, b.Y, b.Pixels.Width, b.Pixels.Height) : null;
    public double RotateAngle => _rotAngle;

    /// <summary>Start of rotation by the handle: the selection is lifted and the original state is remembered.</summary>
    public bool BeginRotateHandle()
    {
        if (_batch != null || _drag != DragMode.None || !HasSelection || !_s.CanEditActiveLayer()) return false;
        if (_float == null && !(_selRect is PixelRect r && !r.IsEmpty && TryLift(r))) return false;
        _rotBase = _float!.Save();
        _rotAngle = 0;
        return true;
    }

    /// <summary>The angle in degrees clockwise from the original position; recomputed from the snapshot, so quality does not drop.</summary>
    public void RotateHandleTo(double degrees)
    {
        if (_rotBase is not { } b || _float == null) return;
        _rotAngle = degrees;
        _float.Restore(b);
        if (Math.Abs(degrees % 360) > 1e-9)
        {
            const bool smooth = true;
            _float.Transform(img => Rotation.Rotate(img, degrees, smooth, expand: true));
        }
        UpdateSelection();
        _s.RequestRender();
        _s.Status(Loc.T("canvas.rotated_by", Math.Round(degrees, 1)));
    }

    public void EndRotateHandle() => _rotBase = null;
}

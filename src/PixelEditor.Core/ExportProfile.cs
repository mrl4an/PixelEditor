// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>
/// The last animation export settings of a document (stored in .pixproj): output folder, base name, chosen formats, Godot texture path
/// and the names of the rows that do not loop. Every member has a default so that older or partial JSON still loads.
/// </summary>
public sealed record AnimationExportProfile(
    string Folder = "",
    string BaseName = "",
    bool Aseprite = true,
    bool Godot = true,
    bool Gif = true,
    bool Simple = false,
    bool Unity = false,
    string GodotTexturePath = "",
    string[]? NoLoopRows = null)
{
    public IReadOnlyList<string> NoLoop => NoLoopRows ?? [];
}

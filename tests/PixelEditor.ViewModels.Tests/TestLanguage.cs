// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Runtime.CompilerServices;
using PixelEditor.Core;

namespace PixelEditor.ViewModels.Tests;

/// <summary>The model tests check Russian texts; the core language is Russian in them until a test switches Loc.Instance.</summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Init() => CoreText.Language = "ru";
}

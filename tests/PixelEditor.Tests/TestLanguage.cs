// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Runtime.CompilerServices;
using PixelEditor.Core;

namespace PixelEditor.Tests;

/// <summary>The tests check Russian texts (the source strings of the code): the core language defaults to Russian in them, in the CLI and MCP to English.</summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Init() => CoreText.Language = "ru";
}

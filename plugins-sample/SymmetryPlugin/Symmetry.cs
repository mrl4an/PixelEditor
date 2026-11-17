// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.Core.Plugins;

namespace SymmetryPlugin;

/// <summary>
/// Sample check plugin: the sprite is horizontally symmetric.
/// Usage in a template: { "check": "symmetricX", "maxMismatch": 0 }
/// </summary>
public sealed class SymmetricXCheck : IPixelCheck
{
    public string Name => "symmetricX";
    public string Description => "Левая и правая половины зеркальны. Допускается maxMismatch несовпавших пикселей.";
    public IReadOnlyList<string> Parameters => ["maxMismatch:int=0"];

    public CheckResult Run(PixelImage image, OpArgs args)
    {
        int allowed = args.Int("maxMismatch", 0);
        int mismatch = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width / 2; x++)
                if (image.Get(x, y) != image.Get(image.Width - 1 - x, y))
                    mismatch++;
        return mismatch <= allowed
            ? CheckResult.Pass($"несовпадающих пар: {mismatch}")
            : CheckResult.Fail($"несовпадающих пар пикселей: {mismatch}, допустимо {allowed}");
    }
}

/// <summary>Sample operation plugin: the right half becomes a mirrored copy of the left half.</summary>
public sealed class MirrorLeftToRightOperation : IPixelOperation
{
    public string Name => "mirrorLeftToRight";
    public string Description => "Заменяет правую половину зеркальной копией левой (делает спрайт симметричным).";
    public IReadOnlyList<string> Parameters => [];

    public PixelImage Run(PixelImage input, OpArgs args)
    {
        var result = input.Clone();
        for (int y = 0; y < input.Height; y++)
            for (int x = 0; x < input.Width / 2; x++)
                result.Set(input.Width - 1 - x, y, input.Get(x, y));
        return result;
    }
}

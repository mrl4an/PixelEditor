// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class ImageSizeTests
{
    [Fact]
    public void Dialog_KeepsAspect_SyncsPercent_AndOffersScaleButtons()
    {
        var vm = new ImageSizeViewModel(40, 20);
        Assert.Equal("Сейчас 40×20 px", vm.CurrentInfo);
        vm.WidthText = "20";
        Assert.Equal("10", vm.HeightText);
        Assert.Equal("50", vm.PercentText);

        vm.ScaleCommand.Execute("2");
        Assert.Equal(("80", "40"), (vm.WidthText, vm.HeightText));
        vm.PercentText = "25";
        Assert.Equal(("10", "5"), (vm.WidthText, vm.HeightText));
        vm.ResetCommand.Execute(null);
        Assert.Equal(("40", "20"), (vm.WidthText, vm.HeightText));

        vm.KeepAspect = false;
        vm.WidthText = "7";
        Assert.Equal("20", vm.HeightText); // without the link the height does not change
    }

    [Fact]
    public void Dialog_HintDependsOnMethod_AndValidatesSize()
    {
        var vm = new ImageSizeViewModel(64, 64);
        vm.WidthText = "32";
        Assert.Contains("не создаёт новых цветов", vm.Hint);
        vm.SelectedMethod = vm.Methods.First(m => m.Key == "Box");
        Assert.Contains("новые цвета", vm.Hint);
        vm.WidthText = "128";
        Assert.Contains("Увеличение", vm.Hint);

        vm.WidthText = "abc";
        vm.OkCommand.Execute(null);
        Assert.Contains("числом", vm.Error);
        Assert.Null(vm.Result);

        vm.WidthText = "32";
        vm.SelectedMethod = vm.Methods.First(m => m.Key == "Nearest");
        vm.OkCommand.Execute(null);
        Assert.Equal(new ImageSizeResult(32, 32, ResizeMethod.Nearest), vm.Result);
    }

    [Fact]
    public void ImageSizeCommand_ScalesAllLayers_WithChosenMethod_AndUndo()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++) rig.Doc.Layers[0].Pixels[y * 32 + x] = (x / 2 + y / 2) % 2 == 0 ? Rig.Red : Rig.Blue;
        rig.Doc.Layers[1].Pixels[0] = Rig.Red;

        rig.Dialogs.ImageSizeConfigure = d =>
        {
            d.WidthText = "16";
            d.SelectedMethod = d.Methods.First(m => m.Key == "Mode");
        };
        rig.Vm.Canvas.ImageSizeCommand.Execute(null);

        Assert.Equal((16, 16), (rig.Doc.Width, rig.Doc.Height));
        Assert.All(rig.Doc.Layers, l => Assert.Equal(256, l.Pixels.Length));
        Assert.All(rig.Doc.Layers[0].Pixels, p => Assert.True(p == Rig.Red || p == Rig.Blue)); // mode creates no new colors

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal((32, 32), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal(Rig.Red, rig.Doc.Layers[1].Pixels[0]);
    }

    [Fact]
    public void BoxCreatesNewColors_NearestUpscalesWithoutBlur_AndCancelChangesNothing()
    {
        var doc = new Document(2, 1);
        doc.Layers[0].Pixels[0] = Rig.Red;
        doc.Layers[0].Pixels[1] = Rig.Blue;
        doc.ResizeImage(4, 1, ResizeMethod.Nearest);
        Assert.Equal([Rig.Red, Rig.Red, Rig.Blue, Rig.Blue], doc.Layers[0].Pixels);
        doc.ResizeImage(1, 1, ResizeMethod.Box);
        Assert.Equal(3, doc.Layers[0].Pixels.Distinct().Count() + 2); // one pixel, an averaged color (neither red nor blue)
        Assert.NotEqual(Rig.Red, doc.Layers[0].Pixels[0]);
        Assert.NotEqual(Rig.Blue, doc.Layers[0].Pixels[0]);

        using var rig = new Rig();
        rig.Dialogs.ImageSizeConfigure = null;
        rig.Vm.Canvas.ImageSizeCommand.Execute(null);
        Assert.Equal((32, 32), (rig.Doc.Width, rig.Doc.Height));
    }
}

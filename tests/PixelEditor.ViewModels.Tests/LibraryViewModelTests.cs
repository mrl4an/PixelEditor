// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class MessageViewModelTests
{
    [Fact]
    public void Error_HasSingleDefaultButton_AndShowsDetails()
    {
        var m = MessageViewModel.Error("не вышло", "подробности");
        Assert.Equal(MessageKind.Error, m.Kind);
        Assert.Single(m.Buttons);
        Assert.True(m.Buttons[0].IsDefault && m.Buttons[0].IsCancel);
        Assert.True(m.HasDetails);
        Assert.False(MessageViewModel.Info("t", "m").HasDetails);
    }

    [Fact]
    public void Choose_RemembersKey_AndClosesWithResult()
    {
        var m = MessageViewModel.Confirm("t", "m");
        bool? closed = null;
        m.CloseRequested += ok => closed = ok;
        m.ChooseCommand.Execute(MessageViewModel.Yes);
        Assert.Equal(MessageViewModel.Yes, m.ChosenKey);
        Assert.True(closed);

        var n = MessageViewModel.Confirm("t", "m");
        n.CloseRequested += ok => closed = ok;
        n.ChooseCommand.Execute(MessageViewModel.No);
        Assert.False(closed);
    }

    [Fact]
    public void ClosingWindowWithoutChoice_ActsAsCancel()
    {
        var m = MessageViewModel.SaveChanges();
        m.Dismiss();
        Assert.Equal(MessageViewModel.Cancel, m.ChosenKey);

        var c = MessageViewModel.Confirm("t", "m");
        c.Dismiss();
        Assert.Equal(MessageViewModel.No, c.ChosenKey);
    }

    [Fact]
    public void Destructive_DefaultsToCancel()
    {
        var m = MessageViewModel.ConfirmDestructive("Удалить", "точно?");
        Assert.True(m.Buttons.Single(b => b.IsDefault).IsCancel);
        Assert.True(m.Buttons.Single(b => b.IsPrimary).Key == MessageViewModel.Yes);
        Assert.Equal(MessageKind.Warning, m.Kind);
    }

    [Fact]
    public void FakeDialogs_ConfirmDestructive_FollowsConfirmFn()
    {
        using var rig = new Rig();
        rig.Dialogs.ConfirmFn = _ => false;
        Assert.False(rig.Dialogs.ConfirmDestructive("t", "m"));
        rig.Dialogs.ConfirmFn = _ => true;
        Assert.True(rig.Dialogs.ConfirmDestructive("t", "m"));
    }
}

public class NameDescriptionViewModelTests
{
    [Fact]
    public void EmptyName_IsRejected_AndValidatorIsUsed()
    {
        var vm = new NameDescriptionViewModel("t", "", "", n => n == "занято" ? "Название занято" : null);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;

        vm.OkCommand.Execute(null);
        Assert.Equal("Введите название", vm.Error);
        Assert.Null(closed);

        vm.Name = "занято";
        Assert.Equal("", vm.Error); // the error is reset on editing
        vm.OkCommand.Execute(null);
        Assert.Equal("Название занято", vm.Error);

        vm.Name = "  Моя  ";
        vm.OkCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal("Моя", vm.Name);
    }
}

public class PaletteIntegrationTests
{
    [Fact]
    public void StartsWithSystemPalette_AndStoresItInDocument()
    {
        using var rig = new Rig();
        Assert.Equal("PICO-8", rig.Vm.Colors.ActivePaletteName);
        Assert.Equal(16, rig.Vm.Colors.Palette.Count);
        Assert.Equal(16, rig.Doc.Palette!.Count);
        Assert.Equal("PICO-8", rig.Doc.PaletteName);
        Assert.False(rig.Vm.Colors.IsPaletteModified);
    }

    [Fact]
    public void ApplyPalette_ReplacesColors_NamesIt_AndMarksProjectDirty()
    {
        using var rig = new Rig();
        var gb = BuiltInPalettes.All.First(p => p.Name == "Game Boy");
        rig.Vm.Colors.ApplyPalette(gb);

        Assert.Equal(gb.Colors, rig.Vm.Colors.Palette.ToList());
        Assert.Equal("Game Boy", rig.Vm.Colors.PaletteTitle);
        Assert.Equal(gb.Colors, rig.Doc.Palette);
        Assert.True(rig.S.Dirty);
    }

    [Fact]
    public void EditingPalette_ShowsModifiedMark_AndForgettingNameOnFileLoad()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Pixel.Rgb(1, 2, 3));
        rig.Vm.Colors.AddToPaletteCommand.Execute(null);
        Assert.True(rig.Vm.Colors.IsPaletteModified);
        Assert.Equal("PICO-8 *", rig.Vm.Colors.PaletteTitle);

        var file = Path.Combine(rig.Dir, "p.hex");
        File.WriteAllText(file, "ff0000\n00ff00\n");
        rig.Dialogs.OpenFileFn = () => file;
        rig.Vm.Colors.OpenPaletteCommand.Execute(null);
        Assert.Null(rig.Vm.Colors.ActivePaletteName);
        Assert.Equal("Своя палитра", rig.Vm.Colors.PaletteTitle);
        Assert.Equal(2, rig.Doc.Palette!.Count);
    }

    [Fact]
    public void NewDocument_InheritsCurrentPalette()
    {
        using var rig = new Rig();
        rig.Vm.Colors.ApplyPalette(BuiltInPalettes.All.First(p => p.Name == "Game Boy"));
        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(16, 16);
        rig.Vm.NewCommand.Execute(null);

        Assert.Equal(16, rig.Doc.Width);
        Assert.Equal(4, rig.Vm.Colors.Palette.Count);
        Assert.Equal("Game Boy", rig.Vm.Colors.ActivePaletteName);
        Assert.Equal(4, rig.Doc.Palette!.Count);
    }

    [Fact]
    public void ProjectRoundTrip_RestoresPaletteAndName()
    {
        using var rig = new Rig();
        var pal = BuiltInPalettes.All.First(p => p.Name == "Game Boy");
        rig.Vm.Colors.ApplyPalette(pal);
        var path = Path.Combine(rig.Dir, "p.pixproj");
        ProjectIO.Save(rig.Doc, path);

        rig.Vm.Colors.ApplyPalette(BuiltInPalettes.All[0]);
        rig.S.SetDocument(ProjectIO.Load(path), path);

        Assert.Equal(pal.Colors, rig.Vm.Colors.Palette.ToList());
        Assert.Equal("Game Boy", rig.Vm.Colors.ActivePaletteName);
        Assert.False(rig.Vm.Colors.IsPaletteModified);
    }

    [Fact]
    public void SaveToLibrary_StoresUserPaletteAndNamesTheProjectPalette()
    {
        using var rig = new Rig();
        rig.Dialogs.NameDescriptionConfigure = d => { d.Name = "Мои цвета"; d.Description = "для теста"; };
        rig.Vm.Colors.SavePaletteToLibraryCommand.Execute(null);

        var saved = rig.Settings.Palettes.Find("Мои цвета");
        Assert.NotNull(saved);
        Assert.False(saved!.IsSystem);
        Assert.Equal("для теста", saved.Description);
        Assert.Equal(16, saved.Colors.Count);
        Assert.Equal("Мои цвета", rig.Vm.Colors.ActivePaletteName);
    }

    [Fact]
    public void SaveToLibrary_RejectsExistingName_AndCancelSavesNothing()
    {
        using var rig = new Rig();
        string? error = null;
        rig.Dialogs.NameDescriptionConfigure = d =>
        {
            d.Name = "pico-8"; // taken by a system palette (case-insensitive)
            d.OkCommand.Execute(null);
            error = d.Error;
            d.Name = "Другая";
        };
        rig.Vm.Colors.SavePaletteToLibraryCommand.Execute(null);
        Assert.Contains("уже есть", error);
        Assert.NotNull(rig.Settings.Palettes.Find("Другая"));

        rig.Dialogs.NameDescriptionConfigure = null;
        rig.Vm.Colors.SavePaletteToLibraryCommand.Execute(null);
        Assert.Equal(BuiltInPalettes.All.Count + 1, rig.Settings.Palettes.All().Count);
    }
}

public class PaletteLibraryViewModelTests
{
    private static PaletteLibraryViewModel Open(Rig rig) =>
        new(rig.Vm.Colors, rig.Settings.Palettes, rig.Dialogs, rig.Shell);

    [Fact]
    public void ListsSystemPalettes_AndSystemOnesCannotBeChanged()
    {
        using var rig = new Rig();
        var vm = Open(rig);
        Assert.Equal(BuiltInPalettes.All.Count, vm.Items.Count);
        Assert.True(vm.Selected!.IsSystem);
        Assert.False(vm.CanModify);
        Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.False(vm.UpdateColorsCommand.CanExecute(null));
        Assert.NotEmpty(vm.SystemHint);
        Assert.True(vm.DuplicateCommand.CanExecute(null));
    }

    [Fact]
    public void Use_AppliesSelectedPaletteAndCloses()
    {
        using var rig = new Rig();
        var vm = Open(rig);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        vm.Selected = vm.Items.First(i => i.Name == "Game Boy");
        vm.UseCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal("Game Boy", rig.Vm.Colors.ActivePaletteName);
        Assert.Equal(4, rig.Vm.Colors.Palette.Count);
    }

    [Fact]
    public void Append_AddsOnlyMissingColors()
    {
        using var rig = new Rig();
        var vm = Open(rig);
        vm.Selected = vm.Items.First(i => i.Name == "Game Boy");
        vm.AppendCommand.Execute(null);
        Assert.Equal(20, rig.Vm.Colors.Palette.Count);
        Assert.Contains("4", vm.Message);

        vm.AppendCommand.Execute(null);
        Assert.Equal(20, rig.Vm.Colors.Palette.Count);
        Assert.Contains("уже есть", vm.Message);
    }

    [Fact]
    public void SaveCurrent_EditRename_UpdateColors_AndDelete()
    {
        using var rig = new Rig();
        var vm = Open(rig);

        rig.Dialogs.NameDescriptionConfigure = d => { d.Name = "Десять"; d.Description = "первая"; };
        vm.SaveCurrentCommand.Execute(null);
        Assert.Equal("Десять", vm.Selected!.Name);
        Assert.False(vm.Selected.IsSystem);
        Assert.True(vm.CanModify);

        rig.Dialogs.NameDescriptionConfigure = d => { d.Name = "Десять v2"; d.Description = "вторая"; };
        vm.EditCommand.Execute(null);
        Assert.Equal("Десять v2", vm.Selected!.Name);
        Assert.Equal("вторая", vm.Selected.Description);
        Assert.Null(rig.Settings.Palettes.Find("Десять"));
        Assert.Single(vm.Items, i => !i.IsSystem);

        rig.Vm.Colors.Palette.RemoveAt(0);
        vm.UpdateColorsCommand.Execute(null); // the confirmation is "yes" by default
        Assert.Equal(15, vm.Selected!.Colors.Count);

        rig.Dialogs.ConfirmFn = _ => false;
        vm.DeleteCommand.Execute(null);
        Assert.NotNull(rig.Settings.Palettes.Find("Десять v2")); // a refusal deleted nothing

        rig.Dialogs.ConfirmFn = _ => true;
        vm.DeleteCommand.Execute(null);
        Assert.Null(rig.Settings.Palettes.Find("Десять v2"));
        Assert.Equal(BuiltInPalettes.All.Count, vm.Items.Count);
        Assert.All(vm.Items, i => Assert.True(i.IsSystem));
    }

    [Fact]
    public void Duplicate_MakesEditableCopyWithUniqueName()
    {
        using var rig = new Rig();
        var vm = Open(rig);
        vm.Selected = vm.Items.First(i => i.Name == "Game Boy");
        rig.Dialogs.NameDescriptionConfigure = _ => { };
        vm.DuplicateCommand.Execute(null);
        Assert.Equal("Game Boy (копия)", vm.Selected!.Name);
        Assert.False(vm.Selected.IsSystem);
        Assert.Equal(4, vm.Selected.Colors.Count);

        vm.Selected = vm.Items.First(i => i.Name == "Game Boy");
        vm.DuplicateCommand.Execute(null);
        Assert.Equal("Game Boy (копия) 2", vm.Selected!.Name);
    }

    [Fact]
    public void ImportAndExport_UseFiles()
    {
        using var rig = new Rig();
        var vm = Open(rig);
        var file = Path.Combine(rig.Dir, "mine.hex");
        File.WriteAllText(file, "ff0000\n00ff00\n0000ff\n");
        rig.Dialogs.OpenFileFn = () => file;
        rig.Dialogs.NameDescriptionConfigure = _ => { };
        vm.ImportCommand.Execute(null);
        Assert.Equal("mine", vm.Selected!.Name);
        Assert.Equal(3, vm.Selected.Colors.Count);

        var outFile = Path.Combine(rig.Dir, "out.hex");
        rig.Dialogs.SaveFileFn = () => outFile;
        vm.ExportCommand.Execute(null);
        Assert.Equal(3, PaletteIO.Load(outFile).Count);

        vm.OpenFolderCommand.Execute(null);
        Assert.Equal([rig.Settings.Palettes.UserDirectory], rig.Shell.Opened);
    }
}

public class TemplateLibraryViewModelTests
{
    [Fact]
    public void ParamRow_ParsesDescriptionsAndConvertsValues()
    {
        var p = ParamRow.Parse("keepAspect:bool=true (с width+height вписывает в рамку)");
        Assert.Equal(("keepAspect", "bool", "true"), (p.Name, p.Type, p.Default));
        Assert.Contains("вписывает", p.Hint);
        Assert.Null(p.ToNode());

        p.Value = "false";
        Assert.False(p.ToNode()!.GetValue<bool>());

        var n = ParamRow.Parse("width:int");
        n.Value = "32";
        Assert.Equal(32, n.ToNode()!.GetValue<long>());
        n.Value = "[1,2]";
        Assert.IsType<JsonArray>(n.ToNode());

        var s = ParamRow.Parse("method:string=box");
        s.Value = "mode";
        Assert.Equal("mode", s.ToNode()!.GetValue<string>());
    }

    [Fact]
    public void AllOperationParameters_ParseToNamedRows()
    {
        foreach (var op in Operations.All)
            foreach (var spec in op.Parameters)
                Assert.False(string.IsNullOrWhiteSpace(ParamRow.Parse(spec).Name), $"{op.Name}: {spec}");
        foreach (var c in Checks.All)
            foreach (var spec in c.Parameters)
                Assert.False(string.IsNullOrWhiteSpace(ParamRow.Parse(spec).Name), $"{c.Name}: {spec}");
    }

    [Fact]
    public void Editor_BuildsJson_TestsAndSaves()
    {
        using var rig = new Rig();
        var ed = rig.Vm.Automation.CreateEditor(null, false);
        Assert.Equal("Новый шаблон", ed.Title);

        ed.Name = "Уменьшить вдвое";
        ed.Description = "тест";
        ed.StepToAdd = ed.OperationCatalog.First(o => o.Name == "resize");
        ed.AddStepCommand.Execute(null);
        var step = ed.Steps.Single();
        step.Parameters.First(p => p.Name == "width").Value = "16";
        step.Parameters.First(p => p.Name == "height").Value = "16";
        ed.CheckToAdd = ed.CheckCatalog.First(c => c.Name == "size");
        ed.AddCheckCommand.Execute(null);
        ed.CheckRows[0].Parameters.First(p => p.Name == "maxWidth").Value = "16";

        var json = JsonNode.Parse(ed.Json)!.AsObject();
        Assert.Equal("Уменьшить вдвое", json["name"]!.GetValue<string>());
        Assert.Equal("resize", json["steps"]![0]!["op"]!.GetValue<string>());
        Assert.Equal(16, json["steps"]![0]!["width"]!.GetValue<int>());
        Assert.Equal("size", json["checks"]![0]!["check"]!.GetValue<string>());

        ed.TestCommand.Execute(null);
        Assert.Contains("→ 16×16", ed.TestResult);
        Assert.Contains("Результат подходит", ed.TestResult);
        Assert.Equal(32, rig.Doc.Width); // a trial run does not touch the document

        ed.SaveCommand.Execute(null);
        Assert.Equal("", ed.Error);
        var saved = rig.Settings.Templates.Find("Уменьшить вдвое")!;
        Assert.False(saved.IsSystem);
        Assert.Equal(1, saved.StepCount);
        Assert.Equal(ed.SavedId, saved.Id);
    }

    [Fact]
    public void Editor_ValidatesBeforeSaving()
    {
        using var rig = new Rig();
        var ed = rig.Vm.Automation.CreateEditor(null, false);
        bool? closed = null;
        ed.CloseRequested += ok => closed = ok;

        ed.SaveCommand.Execute(null);
        Assert.Equal("Введите название шаблона", ed.Error);

        ed.Name = "X";
        ed.SaveCommand.Execute(null);
        Assert.Equal("Добавьте хотя бы один шаг", ed.Error);

        ed.StepToAdd = ed.OperationCatalog.First(o => o.Name == "resize");
        ed.AddStepCommand.Execute(null);
        ed.Steps[0].Parameters.First(p => p.Name == "width").Value = "-5"; // the operation will answer with an error itself on run, but the shape is right
        ed.Name = BuiltInTemplates.All[0].Name; // taken by a system one
        ed.SaveCommand.Execute(null);
        Assert.Contains("системн", ed.Error);
        Assert.Null(closed);
    }

    [Fact]
    public void Editor_StepsCanBeReorderedAndRemoved()
    {
        using var rig = new Rig();
        var ed = rig.Vm.Automation.CreateEditor(null, false);
        ed.StepToAdd = ed.OperationCatalog.First(o => o.Name == "trim");
        ed.AddStepCommand.Execute(null);
        ed.StepToAdd = ed.OperationCatalog.First(o => o.Name == "alphaThreshold");
        ed.AddStepCommand.Execute(null);
        Assert.Equal(["trim", "alphaThreshold"], ed.Steps.Select(s => s.Name));

        ed.MoveDownCommand.Execute(ed.Steps[0]);
        Assert.Equal(["alphaThreshold", "trim"], ed.Steps.Select(s => s.Name));
        Assert.Equal([1, 2], ed.Steps.Select(s => s.Number));

        ed.RemoveCommand.Execute(ed.Steps[0]);
        Assert.Equal(["trim"], ed.Steps.Select(s => s.Name));
        ed.MoveUpCommand.Execute(ed.Steps[0]); // already first: nothing happens
        Assert.Single(ed.Steps);
    }

    [Fact]
    public void Editor_OpensSystemTemplateAsCopy_WithAllStepsAndChecks()
    {
        using var rig = new Rig();
        var source = BuiltInTemplates.All[0];
        var ed = rig.Vm.Automation.CreateEditor(source, asCopy: true);
        Assert.Equal(source.StepCount, ed.Steps.Count);
        Assert.Equal(source.CheckCount, ed.CheckRows.Count);
        Assert.Equal(source.Name + " (копия)", ed.Name);

        ed.SaveCommand.Execute(null);
        Assert.Equal("", ed.Error);
        var copy = rig.Settings.Templates.Find(ed.Name)!;
        Assert.False(copy.IsSystem);
        Assert.Equal(source.StepCount, copy.StepCount);
        Assert.True(BuiltInTemplates.All[0].IsSystem); // the original is in place
    }

    [Fact]
    public void Editor_KeepsUnknownParametersAndRequiredFlag()
    {
        using var rig = new Rig();
        var lib = rig.Settings.Templates;
        var info = lib.Save("Свой", "", """{"steps":[{"op":"trim","extra":5}],"checks":[{"check":"connected","required":false}]}""");
        var ed = rig.Vm.Automation.CreateEditor(info, asCopy: false);
        Assert.Contains(ed.Steps[0].Parameters, p => p.Name == "extra" && p.Value == "5");
        Assert.False(ed.CheckRows[0].Required);

        ed.Name = "Свой 2";
        ed.SaveCommand.Execute(null);
        Assert.Null(lib.Find("Свой")); // editing your own template renames it, not duplicates it
        var json = JsonNode.Parse(lib.Find("Свой 2")!.Json)!;
        Assert.Equal(5, json["steps"]![0]!["extra"]!.GetValue<int>());
        Assert.False(json["checks"]![0]!["required"]!.GetValue<bool>());
    }

    [Fact]
    public void Manager_ListsSystemFirst_AndProtectsThem()
    {
        using var rig = new Rig();
        var vm = new TemplateLibraryViewModel(rig.S, rig.Vm.Automation);
        Assert.Equal(BuiltInTemplates.All.Count, vm.Items.Count);
        Assert.True(vm.Selected!.IsSystem);
        Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.True(vm.DuplicateCommand.CanExecute(null));
        Assert.NotEmpty(vm.Outline);
        Assert.NotEmpty(vm.SystemHint);
    }

    [Fact]
    public void Manager_CreateDuplicateDelete_UpdateAutomationMenu()
    {
        using var rig = new Rig();
        var vm = new TemplateLibraryViewModel(rig.S, rig.Vm.Automation);

        rig.Dialogs.EditorConfigure = ed =>
        {
            ed.Name = "Мой шаблон";
            ed.StepToAdd = ed.OperationCatalog.First(o => o.Name == "trim");
            ed.AddStepCommand.Execute(null);
        };
        vm.NewCommand.Execute(null);
        Assert.Equal("Мой шаблон", vm.Selected!.Name);
        Assert.Single(rig.Vm.Automation.UserTemplateMenu);
        Assert.True(vm.CanModify);

        rig.Dialogs.EditorConfigure = _ => { };
        vm.DuplicateCommand.Execute(null);
        Assert.Equal("Мой шаблон (копия)", vm.Selected!.Name);
        Assert.Equal(2, rig.Vm.Automation.UserTemplateMenu.Count);

        rig.Dialogs.ConfirmFn = _ => false;
        vm.DeleteCommand.Execute(null);
        Assert.Equal(2, rig.Vm.Automation.UserTemplateMenu.Count);

        rig.Dialogs.ConfirmFn = _ => true;
        vm.DeleteCommand.Execute(null);
        Assert.Single(rig.Vm.Automation.UserTemplateMenu);
        Assert.Single(vm.Items, i => !i.IsSystem);
    }

    [Fact]
    public void Manager_CancelledEditor_ChangesNothing()
    {
        using var rig = new Rig();
        var vm = new TemplateLibraryViewModel(rig.S, rig.Vm.Automation);
        rig.Dialogs.EditorConfigure = null;
        vm.NewCommand.Execute(null);
        Assert.Equal(BuiltInTemplates.All.Count, vm.Items.Count);
    }

    [Fact]
    public void Manager_ImportExportAndFolder()
    {
        using var rig = new Rig();
        var vm = new TemplateLibraryViewModel(rig.S, rig.Vm.Automation);
        var file = Path.Combine(rig.Dir, "ext.json");
        File.WriteAllText(file, """{"name":"Внешний","description":"из файла","steps":[{"op":"trim"}]}""");
        rig.Dialogs.OpenFileFn = () => file;
        vm.ImportCommand.Execute(null);
        Assert.Equal("Внешний", vm.Selected!.Name);
        Assert.Equal("из файла", vm.Selected.Description);

        var broken = Path.Combine(rig.Dir, "bad.json");
        File.WriteAllText(broken, """{"steps":[{"op":"нет-такой"}]}""");
        rig.Dialogs.OpenFileFn = () => broken;
        vm.ImportCommand.Execute(null);
        Assert.Single(rig.Dialogs.Errors);

        var outFile = Path.Combine(rig.Dir, "out.json");
        rig.Dialogs.SaveFileFn = () => outFile;
        vm.ExportCommand.Execute(null);
        Assert.Equal("Внешний", Pipeline.Load(outFile).Name);

        vm.OpenFolderCommand.Execute(null);
        Assert.Equal([rig.Settings.Templates.UserDirectory], rig.Shell.Opened);
    }

    [Fact]
    public void Manager_Run_AppliesSystemTemplateToActiveLayer()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Stroke(5, 5, 5, 5);
        Assert.Equal(Rig.Red, rig.Layer.Get(5, 5));

        var vm = new TemplateLibraryViewModel(rig.S, rig.Vm.Automation);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        vm.Selected = vm.Items.First(i => i.Id == "system:clean-sprite");
        vm.RunCommand.Execute(null);

        Assert.True(closed);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(5, 5))); // a single pixel removed by the template
        Assert.True(rig.Doc.History.CanUndo);
    }
}

public class UsabilityFixTests
{
    [Fact]
    public void LeavingSelectTool_CommitsPasteAndClearsSelection()
    {
        using var rig = new Rig();
        var img = new PixelImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Rig.Red;
        rig.Clipboard.SetExternal(img);
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.Equal(Tool.Pencil, rig.Vm.Canvas.Tool);
        Assert.True(rig.Vm.Canvas.HasSelection);

        rig.Vm.Canvas.Tool = Tool.Select;   // the paste stays draggable under any tool; moving to a selection tool and away commits it
        rig.Vm.Canvas.Tool = Tool.Pencil;

        Assert.False(rig.Vm.Canvas.HasSelection);
        Assert.False(rig.Vm.Canvas.HasFloat);
        Assert.Equal(Rig.Red, rig.Layer.Get(0, 0)); // the paste is committed in the layer
    }

    [Fact]
    public void TemplateFromMenu_ShowsPreviewFirst_AndCancelChangesNothing()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Stroke(5, 5, 5, 5);
        var item = TemplateItem.From(BuiltInTemplates.All.First(t => t.Id == "system:clean-sprite"));

        rig.Dialogs.PreviewAccept = false;
        rig.Vm.Automation.RunTemplate(item);
        var preview = Assert.Single(rig.Dialogs.PreviewsShown);
        Assert.True(preview.SameSize);
        Assert.Equal(1, preview.ChangedPixels);
        Assert.Equal(Rig.Red, rig.Layer.Get(5, 5)); // cancel: the layer is intact

        rig.Dialogs.PreviewAccept = true;
        rig.Vm.Automation.RunTemplate(item);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(5, 5)));
    }

    [Fact]
    public void PreviewOfSizeChangingTemplate_ExplainsNewDocument_AndOpensIt()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Stroke(5, 5, 5, 5);
        var item = TemplateItem.From(BuiltInTemplates.All.First(t => t.Id == "system:prepare-for-game"));
        rig.Vm.Automation.RunTemplate(item);

        var preview = Assert.Single(rig.Dialogs.PreviewsShown);
        Assert.False(preview.SameSize);
        Assert.Equal("Открыть в новом документе", preview.ApplyText);
        Assert.Contains("НОВОМ документе", preview.Explanation);
        Assert.Equal(16, rig.Doc.Width); // trimmed to the sprite and padded to a multiple of 16; accepted: the result opened as a new document without extra questions
        Assert.Empty(rig.Dialogs.Confirms.Where(c => c.Contains("Размер изменился")));
    }

    [Fact]
    public void OperationThatChangesNothing_SaysSo()
    {
        using var rig = new Rig();
        rig.Vm.Automation.ApplyOperation("removeIslands", """{"maxSize":1}""");
        Assert.Single(rig.Dialogs.Infos);
        Assert.Contains("ничего не изменила", rig.Dialogs.Infos[0]);
    }
}

public class MoveAndShapeToolTests
{
    private static void Fill(Layer l, int x0, int y0, int w, int h, uint c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) l.Pixels[y * l.Width + x] = c;
    }

    [Fact]
    public void MoveTool_DragsOnlyTheObjectUnderCursor_AndUndoRestores()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 2, 2, 3, 3, Rig.Red);
        Fill(rig.Layer, 10, 2, 3, 3, Rig.Blue);
        rig.Vm.Canvas.Tool = Tool.Move;

        rig.Stroke(3, 3, 8, 13); // drag the red subject by (+5, +10)

        Assert.True(Pixel.IsEmpty(rig.Layer.Get(3, 3)));
        Assert.Equal(Rig.Red, rig.Layer.Get(8, 13));
        Assert.Equal(Rig.Blue, rig.Layer.Get(11, 3)); // the neighboring subject is in place
        Assert.False(rig.Vm.Canvas.HasFloat);
        Assert.True(rig.Vm.Canvas.HasSelection);        // the subject stays selected (handles, dragging again)
        rig.Vm.Canvas.PointerDown(28, 28, false, false); rig.Vm.Canvas.PointerUp();   // a click on an empty spot clears it
        Assert.False(rig.Vm.Canvas.HasSelection);

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(Rig.Red, rig.Layer.Get(3, 3));
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(8, 13)));
    }

    [Fact]
    public void MoveTool_LeavesTouchingBackgroundObjectsAlone_AndSaysWhenEmpty()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 2, 2, 2, 2, Rig.Red);
        rig.Layer.Pixels[4 * rig.Layer.Width + 4] = Rig.Blue; // touches diagonally: counts as the same subject
        rig.Vm.Canvas.Tool = Tool.Move;
        rig.Stroke(20, 20, 25, 25); // an empty place
        Assert.Contains("пусто", rig.S.StatusText);
        Assert.Equal(Rig.Red, rig.Layer.Get(2, 2));

        rig.Stroke(2, 2, 4, 2);
        Assert.Equal(Rig.Blue, rig.Layer.Get(6, 4)); // the diagonal pixel moved along
        Assert.Equal(Rig.Red, rig.Layer.Get(4, 2));
    }

    [Fact]
    public void ShapeThickness_ControlsOutlineWidth_AndFillIsOptional()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Vm.Canvas.Tool = Tool.Rect;
        Assert.True(rig.Vm.Canvas.IsShapeTool);
        rig.Vm.Canvas.ShapeThickness = 1;
        rig.Stroke(2, 2, 12, 12);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(7, 7)));
        Assert.Equal(Rig.Red, rig.Layer.Get(2, 7));
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(3, 7))); // an outline of 1 px

        rig.Vm.UndoCommand.Execute(null);
        rig.Vm.Canvas.ShapeThickness = 3;
        rig.Stroke(2, 2, 12, 12);
        Assert.Equal(Rig.Red, rig.Layer.Get(3, 7)); // a thicker outline
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(7, 7)));

        rig.Vm.UndoCommand.Execute(null);
        rig.Vm.Canvas.FilledShapes = true;
        rig.Stroke(2, 2, 12, 12);
        Assert.Equal(Rig.Red, rig.Layer.Get(7, 7)); // filled
    }

    [Fact]
    public void ToolOptionFlags_FollowTheSelectedTool()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        Assert.True(c.IsBrushTool && !c.IsShapeTool);
        c.Tool = Tool.Ellipse;
        Assert.True(c.IsShapeTool && !c.IsBrushTool);
        c.Tool = Tool.Move;
        Assert.True(c.IsMoveTool);
        c.Tool = Tool.Picker;
        Assert.True(c.IsPickerTool);
    }
}

public class PaletteCreateAndGridTests
{
    [Fact]
    public void NewPalette_StartsEmpty_ThenColorsCanBeAddedAndSaved()
    {
        using var rig = new Rig();
        rig.Dialogs.ConfirmFn = _ => true;
        rig.Vm.Colors.NewPaletteCommand.Execute(null);
        Assert.Empty(rig.Vm.Colors.Palette);
        Assert.Null(rig.Vm.Colors.ActivePaletteName);
        Assert.False(rig.Vm.Colors.SavePaletteToLibraryCommand.CanExecute(null));

        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Vm.Colors.AddToPaletteCommand.Execute(null);
        Assert.True(rig.Vm.Colors.SavePaletteToLibraryCommand.CanExecute(null));
        rig.Dialogs.NameDescriptionConfigure = d => d.Name = "Красная";
        rig.Vm.Colors.SavePaletteToLibraryCommand.Execute(null);
        Assert.Equal([Rig.Red], rig.Settings.Palettes.Find("Красная")!.Colors);
    }

    [Fact]
    public void NewPalette_AsksOnlyWhenCurrentColorsWouldBeLost()
    {
        using var rig = new Rig();
        rig.Vm.Colors.NewPaletteCommand.Execute(null); // PICO-8 from the library without edits: nothing to lose
        Assert.Empty(rig.Dialogs.Confirms);

        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Vm.Colors.AddToPaletteCommand.Execute(null); // your own palette with colors
        rig.Dialogs.ConfirmFn = _ => false;
        rig.Vm.Colors.NewPaletteCommand.Execute(null);
        Assert.Single(rig.Dialogs.Confirms);
        Assert.Single(rig.Vm.Colors.Palette); // refusal: the palette is intact
    }

    [Fact]
    public void LibraryWindow_NewButton_EmptiesPaletteAndCloses()
    {
        using var rig = new Rig();
        var vm = new PaletteLibraryViewModel(rig.Vm.Colors, rig.Settings.Palettes, rig.Dialogs, rig.Shell);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        vm.NewCommand.Execute(null);
        Assert.True(closed);
        Assert.Empty(rig.Vm.Colors.Palette);
    }

    [Fact]
    public void SelectionGrid_IsOffByDefault_AndSelectToolFlagWorks()
    {
        using var rig = new Rig();
        Assert.False(rig.Vm.Canvas.ShowSelectionGrid);
        rig.Vm.Canvas.Tool = Tool.Select;
        Assert.True(rig.Vm.Canvas.IsSelectTool);
        rig.Vm.Canvas.ShowSelectionGrid = true;
        Assert.True(rig.Vm.Canvas.ShowSelectionGrid);
    }
}

public class SelectionToolsTests
{
    private static void Fill(Layer l, int x0, int y0, int w, int h, uint c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) l.Pixels[y * l.Width + x] = c;
    }

    [Fact]
    public void Wand_SelectsRegion_RightClickAdds_AndDeleteClearsOnlyShape()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 2, 2, 3, 3, Rig.Red);
        Fill(rig.Layer, 10, 2, 3, 3, Rig.Red);
        Fill(rig.Layer, 3, 3, 1, 1, Rig.Blue);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Wand;
        c.PointerDown(2, 2, right: false, alt: false);
        c.PointerUp();
        Assert.True(c.HasSelection);
        Assert.NotNull(c.SelectionShape);
        Assert.Equal(8, c.SelectionShape!.Count); // a red frame without the blue center

        c.Tool = Tool.Wand; // after the first selection the tool changed to "Move"
        c.PointerDown(10, 2, right: true, alt: false);
        c.PointerUp();
        Assert.Equal(17, c.SelectionShape!.Count);

        c.DeleteCommand.Execute(null);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(2, 2)));
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(11, 3)));
        Assert.Equal(Rig.Blue, rig.Layer.Get(3, 3)); // outside the shape
        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(Rig.Red, rig.Layer.Get(2, 2));
    }

    [Fact]
    public void Wand_Selection_CanBeDraggedWithSelectTool_AsShape()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 2, 2, 3, 3, Rig.Red);
        Fill(rig.Layer, 3, 3, 1, 1, Rig.Blue);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Wand;
        c.PointerDown(2, 2, false, false);
        c.PointerUp();
        c.Tool = Tool.Select;
        Assert.True(c.HasSelection); // between the selection tools it is kept

        rig.Stroke(2, 2, 12, 12); // drag the red frame by (+10, +10)
        Assert.Equal(Rig.Red, rig.Layer.Get(12, 12));
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(2, 2)));
        Assert.Equal(Rig.Blue, rig.Layer.Get(3, 3)); // blue was not in the shape and stayed
    }

    [Fact]
    public void Lasso_SelectsInsideOfDrawnLoop_AndSwitchingToPencilClearsIt()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 0, 0, 32, 32, Rig.Red);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Lasso;
        Assert.True(c.PointerDown(4, 4, false, false));
        c.PointerMove(12, 4);
        c.PointerMove(12, 12);
        c.PointerMove(4, 12);
        c.PointerUp();
        Assert.NotNull(c.SelectionShape);
        Assert.True(c.SelectionShape!.Contains(8, 8));
        Assert.False(c.SelectionShape.Contains(20, 20));

        c.Tool = Tool.Pencil;
        Assert.False(c.HasSelection);
        Assert.Null(c.SelectionShape);
    }

    [Fact]
    public void SelectionTransforms_RotateFlipScale()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 4, 4, 4, 2, Rig.Red); // 4 wide, 2 high
        rig.Layer.Pixels[4 * rig.Layer.Width + 4] = Rig.Blue; // the top left corner is blue
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Select;
        rig.Stroke(4, 4, 7, 5); // a frame along the rectangle
        Assert.Equal(4, c.SelectionBounds!.Value.Width);

        c.RotateRightCommand.Execute(null);
        Assert.Equal((2, 4), (c.SelectionBounds!.Value.Width, c.SelectionBounds!.Value.Height));

        c.FlipHorizontalCommand.Execute(null);
        c.ScaleUpCommand.Execute(null);
        Assert.Equal((4, 8), (c.SelectionBounds!.Value.Width, c.SelectionBounds!.Value.Height));
        c.ScaleDownCommand.Execute(null);
        Assert.Equal((2, 4), (c.SelectionBounds!.Value.Width, c.SelectionBounds!.Value.Height));

        c.DeselectCommand.Execute(null);
        Assert.Equal(Rig.Red, rig.Layer.Pixels.First(p => p == Rig.Red));
        Assert.Equal(1, rig.Layer.Pixels.Count(p => p == Rig.Blue)); // the corner was not lost
    }

    [Fact]
    public void RotateImage_MapsCornersCorrectly()
    {
        var img = new PixelImage(3, 2);
        img.Pixels[0] = Rig.Red; // (0,0)
        var cw = CanvasViewModel.RotateCw(img);
        Assert.Equal((2, 3), (cw.Width, cw.Height));
        Assert.Equal(Rig.Red, cw.Pixels[0 * 2 + 1]); // (x=1,y=0)
        var ccw = CanvasViewModel.RotateCcw(img);
        Assert.Equal(Rig.Red, ccw.Pixels[2 * 2 + 0]); // (x=0,y=2)
    }

    [Fact]
    public void Gradient_Tool_FillsInsideSelectionOnly()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Vm.Colors.SetSecondary(Rig.Blue);
        c.Tool = Tool.Select;
        rig.Stroke(2, 2, 9, 9);
        c.Tool = Tool.Gradient; // the selection stays
        Assert.True(c.HasSelection);
        c.GradientKind = GradientMode.Smooth;
        rig.Stroke(2, 2, 9, 2);
        Assert.Equal(Rig.Red, rig.Layer.Get(2, 5));
        Assert.Equal(Rig.Blue, rig.Layer.Get(9, 5));
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(1, 5))); // outside the selection untouched
        rig.Vm.UndoCommand.Execute(null);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(2, 5)));
    }

    [Fact]
    public void Shade_Tool_LightensWithLeftAndDarkensWithRight_OncePerStroke()
    {
        using var rig = new Rig();
        var mid = Pixel.Rgb(100, 60, 60);
        Fill(rig.Layer, 0, 0, 8, 1, mid);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Shade;
        c.ShadeAmount = 20;
        rig.Stroke(0, 0, 7, 0);
        var lighter = rig.Layer.Get(3, 0);
        Assert.True(Pixel.R(lighter) > 100);
        rig.Stroke(0, 0, 7, 0, right: true);
        Assert.True(Pixel.R(rig.Layer.Get(3, 0)) < Pixel.R(lighter)); // +20% then -20% of the new one
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(0, 5))); // transparent is not painted
    }

    [Fact]
    public void Text_Tool_PlacesFloatingText_ThatCanBeCommitted()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Red);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Text;
        c.PointerDown(3, 4, false, false);
        c.PointerUp();
        Assert.True(c.IsTextEditing);
        c.TextInsert("Аб");
        Assert.Equal(11, c.TextFrame!.Value.Width);
        Assert.Empty(rig.Layer.Pixels.Where(p => !Pixel.IsEmpty(p)));   // the preview is not in the layer yet
        Assert.NotNull(rig.S.Preview);

        c.Tool = Tool.Pencil; // leaving the text commits it in the layer
        Assert.False(c.IsTextEditing);
        Assert.True(rig.Layer.Pixels.Count(p => p == Rig.Red) > 10);
        Assert.True(rig.Doc.History.CanUndo);

        c.Tool = Tool.Text;
        c.PointerDown(3, 20, false, false);
        Assert.True(c.IsTextEditing);
        c.TextCancel(); // cancelling places nothing
        Assert.False(c.IsTextEditing);
    }

    [Fact]
    public void Text_Session_Editing_Undo_Commit_AreOneStep_AndEmptyMakesNoLayer()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.NewLayerForObjects = true;
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Text;
        c.PointerDown(1, 1, false, false);
        c.TextInsert("ab");
        c.TextInsert("\n");
        c.TextInsert("cd");
        Assert.Equal("ab\ncd", c.TextEdit!.Text);
        c.TextMoveCaret(TextMove.Up);
        c.TextMoveCaret(TextMove.End);
        c.TextBackspace();
        Assert.Equal("a\ncd", c.TextEdit.Text);
        Assert.True(rig.Vm.UndoCommand.CanExecute(null));
        rig.Vm.UndoCommand.Execute(null);                 // Ctrl+Z undoes typing, not the drawing
        Assert.Equal("ab\ncd", c.TextEdit!.Text);
        c.TextCommit();
        Assert.Equal(2, rig.Doc.Layers.Count);
        rig.Vm.UndoCommand.Execute(null);                 // layer and text are one step
        Assert.Single(rig.Doc.Layers);

        c.PointerDown(1, 1, false, false);
        c.TextInsert("x");
        rig.Vm.UndoCommand.Execute(null);                 // undoes the typing
        Assert.True(c.IsTextEditing);
        rig.Vm.UndoCommand.Execute(null);                 // nothing left to undo: cancels the session
        Assert.False(c.IsTextEditing);
        c.PointerDown(1, 1, false, false);
        c.TextInsert("  ");
        c.TextCommit();
        Assert.Single(rig.Doc.Layers);                    // blank text creates nothing
    }

    [Fact]
    public void Text_Font_AndScale_AreRemembered()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.TextFont = rig.Vm.Canvas.TextFonts.First(f => f.Id == "wide");
        rig.Vm.Canvas.TextScale = 3;
        var second = new MainViewModel(rig.Services, AppSettings.Load(Path.Combine(rig.Dir, "settings")));
        Assert.Equal("wide", second.Canvas.TextFont.Id);
        Assert.Equal(3, second.Canvas.TextScale);
    }
}

public class RoundThreeTests
{
    private static void Fill(Layer l, int x0, int y0, int w, int h, uint c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) l.Pixels[y * l.Width + x] = c;
    }

    [Fact]
    public void Gradient_WithoutSelection_AffectsOnlyTheObjectUnderStart_NotTheWholeField()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 2, 2, 6, 4, Rig.Red);
        Fill(rig.Layer, 20, 20, 4, 4, Rig.Red);
        rig.Vm.Colors.SetPrimary(Rig.Blue);
        rig.Vm.Colors.SetSecondary(Pixel.Rgb(255, 255, 255));
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Gradient;
        c.GradientKind = GradientMode.Smooth;
        rig.Stroke(2, 3, 7, 3);

        Assert.Equal(Rig.Blue, rig.Layer.Get(2, 3));
        Assert.Equal(Pixel.Rgb(255, 255, 255), rig.Layer.Get(7, 3));
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(0, 0)));  // an empty field is not filled
        Assert.Equal(Rig.Red, rig.Layer.Get(20, 20));      // the other subject is not touched
    }

    [Fact]
    public void Gradient_StartedOnEmpty_DoesNothingAndExplains()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.Tool = Tool.Gradient;
        rig.Stroke(5, 5, 9, 9);
        Assert.All(rig.Layer.Pixels, p => Assert.True(Pixel.IsEmpty(p)));
        Assert.Contains("предмет", rig.S.StatusText);
        Assert.False(rig.Doc.History.CanUndo);
    }

    [Fact]
    public void PaletteWindow_AddNewPalette_ThenAddAndRemoveColors()
    {
        using var rig = new Rig();
        var vm = new PaletteLibraryViewModel(rig.Vm.Colors, rig.Settings.Palettes, rig.Dialogs, rig.Shell);
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Dialogs.NameDescriptionConfigure = d => { d.Name = "Моя"; d.Description = "тест"; };
        vm.AddNewCommand.Execute(null);
        Assert.Equal("Моя", vm.Selected!.Name);
        Assert.Equal([Rig.Red], vm.Selected.Colors);

        rig.Vm.Colors.SetPrimary(Rig.Blue);
        vm.AddColorCommand.Execute(null);
        Assert.Equal([Rig.Red, Rig.Blue], vm.Selected!.Colors);
        vm.AddColorCommand.Execute(null); // the same color a second time
        Assert.Equal(2, vm.Selected!.Colors.Count);

        vm.SelectedColor = Rig.Red;
        Assert.True(vm.RemoveColorCommand.CanExecute(null));
        vm.RemoveColorCommand.Execute(null);
        Assert.Equal([Rig.Blue], vm.Selected!.Colors);
        vm.SelectedColor = Rig.Blue;
        Assert.False(vm.RemoveColorCommand.CanExecute(null)); // the last color cannot be removed
    }

    [Fact]
    public void PaletteWindow_SystemPalette_CannotGetOrLoseColors()
    {
        using var rig = new Rig();
        var vm = new PaletteLibraryViewModel(rig.Vm.Colors, rig.Settings.Palettes, rig.Dialogs, rig.Shell);
        Assert.True(vm.Selected!.IsSystem);
        Assert.False(vm.AddColorCommand.CanExecute(null));
        vm.SelectedColor = vm.Selected.Colors[0];
        Assert.False(vm.RemoveColorCommand.CanExecute(null));
    }

    [Fact]
    public void PreviewWindow_FollowsEdits_ScalesAndStopsAfterClose()
    {
        using var rig = new Rig();
        var vm = rig.Vm;
        vm.OpenPreviewCommand.Execute(null);
        Assert.Single(rig.Dialogs.Previews);
        vm.OpenPreviewCommand.Execute(null); // already open: there is no second window
        Assert.Single(rig.Dialogs.Previews);

        var p = rig.Dialogs.Previews[0];
        Assert.Equal(32, p.DisplayWidth);
        p.Scale = 4;
        Assert.Equal(128, p.DisplayWidth);
        Assert.Contains("4×", p.Info);

        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Stroke(1, 1, 1, 1);
        Assert.Equal(Rig.Red, p.Image.Pixels[1 * 32 + 1]);

        p.NotifyClosed();
        Assert.False(p.IsOpen);
        rig.Stroke(5, 5, 5, 5);
        Assert.True(Pixel.IsEmpty(p.Image.Pixels[5 * 32 + 5])); // a closed window is no longer updated
        vm.OpenPreviewCommand.Execute(null); // can be opened again
        Assert.Equal(2, rig.Dialogs.Previews.Count);
    }

    [Fact]
    public void SelectionGridMode_DefaultsToCanvasCoordinates()
    {
        using var rig = new Rig();
        Assert.False(rig.Vm.Canvas.SelectionGridLocal);
        rig.Vm.Canvas.SelectionGridLocal = true;
        Assert.True(rig.Vm.Canvas.SelectionGridLocal);
    }
}

public class ObjectLayerTests
{
    [Fact]
    public void Shape_GoesToNewLayer_AndOneUndoRemovesLayerAndDrawing()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.NewLayerForObjects = true;
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Vm.Canvas.Tool = Tool.Rect;
        int before = rig.Doc.Layers.Count;

        rig.Stroke(2, 2, 6, 6);
        Assert.Equal(before + 1, rig.Doc.Layers.Count);
        Assert.StartsWith("Фигура", rig.Layer.Name);
        Assert.Equal(Rig.Red, rig.Layer.Get(2, 2));
        Assert.True(Pixel.IsEmpty(rig.Doc.Layers[0].Get(2, 2))); // the bottom layer is clean

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(before, rig.Doc.Layers.Count);
        rig.Vm.RedoCommand.Execute(null);
        Assert.Equal(before + 1, rig.Doc.Layers.Count);
        Assert.Equal(Rig.Red, rig.Doc.Layers[^1].Get(2, 2));
    }

    [Fact]
    public void EachShape_GetsItsOwnLayer()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.NewLayerForObjects = true;
        rig.Vm.Canvas.Tool = Tool.Ellipse;
        rig.Stroke(1, 1, 8, 8);
        rig.Stroke(12, 12, 20, 20);
        Assert.Equal(3, rig.Doc.Layers.Count);
        Assert.Equal(1, rig.Doc.Layers[1].Pixels.Count(p => !Pixel.IsEmpty(p)) > 0 ? 1 : 0);
        Assert.Equal(1, rig.Doc.Layers[2].Pixels.Count(p => !Pixel.IsEmpty(p)) > 0 ? 1 : 0);
        Assert.True(Pixel.IsEmpty(rig.Doc.Layers[1].Get(15, 15)));
    }

    [Fact]
    public void PencilDoesNotCreateLayers_AndOptionCanBeSwitchedOff()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.NewLayerForObjects = true;
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Stroke(1, 1, 5, 1);
        Assert.Single(rig.Doc.Layers);

        rig.Vm.Canvas.NewLayerForObjects = false;
        rig.Vm.Canvas.Tool = Tool.Rect;
        rig.Stroke(2, 2, 6, 6);
        Assert.Single(rig.Doc.Layers);
    }

    [Fact]
    public void Paste_GoesToNewLayer_CommitsWithSingleUndo_AndCancelRemovesIt()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.NewLayerForObjects = true;
        var img = new PixelImage(3, 3);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Rig.Blue;
        rig.Clipboard.SetExternal(img);

        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.Equal(2, rig.Doc.Layers.Count);
        Assert.StartsWith("Вставка", rig.Layer.Name);
        rig.Vm.Canvas.DeselectCommand.Execute(null); // commit
        Assert.Equal(Rig.Blue, rig.Layer.Get(0, 0));
        Assert.True(Pixel.IsEmpty(rig.Doc.Layers[0].Get(0, 0)));

        rig.Vm.UndoCommand.Execute(null);
        Assert.Single(rig.Doc.Layers);

        // cancelling before the commit removes the layer
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.Equal(2, rig.Doc.Layers.Count);
        rig.Vm.UndoCommand.Execute(null);
        Assert.Single(rig.Doc.Layers);
    }

    [Fact]
    public void Text_GoesToNewLayer()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.NewLayerForObjects = true;
        rig.Vm.Canvas.Tool = Tool.Text;
        rig.Vm.Canvas.PointerDown(2, 2, false, false);
        rig.Vm.Canvas.PointerUp();
        rig.Vm.Canvas.TextInsert("Я");
        rig.Vm.Canvas.TextCommit();
        Assert.Equal(2, rig.Doc.Layers.Count);
        Assert.StartsWith("Текст", rig.Layer.Name);
        rig.Vm.Canvas.Tool = Tool.Pencil;
        Assert.True(rig.Layer.Pixels.Any(p => !Pixel.IsEmpty(p)));
    }
}

public class ActivePaletteTests
{
    [Fact]
    public void NewPalette_BecomesProjectPalette_AndAddingColorsSavesToLibrary()
    {
        using var rig = new Rig();
        var vm = new PaletteLibraryViewModel(rig.Vm.Colors, rig.Settings.Palettes, rig.Dialogs, rig.Shell);
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Dialogs.NameDescriptionConfigure = d => { d.Name = "Живая"; d.Description = "описание"; };
        vm.AddNewCommand.Execute(null);

        Assert.Equal("Живая", rig.Vm.Colors.ActivePaletteName);   // chosen as the project palette
        Assert.Equal([Rig.Red], rig.Vm.Colors.Palette.ToList());

        rig.Vm.Colors.SetPrimary(Rig.Blue);
        rig.Vm.Colors.AddToPaletteCommand.Execute(null);           // the "+ To palette" button under the color
        var saved = rig.Settings.Palettes.Find("Живая")!;
        Assert.Equal([Rig.Red, Rig.Blue], saved.Colors);          // it was written to the library by itself
        Assert.Equal("описание", saved.Description);
        Assert.False(rig.Vm.Colors.IsPaletteModified);
    }

    [Fact]
    public void SystemPalette_IsNotWrittenBack_WhenEdited()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Pixel.Rgb(1, 2, 3));
        rig.Vm.Colors.AddToPaletteCommand.Execute(null);
        Assert.Equal(BuiltInPalettes.All.Count, rig.Settings.Palettes.All().Count); // there are no new files
        Assert.True(rig.Vm.Colors.IsPaletteModified);
    }

    [Fact]
    public void ColorAddedInLibraryWindow_AppearsInProjectPalette()
    {
        using var rig = new Rig();
        var vm = new PaletteLibraryViewModel(rig.Vm.Colors, rig.Settings.Palettes, rig.Dialogs, rig.Shell);
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Dialogs.NameDescriptionConfigure = d => d.Name = "П";
        vm.AddNewCommand.Execute(null);
        rig.Vm.Colors.SetPrimary(Rig.Blue);
        vm.AddColorCommand.Execute(null);
        Assert.Equal(2, rig.Vm.Colors.Palette.Count);
    }
}

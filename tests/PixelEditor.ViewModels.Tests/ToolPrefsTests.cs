// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class ToolPrefsTests
{
    [Fact]
    public void ToolAndOptions_AreRestoredOnNextLaunch()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Ellipse;
        c.BrushSize = 5;
        c.FilledShapes = true;
        c.FillTolerance = 40;
        c.Tool = Tool.Fill;

        // "a new launch": the same settings from disk, a new model
        var settings = AppSettings.Load(Path.Combine(rig.Dir, "settings"));
        var second = new MainViewModel(rig.Services, settings);
        var c2 = second.Canvas;
        Assert.Equal(Tool.Fill, c2.Tool);
        Assert.Equal(5, c2.BrushSize);
        Assert.True(c2.FilledShapes);
        Assert.Equal(40, c2.FillTolerance);
        Assert.Equal(Tool.Ellipse, c2.LastShapeTool);       // the "Shapes" group remembers the variant
    }

    [Fact]
    public void WandOptions_AreRemembered_IncludingFollowSmoothTransitions()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        c.WandTolerance = 17;
        c.WandContiguous = false;
        c.WandFollow = true;
        var second = new MainViewModel(rig.Services, AppSettings.Load(Path.Combine(rig.Dir, "settings")));
        Assert.Equal(17, second.Canvas.WandTolerance);
        Assert.False(second.Canvas.WandContiguous);
        Assert.True(second.Canvas.WandFollow);
    }

    [Fact]
    public void MoveTool_IsNotRestored_AndBrokenToolNameFallsBackToPencil()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.Tool = Tool.Eraser;
        rig.Vm.Canvas.Tool = Tool.Move;
        var settings = AppSettings.Load(Path.Combine(rig.Dir, "settings"));
        Assert.Equal(Tool.Eraser, new MainViewModel(rig.Services, settings).Canvas.Tool);

        settings.ToolName = "Нет такого";
        Assert.Equal(Tool.Pencil, new MainViewModel(rig.Services, settings).Canvas.Tool);
    }

    [Fact]
    public void NewDocumentSize_IsRemembered_AndDefaultIs64()
    {
        Assert.Equal((64, 64), (new AppSettings().DefaultWidth, new AppSettings().DefaultHeight));

        using var rig = new Rig();
        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(48, 40);
        rig.Vm.NewCommand.Execute(null);
        Assert.Equal((48, 40), (rig.Doc.Width, rig.Doc.Height));
        var settings = AppSettings.Load(Path.Combine(rig.Dir, "settings"));
        var next = new MainViewModel(rig.Services, settings);
        Assert.Equal((48, 40), (next.Session.Doc.Width, next.Session.Doc.Height));
    }
}

public class AboutAndLogTests
{
    [Fact]
    public void About_ShowsVersionAuthorAndLicense_AndLogFolderOpensInTheShell()
    {
        using var rig = new Rig();
        rig.Vm.AboutCommand.Execute(null);
        var text = rig.Dialogs.Infos.Single();
        Assert.Contains("1.0.0", text);
        Assert.Contains("GPL-3.0-or-later", text);
        Assert.Contains("mrl4an", text);

        rig.Vm.OpenLogFolderCommand.Execute(null);
        Assert.Equal(Log.Directory, rig.Shell.Opened.Single());
    }

    [Fact]
    public void ShowError_WithException_IsLogged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixed-vm-log-" + Guid.NewGuid().ToString("N"));
        var old = Log.Directory;
        Log.Directory = dir;
        try
        {
            using var rig = new Rig();
            rig.Dialogs.ShowError(new InvalidOperationException("disk full"));
            Assert.Contains("disk full", File.ReadAllText(Log.CurrentFile));
            Assert.Contains("[ui]", File.ReadAllText(Log.CurrentFile));
        }
        finally { Log.Directory = old; try { Directory.Delete(dir, true); } catch { } }
    }
}

public class SymmetryScopeTests
{
    [Fact]
    public void Symmetry_AppliesToPencilAndEraser_ButNotToShapes()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        rig.Vm.Colors.SetPrimary(Rig.Red);
        c.MirrorX = true;

        c.Tool = Tool.Pencil;
        rig.Stroke(4, 10, 4, 10);
        Assert.Equal(Rig.Red, rig.Layer.Get(4, 10));
        Assert.Equal(Rig.Red, rig.Layer.Get(31 - 4, 10));          // the pencil draws the mirrored copy too

        c.Tool = Tool.Ellipse;                                      // the pencil symmetry must not "stick" to shapes
        rig.Stroke(2, 20, 8, 26);
        int rightHalf = rig.Doc.Layers.Sum(l => Enumerable.Range(0, l.Pixels.Length).Count(i => l.Pixels[i] == Rig.Red && i % l.Width > 15));
        Assert.Equal(1, rightHalf);                                // on the right only the mirrored pencil dot, the shape has no copy
    }
}

public class ChatPipelineFlowTests
{
    private const string Answer = "Sure!\n```json\n{\"name\":\"shrink\",\"steps\":[{\"op\":\"resize\",\"width\":4}]}\n```";

    [Fact]
    public void Dialog_ValidatesPastedAnswerLive_AndOffersAFixRequestForProblems()
    {
        using var rig = new Rig();
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, () => "PROMPT", () => rig.Layer.ToImage());
        Assert.False(vm.IsValid);                                   // empty for now
        Assert.False(vm.ApplyCommand.CanExecute(null));

        vm.Text = Answer;
        Assert.True(vm.IsValid);
        Assert.Contains("1", vm.Status);                            // "steps 1"
        Assert.True(vm.ApplyCommand.CanExecute(null));

        vm.Text = "```json\n{\"steps\":[{\"op\":\"resise\"}]}\n```";
        Assert.False(vm.IsValid);
        Assert.Contains(vm.Messages, m => m.Contains("resise") && m.Contains("resize"));

        vm.Text = "no json here";
        Assert.False(vm.IsValid);
        Assert.NotEmpty(vm.Messages);
    }

    [Fact]
    public void Dialog_PastesFromTheClipboard_AndCopiesThePrompt()
    {
        using var rig = new Rig();
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, () => "THE PROMPT", () => rig.Layer.ToImage());
        vm.CopyPromptCommand.Execute(null);
        Assert.Equal("THE PROMPT", rig.Clipboard.Text);

        rig.Clipboard.Text = Answer;
        vm.PasteCommand.Execute(null);
        Assert.True(vm.IsValid);
        rig.Clipboard.Text = null;
        vm.PasteCommand.Execute(null);                              // an empty clipboard: the text is not overwritten
        Assert.True(vm.IsValid);
        Assert.NotEmpty(vm.Notice);
    }

    [Fact]
    public void Preview_IsBuiltLive_AndEditingTheJsonChangesTheResult()
    {
        using var rig = new Rig();
        for (int y = 4; y < 20; y++) for (int x = 4; x < 20; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, () => "P", () => rig.Layer.ToImage());
        Assert.False(vm.HasPreview);
        vm.Text = Answer;
        Assert.True(vm.HasPreview);
        Assert.Equal(4, vm.After!.Width);
        Assert.Equal(32, vm.Before!.Width);                         // the layer is not touched: "before" is the original picture
        Assert.Equal(32, rig.Layer.Width);

        vm.Text = Answer.Replace("\"width\":4", "\"width\":8");     // the user fixed the JSON by hand
        Assert.Equal(8, vm.After!.Width);

        vm.Text = "```json\n{\"steps\":[{\"op\":\"resise\"}]}\n```";
        Assert.False(vm.HasPreview);                               // with an error there is no preview, applying is impossible
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void Paste_FormatsTheAnswerIntoCleanEditableJson()
    {
        using var rig = new Rig();
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, () => "P", () => rig.Layer.ToImage());
        rig.Clipboard.Text = Answer;
        vm.PasteCommand.Execute(null);
        Assert.StartsWith("{", vm.Text);                            // the chat explanations are removed
        Assert.DoesNotContain("Sure", vm.Text);                     // prose and the code fence are gone
        Assert.Contains("\"op\": \"resize\"", vm.Text);              // short objects are laid out on one line with spaces
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void Paste_RemembersTheVariantAtOnce_WithoutDuplicates_AndHidesTheAddButtonOnAKnownVariant()
    {
        using var rig = new Rig();
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, () => "P", () => rig.Layer.ToImage());
        Assert.False(vm.CanAddVariant);
        vm.PasteText(Answer);
        Assert.Single(vm.Variants);                                 // the paste remembered the variant at once
        Assert.Same(vm.Variants[0], vm.SelectedVariant);
        Assert.False(vm.CanAddVariant);                             // we are on a variant: the "keep" button is not needed
        vm.PasteText(Answer.Replace("{\"name", "{ \"name"));           // the same template in a different layout
        Assert.Single(vm.Variants);

        vm.Text = vm.Text.Replace("\"width\": 4", "\"width\": 8");    // a manual edit: already a new result
        Assert.Null(vm.SelectedVariant);
        Assert.True(vm.CanAddVariant);
        vm.AddVariantCommand.Execute(null);
        Assert.Equal(2, vm.Variants.Count);
        Assert.False(vm.CanAddVariant);
        Assert.NotEqual(vm.Variants[0].Name, vm.Variants[1].Name);
        Assert.Equal(4, vm.Variants[0].After.Width);
        Assert.Equal(8, vm.Variants[1].After.Width);

        vm.SelectedVariant = vm.Variants[0];                        // choosing loads the JSON and creates no copies
        Assert.Contains("4", vm.Text);
        Assert.Equal(4, vm.After!.Width);
        Assert.Equal(2, vm.Variants.Count);
        vm.RemoveVariantCommand.Execute(null);
        Assert.Single(vm.Variants);
    }

    [Fact]
    public void Variants_HaveNoLimit()
    {
        using var rig = new Rig();
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, () => "P", () => rig.Layer.ToImage());
        for (int w = 2; w < 14; w++) vm.PasteText(Answer.Replace("\"width\":4", "\"width\":" + w));
        Assert.Equal(12, vm.Variants.Count);
    }

    [Fact]
    public void MenuFlow_OneDialog_CopiesPromptWithImageInfo_ThenAppliesTheEditedAnswerOnTheLayer()
    {
        using var rig = new Rig();
        for (int y = 4; y < 20; y++) for (int x = 4; x < 20; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;

        string? prompt = null;
        rig.Dialogs.ChatConfigure = d =>
        {
            d.CopyPromptCommand.Execute(null);
            prompt = rig.Clipboard.Text;
            d.Text = Answer;
        };
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.Contains("32x32", prompt);                           // the size of the current picture in the prompt
        Assert.Contains("- resize(", prompt);
        Assert.Empty(rig.Dialogs.PreviewsShown);                    // the preview is already inside the dialog, there is no second window
        Assert.Equal(4, rig.Layer.Width);                           // applied: the layer became 4 px wide
    }

    [Fact]
    public void SizeChange_CanResizeTheCanvasInPlace_KeepingTheDocumentAndUndo()
    {
        using var rig = new Rig();
        for (int y = 4; y < 20; y++) for (int x = 4; x < 20; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        var doc = rig.Doc;
        rig.Dialogs.ChatConfigure = d =>
        {
            d.Text = Answer;
            d.RunPreview();
            Assert.True(d.ChangesSize);
            Assert.True(d.ApplySecondaryCommand.CanExecute(null));
            d.ApplySecondaryCommand.Execute(null);
        };
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.Same(doc, rig.Doc);                                  // the same document, not a new one
        Assert.Equal(4, rig.Doc.Width);
        Assert.Equal(4, rig.Doc.Layers[^1].Width);
        doc.History.Undo();
        doc.History.Undo();
        Assert.Equal(32, rig.Doc.Width);                            // undo returns the canvas
    }
}

public class OperationTunerFlowTests
{
    [Fact]
    public void Tuner_ShowsParametersWithDefaults_PreviewsLive_AndAppliesToTheLayer()
    {
        using var rig = new Rig();
        for (int y = 4; y < 8; y++) for (int x = 4; x < 8; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        string hex = $"#{(Rig.Red & 0xFFFFFFu) & 0xFFFFFFu:X6}";
        rig.Dialogs.TunerConfigure = t =>
        {
            Assert.Equal("removeBackground", t.OperationName);          // the menu offers the background by default
            Assert.Contains(t.Parameters, p => p.Name == "follow");
            Assert.Equal("", t.Parameters.First(p => p.Name == "tolerance").Value);     // tolerance is automatic now: nothing is prefilled, an empty field means auto
            Assert.Equal("90", t.Parameters.First(p => p.Name == "limit").Value);       // a plain default value is visible
            t.SelectedOperation = t.Catalog.First(o => o.Name == "replaceColor");
            Assert.Equal("replaceColor", t.OperationName);
            t.Parameters.First(p => p.Name == "from").Value = hex;
            t.Parameters.First(p => p.Name == "to").Value = "transparent";
            t.RunPreview();
            Assert.True(t.CanApply);
            Assert.Contains("16", t.Summary);                           // 16 pixels will change
            Assert.Contains("replaceColor", "replaceColor");
            Assert.Equal(Rig.Red, rig.Layer.Pixels[4 * 32 + 4]);        // the layer is not touched before applying
        };
        rig.Vm.Automation.TuneOperationCommand.Execute("removeBackground|{\"follow\":8}");
        Assert.Equal(0u, rig.Layer.Pixels[4 * 32 + 4]);
        Assert.Equal(0u, rig.Layer.Pixels[7 * 32 + 7]);
    }

    [Fact]
    public void Tuner_DefaultStartsFromGivenArgs_AndAnUnchangedResultCannotBeApplied()
    {
        using var rig = new Rig();
        var t = new OperationTunerViewModel(() => rig.Layer.ToImage(), "removeBackground", "{\"follow\":8}");
        Assert.Equal("8", t.Parameters.First(p => p.Name == "follow").Value);
        Assert.Contains("follow", t.ArgsJson);
        Assert.False(t.ArgsJson.Contains("tolerance"));                 // default values are not written into the JSON
        Assert.False(t.CanApply);                                       // an empty layer: nothing changes
        Assert.Contains(Loc.T("tuner.nothing_changed"), t.Summary);
    }
}

public class ChatSpriteAndSheetTests
{
    private const string Sprite = """{"name":"chest","size":[20,12],"palette":{"w":"#8B5A2B"},"outline":"#1B1B1B","items":[{"rect":[2,2,16,8],"color":"w"}]}""";
    private const string Wide = """{"name":"wide","size":[40,10],"items":[{"rect":[0,0,40,10],"color":"#FF0000"}]}""";
    private const string Sheet = """
        {"name":"walk","frameSize":[8,8],"palette":{"a":"#FF0000"},"rows":[
          {"name":"S","frames":[{"name":"stand","delay":0.4,"items":[{"rect":[1,1,6,6],"color":"a"}]},{"name":"step","items":[{"rect":[1,2,6,5],"color":"a"}]}]},
          {"name":"E","frames":[{"name":"stand","items":[{"rect":[2,1,4,6],"color":"a"}]},{"name":"step","items":[{"rect":[2,2,4,5],"color":"a"}]}]},
          {"name":"W","mirrorOf":"E"}]}
        """;

    [Fact]
    public void Dialog_DetectsSpritesAndSheets_PreviewsThemWithoutAReference_AndOffersTheRightButtons()
    {
        using var rig = new Rig();
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, _ => "P", () => rig.Layer.ToImage());
        vm.PasteText("Here is the chest:\n```json\n" + Sprite + "\n```");
        Assert.Equal(ChatKind.Sprite, vm.Kind);
        Assert.False(vm.ShowBefore);                                // nothing to compare with: drawn from nothing
        Assert.Null(vm.Before);
        Assert.Equal(20, vm.After!.Width);
        Assert.True(vm.CanApply);
        Assert.True(vm.HasSecondary);                               // "new document"
        Assert.False(vm.SaveTemplateCommand.CanExecute(null));      // a sprite is not a pipeline template
        Assert.Contains("20", vm.PreviewSummary);
        Assert.Single(vm.Variants);

        vm.PasteText(Sheet);
        Assert.Equal(ChatKind.Sheet, vm.Kind);
        Assert.Equal(16, vm.After!.Width);                          // 2 columns × 8
        Assert.Equal(24, vm.After.Height);                          // 3 rows × 8
        Assert.False(vm.HasSecondary);
        Assert.Contains("stand", vm.PreviewDetails);
        Assert.Equal(2, vm.Variants.Count);

        vm.PasteText("""{"size":[10,10],"items":[{"elipse":[1,1,5,5],"color":"#FF0000"}]}""");
        Assert.False(vm.CanApply);                                  // the typo is reported with its place
        Assert.Contains(vm.Messages, m => m.Contains("elipse") || m.Contains("ellipse"));
    }

    [Fact]
    public void CopyPrompt_AsksTheFactoryForTheChosenKind()
    {
        using var rig = new Rig();
        var asked = new List<ChatKind>();
        var vm = new ChatPipelineViewModel(rig.Services.Clipboard, k => { asked.Add(k); return "PROMPT " + k; }, () => rig.Layer.ToImage());
        vm.CopyPromptCommand.Execute(null);
        Assert.Equal("PROMPT Pipeline", rig.Clipboard.Text);
        vm.PromptKind = ChatKind.Sheet;
        vm.CopyPromptCommand.Execute(null);
        Assert.Equal("PROMPT Sheet", rig.Clipboard.Text);
        Assert.Equal([ChatKind.Pipeline, ChatKind.Sheet], asked);
    }

    [Fact]
    public void Menu_AddsASpriteAsANewLayer_AndGrowsTheCanvasWhenItIsBigger()
    {
        using var rig = new Rig();
        int layers = rig.Doc.Layers.Count;
        rig.Dialogs.ChatConfigure = d => d.Text = Sprite;
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.Equal(layers + 1, rig.Doc.Layers.Count);
        Assert.Equal("chest", rig.S.ActiveLayer.Name);
        Assert.Equal(32, rig.Doc.Width);                            // fits: the canvas stays
        Assert.Contains(rig.S.ActiveLayer.Pixels, p => !Pixel.IsEmpty(p));

        rig.Dialogs.ChatConfigure = d => d.Text = Wide;
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.Equal(40, rig.Doc.Width);                            // wider than the canvas: it grew
        Assert.Equal(Pixel.Rgb(255, 0, 0), rig.S.ActiveLayer.Get(39, 9));
        rig.Vm.UndoCommand.Execute(null);                           // the new layer
        rig.Vm.UndoCommand.Execute(null);                           // the canvas growth
        Assert.Equal(32, rig.Doc.Width);
    }

    [Fact]
    public void Menu_SpriteSecondaryButton_OpensTheSpriteAsANewDocument()
    {
        using var rig = new Rig();
        var before = rig.Doc;
        rig.Dialogs.ChatConfigure = d => { d.Text = Sprite; d.ApplySecondaryCommand.Execute(null); };
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.NotSame(before, rig.Doc);
        Assert.Equal(20, rig.Doc.Width);
        Assert.Equal(12, rig.Doc.Height);
    }

    [Fact]
    public void Menu_OpensASheetAsAnAnimationSheet_WithGridRowNamesFrameNamesAndDelays()
    {
        using var rig = new Rig();
        rig.Dialogs.ChatConfigure = d => d.Text = Sheet;
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        var doc = rig.Doc;
        Assert.Equal(16, doc.Width);
        Assert.Equal(24, doc.Height);
        Assert.Equal(8, doc.SheetCellWidth);
        Assert.Equal(8, doc.SheetCellHeight);
        var b = doc.Rsi!;
        Assert.False(b.IsRsi);                                      // an animation sheet without an RSI folder
        Assert.Equal(["S", "E", "W"], b.RowLabels);
        Assert.Equal("stand", b.FrameName(0));
        Assert.Equal(0.4, b.Delays[0][0]);
        Assert.Equal(2, b.Frames);
        Assert.Contains(doc.Layers[0].Pixels, p => !Pixel.IsEmpty(p));
    }
}

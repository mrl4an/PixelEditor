// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

/// <summary>The chat dialog asks for a sheet of the layout the user chose (or the current sheet's), checks the answer against it and never resets a sheet's names or delays.</summary>
public class ChatSheetLayoutTests
{
    /// <summary>An answer for rows x frames of the given size: no frame names and no delays (the AI omitted them).</summary>
    private static string Answer(string[] rows, int frames, int w = 16, int h = 16, string? frameJson = null)
    {
        var frame = frameJson ?? "{\"items\":[{\"rect\":[1,1,4,4],\"color\":\"#FF0000\"}]}";
        var rowsJson = string.Join(",", rows.Select(r => $"{{\"name\":\"{r}\",\"frames\":[{string.Join(",", Enumerable.Repeat(frame, frames))}]}}"));
        return $"{{\"frameSize\":[{w},{h}],\"rows\":[{rowsJson}]}}";
    }

    private static ChatPipelineViewModel Chat(Rig rig, ChatSheetSetup setup, List<(ChatKind, SheetLayoutSpec?)>? asked = null) =>
        new(rig.Services.Clipboard, (k, l) => { asked?.Add((k, l)); return "PROMPT " + k; }, () => rig.Layer.ToImage(), null, null, setup);

    private static ChatSheetSetup Setup(Rig rig, SheetLayoutSpec? doc = null, string? last = null, Action<string?, int, int, double?>? remember = null) =>
        new(rig.Settings.SheetTemplates, doc, last, 16, 24, null, remember);

    [Fact]
    public void TheSelector_IsOnlyForTheSheetKind_ListsTemplates_AndDefaultsToTheLastUsedOne()
    {
        using var rig = new Rig();
        var vm = Chat(rig, Setup(rig, last: "system:attack-4"));
        Assert.False(vm.ShowLayoutSelector);
        vm.PromptKind = ChatKind.Sheet;
        Assert.True(vm.ShowLayoutSelector);
        Assert.Equal("system:attack-4", vm.SelectedLayout!.Id);
        Assert.Equal("", vm.LayoutChoices[0].Id);                                       // "any layout" first
        Assert.Contains(vm.LayoutChoices, c => c.Id == "system:walk-8");
        vm.PromptKind = ChatKind.Sprite;
        Assert.False(vm.ShowLayoutSelector);

        var fresh = Chat(rig, Setup(rig));
        Assert.Equal("system:walk-4", fresh.SelectedLayout!.Id);                        // nothing remembered: the classic NSEW walk
    }

    [Fact]
    public void CopyPrompt_PassesTheChosenLayout_WithSizeAndDelayOverride()
    {
        using var rig = new Rig();
        var asked = new List<(ChatKind, SheetLayoutSpec?)>();
        var vm = Chat(rig, Setup(rig, last: "system:attack-4"), asked);
        vm.PromptKind = ChatKind.Sheet;
        vm.LayoutDelayText = "0,3";
        vm.CopyPromptCommand.Execute(null);
        var (kind, layout) = asked[^1];
        Assert.Equal(ChatKind.Sheet, kind);
        Assert.Equal((16, 24), (layout!.FrameWidth, layout.FrameHeight));
        Assert.Equal(["S", "N", "E", "W"], layout.Rows);
        Assert.Equal(["замах", "удар", "возврат"], layout.Frames.Select(f => f.Name));
        Assert.All(layout.Frames, f => Assert.Equal(0.3, f.Delay));

        vm.LayoutDelayText = "";
        vm.SelectedLayout = vm.LayoutChoices.First(c => c.Id == "system:walk-8");
        vm.LayoutWidthText = "20"; vm.LayoutHeightText = "28";
        vm.CopyPromptCommand.Execute(null);
        layout = asked[^1].Item2!;
        Assert.Equal(8, layout.Rows.Count);
        Assert.Equal((20, 28), (layout.FrameWidth, layout.FrameHeight));
        Assert.Equal([0.2, 0.2, 0.2], layout.Frames.Select(f => f.Delay));              // the template's own delays

        vm.SelectedLayout = vm.LayoutChoices[0];                                         // any layout: the classic prompt
        vm.CopyPromptCommand.Execute(null);
        Assert.Null(asked[^1].Item2);
    }

    [Fact]
    public void ABadSizeOrDelay_IsReported_AndNothingIsCopied()
    {
        using var rig = new Rig();
        var asked = new List<(ChatKind, SheetLayoutSpec?)>();
        var vm = Chat(rig, Setup(rig), asked);
        vm.PromptKind = ChatKind.Sheet;
        vm.LayoutWidthText = "0";
        Assert.NotEqual("", vm.LayoutError);
        vm.CopyPromptCommand.Execute(null);
        Assert.Empty(asked);
        Assert.Null(rig.Clipboard.Text);
        vm.LayoutWidthText = "16"; vm.LayoutDelayText = "fast";
        Assert.NotEqual("", vm.LayoutError);
        vm.LayoutDelayText = "";
        Assert.Equal("", vm.LayoutError);
    }

    [Fact]
    public void TheLastChoice_IsRemembered()
    {
        using var rig = new Rig();
        var seen = new List<(string?, int, int, double?)>();
        var vm = Chat(rig, Setup(rig, remember: (id, w, h, d) => seen.Add((id, w, h, d))));
        vm.PromptKind = ChatKind.Sheet;
        vm.SelectedLayout = vm.LayoutChoices.First(c => c.Id == "system:attack-4");
        vm.LayoutWidthText = "24"; vm.LayoutHeightText = "32"; vm.LayoutDelayText = "0.2";
        vm.CopyPromptCommand.Execute(null);
        Assert.Equal(("system:attack-4", 24, 32, (double?)0.2), seen[^1]);
    }

    [Fact]
    public void ADocumentLayout_IsRequired_TheSelectorIsHidden()
    {
        using var rig = new Rig();
        var doc = new SheetLayoutSpec(16, 16, ["idle", "attack"], [new SheetFrame("a", 0.5), new SheetFrame("b", 0.25)]);
        var asked = new List<(ChatKind, SheetLayoutSpec?)>();
        var vm = Chat(rig, Setup(rig, doc), asked);
        vm.PromptKind = ChatKind.Sheet;
        Assert.False(vm.ShowLayoutSelector);
        Assert.True(vm.ShowDocumentLayout);
        Assert.Contains("idle, attack", vm.DocumentLayoutText);
        vm.CopyPromptCommand.Execute(null);
        Assert.Same(doc, asked[^1].Item2);
    }

    [Fact]
    public void AMismatchingAnswer_IsReportedWithExpectedAndReceived_ButStillPreviews()
    {
        using var rig = new Rig();
        var doc = new SheetLayoutSpec(16, 16, ["idle", "attack"], [new SheetFrame("a", 0.5), new SheetFrame("b", 0.25)]);
        var vm = Chat(rig, Setup(rig, doc));
        vm.PromptKind = ChatKind.Sheet;
        vm.PasteText(Answer(["S", "N", "E"], 3, 8, 8));
        Assert.True(vm.CanApply);
        Assert.NotNull(vm.After);
        var msg = vm.Messages.Single(m => m.Contains("не совпадает") || m.Contains("differs"));
        Assert.Contains("idle, attack", msg);          // expected
        Assert.Contains("S, N, E", msg);               // received
        Assert.Contains("16×16", msg);
        Assert.Contains("8×8", msg);
        Assert.False(vm.CanFillCurrent);

        vm.PasteText(Answer(["idle", "attack"], 2));
        Assert.DoesNotContain(vm.Messages, m => m.Contains("не совпадает") || m.Contains("differs"));
        Assert.True(vm.CanFillCurrent);
    }

    [Fact]
    public void OmittedDelaysAndNames_AreTheUsersLayout_TheAnswersWinWhenGiven()
    {
        using var rig = new Rig();
        var doc = new SheetLayoutSpec(16, 16, ["idle", "attack"], [new SheetFrame("a", 0.5), new SheetFrame("b", 0.25)]);
        var vm = Chat(rig, Setup(rig, doc));
        vm.PromptKind = ChatKind.Sheet;
        vm.PasteText(Answer(["idle", "attack"], 2));
        vm.ApplySecondaryCommand.Execute(null);                                          // "open as a new sheet"
        Assert.Equal([0.5, 0.25], vm.ResultSheet!.Delays[0]);
        Assert.Equal(["a", "b"], vm.ResultSheet.FrameNames);
        Assert.False(vm.ResultFillsCurrent);

        var vm2 = Chat(rig, Setup(rig, doc));
        vm2.PromptKind = ChatKind.Sheet;
        vm2.PasteText(Answer(["idle", "attack"], 2, frameJson: "{\"name\":\"z\",\"delay\":0.9,\"items\":[]}"));
        vm2.ApplySecondaryCommand.Execute(null);
        Assert.Equal([0.9, 0.9], vm2.ResultSheet!.Delays[1]);
        Assert.Equal(["z", "z"], vm2.ResultSheet.FrameNames);
    }

    [Fact]
    public void TheSelectorLayout_FillsDelaysWhenTheAnswerOmitsThem_AndShowsThemInTheDetails()
    {
        using var rig = new Rig();
        var vm = Chat(rig, Setup(rig, last: "system:attack-4"));
        vm.PromptKind = ChatKind.Sheet;
        vm.LayoutWidthText = "16"; vm.LayoutHeightText = "16";
        vm.PasteText(Answer(["S", "N", "E", "W"], 3));
        Assert.DoesNotContain(vm.Messages, m => m.Contains("не совпадает"));
        Assert.Contains($"удар {0.08:0.###}s", vm.PreviewDetails);                        // the delays shown are the ones that will be applied
        vm.ApplyCommand.Execute(null);
        Assert.Equal([0.12, 0.08, 0.15], vm.ResultSheet!.Delays[2]);
        Assert.Equal(["замах", "удар", "возврат"], vm.ResultSheet.FrameNames);

        // changing the selector re-checks the answer at once
        var other = Chat(rig, Setup(rig, last: "system:attack-4"));
        other.PromptKind = ChatKind.Sheet;
        other.LayoutWidthText = "16"; other.LayoutHeightText = "16";
        other.PasteText(Answer(["S", "N", "E", "W"], 3));
        other.SelectedLayout = other.LayoutChoices.First(c => c.Id == "system:walk-8");
        Assert.Contains(other.Messages, m => m.Contains("не совпадает"));
    }

    [Fact]
    public void WithoutAKindOrALayout_NothingIsChecked_AsBefore()
    {
        using var rig = new Rig();
        var vm = Chat(rig, Setup(rig));
        vm.PasteText(Answer(["S"], 1));                          // the kind is still "picture": the selector is not in play
        Assert.Empty(vm.Messages);
        vm.PromptKind = ChatKind.Sheet;
        vm.SelectedLayout = vm.LayoutChoices[0];                 // any layout
        Assert.Empty(vm.Messages);
        Assert.NotNull(vm.After);                                // the preview works
    }

    // ---- applying onto an existing sheet document ----

    private static void NewHitSheet(Rig rig)
    {
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.SelectedTemplate = d.Templates.First(t => t.Id == "system:attack-4");
            d.FrameWidthText = "16"; d.FrameHeightText = "16";
            d.Name = "hit";
        };
        rig.Vm.Rsi.NewSheetCommand.Execute(null);
    }

    [Fact]
    public void Apply_DrawsIntoTheCurrentSheet_KeepingItsRowsFrameNamesAndDelays_AndUndoRestores()
    {
        using var rig = new Rig();
        NewHitSheet(rig);
        var doc = rig.Doc;
        var before = doc.Rsi!;
        Assert.Equal(0.12, before.Delays[0][0]);
        rig.Dialogs.ChatConfigure = d => { d.PromptKind = ChatKind.Sheet; d.PasteText(Answer(["S", "N", "E", "W"], 3)); };
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);

        Assert.Same(doc, rig.Doc);                                                       // the same document, not a new one
        Assert.Equal(before, rig.Doc.Rsi);                                               // rows, frame names, delays, size: untouched
        Assert.Contains(rig.Layer.Pixels, p => !Pixel.IsEmpty(p));                       // the picture was drawn
        rig.Vm.UndoCommand.Execute(null);
        Assert.All(rig.Layer.Pixels, p => Assert.True(Pixel.IsEmpty(p)));
    }

    [Fact]
    public void OpenAsNewSheet_KeepsTheLayoutNamesAndDelays_WhenTheAnswerOmitsThem()
    {
        using var rig = new Rig();
        NewHitSheet(rig);
        var old = rig.Doc;
        rig.Dialogs.ChatConfigure = d => { d.PromptKind = ChatKind.Sheet; d.PasteText(Answer(["S", "N", "E", "W"], 3)); d.ApplySecondaryCommand.Execute(null); };
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.NotSame(old, rig.Doc);
        var b = rig.Doc.Rsi!;
        Assert.Equal(["замах", "удар", "возврат"], b.FrameNames);                         // the old behavior blanked the names
        Assert.Equal([0.12, 0.08, 0.15], b.Delays[0]);                                    // and reset every delay to 0.15
        Assert.Equal(["S", "N", "E", "W"], b.RowLabels);
    }

    [Fact]
    public void AnAnswerOfAnotherGrid_OpensANewSheet_NotOverTheCurrentOne()
    {
        using var rig = new Rig();
        NewHitSheet(rig);
        var old = rig.Doc;
        rig.Dialogs.ChatConfigure = d => { d.PromptKind = ChatKind.Sheet; d.PasteText(Answer(["idle", "run"], 2)); };
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.NotSame(old, rig.Doc);
        Assert.Equal(["idle", "run"], rig.Doc.Rsi!.RowLabels);
        Assert.Equal(["замах", "удар"], rig.Doc.Rsi.FrameNames);                          // omitted names still come from the requested layout
    }

    [Fact]
    public void WithoutASheetDocument_TheSelectorLayoutDecidesTheNewSheet()
    {
        using var rig = new Rig();
        rig.Settings.LastSheetTemplateId = "system:attack-4";
        rig.Settings.LastSheetFrameWidth = rig.Settings.LastSheetFrameHeight = 16;
        rig.Dialogs.ChatConfigure = d =>
        {
            Assert.True(d.ShowLayoutSelector || d.PromptKind != ChatKind.Sheet);
            d.PromptKind = ChatKind.Sheet;
            Assert.Equal("system:attack-4", d.SelectedLayout!.Id);
            d.PasteText(Answer(["S", "N", "E", "W"], 3));
        };
        rig.Vm.Automation.ChatPipelineCommand.Execute(null);
        Assert.Equal([0.12, 0.08, 0.15], rig.Doc.Rsi!.Delays[0]);
        Assert.Equal("system:attack-4", rig.Settings.LastSheetTemplateId);
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

/// <summary>
/// A fake interface: for each dialog model the "user" set by a delegate answers.
/// The shown messages and windows are recorded for the checks.
/// </summary>
public sealed class FakeDialogs : IDialogService
{
    public Func<string, string, string, string?> PromptFn = (_, _, def) => def;
    public Func<string, bool> ConfirmFn = _ => true;
    public Func<SaveChoice> SaveChoiceFn = () => SaveChoice.Discard;
    public Func<string?> OpenFileFn = () => null;
    public Func<string?> SaveFileFn = () => null;
    public Func<string?> FolderFn = () => null;
    public Func<int, int, NewDocumentResult?> NewDocFn = (_, _) => null;
    public Func<int, int, CanvasSizeResult?> CanvasSizeFn = (_, _) => null;

    /// <summary>Setting up the import dialog by the "user". null means the dialog is cancelled.</summary>
    public Action<ImportViewModel>? ImportConfigure;

    /// <summary>Setting up the "Image size" dialog by the "user" (null: cancel).</summary>
    public Action<ImageSizeViewModel>? ImageSizeConfigure;

    /// <summary>The RSI and export dialogs: set up by the "user", null means cancel.</summary>
    public Action<RsiProjectViewModel>? RsiProjectConfigure;
    public Action<RsiStateViewModel>? RsiStateConfigure;
    public Action<RsiOpenViewModel>? RsiOpenConfigure;
    public Action<AnimationExportViewModel>? ExportConfigure;

    /// <summary>Filling the "name and description" dialog by the "user" (null: cancel). "Save" is pressed after the call.</summary>
    public Action<NameDescriptionViewModel>? NameDescriptionConfigure;
    public Action<ChatPipelineViewModel>? ChatConfigure;

    /// <summary>The start screen: the "user" presses a button inside (null: the screen is just closed). The shown models are recorded.</summary>
    public Action<StartScreenViewModel>? StartScreenConfigure;
    public List<StartScreenViewModel> StartScreens { get; } = new();
    public Action<OperationTunerViewModel>? TunerConfigure;

    /// <summary>Setting up the template builder by the "user" (null: cancel). "Save" is pressed after the call.</summary>
    public Action<PipelineEditorViewModel>? EditorConfigure;

    public bool PreviewAccept = true;
    public List<PreviewViewModel> Previews { get; } = new();
    public List<ResultPreviewViewModel> PreviewsShown { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Infos { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Confirms { get; } = new();
    public List<IReadOnlyList<PixelImage>> Animations { get; } = new();
    public List<AnimationViewModel> AnimationWindows { get; } = new();
    public int SaveQuestions { get; private set; }
    public int BatchShown { get; private set; }
    public int ReferenceShown { get; private set; }

    public bool? ShowDialog(DialogViewModel vm)
    {
        bool? result = null;
        vm.CloseRequested += ok => result = ok;
        switch (vm)
        {
            case PromptViewModel p:
            {
                var answer = PromptFn(p.Title, p.Label, p.Text);
                if (answer == null) p.CancelCommand.Execute(null);
                else { p.Text = answer; p.OkCommand.Execute(null); }
                break;
            }
            case NewDocumentViewModel n:
            {
                var r = NewDocFn(int.Parse(n.WidthText), int.Parse(n.HeightText));
                if (r == null) n.CancelCommand.Execute(null);
                else { n.WidthText = r.Width.ToString(); n.HeightText = r.Height.ToString(); n.OkCommand.Execute(null); }
                break;
            }
            case CanvasSizeViewModel c:
            {
                var r = CanvasSizeFn(int.Parse(c.WidthText), int.Parse(c.HeightText));
                if (r == null) c.CancelCommand.Execute(null);
                else
                {
                    c.WidthText = r.Width.ToString();
                    c.HeightText = r.Height.ToString();
                    c.Anchor = (AnchorPosition)((int)Math.Round(r.AnchorY * 2) * 3 + (int)Math.Round(r.AnchorX * 2));
                    c.OkCommand.Execute(null);
                }
                break;
            }
            case ImportViewModel i:
                if (ImportConfigure == null) i.CancelCommand.Execute(null);
                else
                {
                    ImportConfigure(i);
                    i.Recompute();
                    if (i.ImportCommand.CanExecute(null)) i.ImportCommand.Execute(null); else i.CancelCommand.Execute(null);
                }
                break;
            case RsiProjectViewModel rp2:
                if (RsiProjectConfigure == null) rp2.CancelCommand.Execute(null);
                else { RsiProjectConfigure(rp2); rp2.OkCommand.Execute(null); }
                break;
            case RsiStateViewModel rs:
                if (RsiStateConfigure == null) rs.CancelCommand.Execute(null);
                else { RsiStateConfigure(rs); rs.OkCommand.Execute(null); }
                break;
            case RsiOpenViewModel ro:
                if (RsiOpenConfigure == null) ro.CancelCommand.Execute(null);
                else { RsiOpenConfigure(ro); ro.OkCommand.Execute(null); }
                break;
            case AnimationExportViewModel ae:
                if (ExportConfigure == null) ae.CancelCommand.Execute(null);
                else { ExportConfigure(ae); ae.OkCommand.Execute(null); }
                break;
            case ImageSizeViewModel isz:
                if (ImageSizeConfigure == null) isz.CancelCommand.Execute(null);
                else { ImageSizeConfigure(isz); isz.OkCommand.Execute(null); }
                break;
            case OperationTunerViewModel tuner:
                if (TunerConfigure == null) tuner.CancelCommand.Execute(null);
                else { TunerConfigure(tuner); tuner.ApplyCommand.Execute(null); }
                break;
            case ChatPipelineViewModel chat:
                if (ChatConfigure == null) chat.CancelCommand.Execute(null);
                else { ChatConfigure(chat); if (chat.ResultJson == null) chat.ApplyCommand.Execute(null); }   // the test may have pressed a button itself
                break;
            case StartScreenViewModel start:
                StartScreens.Add(start);
                if (StartScreenConfigure == null) start.CancelCommand.Execute(null);
                else StartScreenConfigure(start);
                break;
            case NameDescriptionViewModel nd:
                if (NameDescriptionConfigure == null) nd.CancelCommand.Execute(null);
                else { NameDescriptionConfigure(nd); nd.OkCommand.Execute(null); }
                break;
            case PipelineEditorViewModel ed:
                if (EditorConfigure == null) ed.CancelCommand.Execute(null);
                else { EditorConfigure(ed); ed.SaveCommand.Execute(null); }
                break;
            case ResultPreviewViewModel rp:
                PreviewsShown.Add(rp);
                if (PreviewAccept) rp.ApplyCommand.Execute(null); else rp.CancelCommand.Execute(null);
                break;
            case MessageViewModel m:
            {
                // the "user" confirms a question with the main button if ConfirmFn agrees, otherwise presses cancel
                Confirms.Add(m.Message);
                var primary = m.Buttons.FirstOrDefault(b => b.IsPrimary) ?? m.Buttons[0];
                m.ChooseCommand.Execute(ConfirmFn(m.Message) ? primary.Key : m.DismissKey);
                break;
            }
            case BatchViewModel:
                BatchShown++;
                vm.CancelCommand.Execute(null);
                break;
            case ReferenceViewModel:
                ReferenceShown++;
                vm.CancelCommand.Execute(null);
                break;
            default:
                vm.CancelCommand.Execute(null);
                break;
        }
        vm.NotifyClosed();
        return result;
    }

    public void Show(DialogViewModel vm)
    {
        if (vm is PreviewViewModel pv) Previews.Add(pv);
        if (vm is AnimationViewModel a)
        {
            Animations.Add(a.Frames);
            AnimationWindows.Add(a);
        }
    }

    public bool Confirm(string message, string title) { Confirms.Add(message); return ConfirmFn(message); }
    public SaveChoice AskSaveChanges() { SaveQuestions++; return SaveChoiceFn(); }
    public void ShowError(string message) => Errors.Add(message);
    public void ShowInfo(string message, string title) => Infos.Add(message);
    public void ShowWarning(string message, string title) => Warnings.Add(message);
    public string? PickOpenFile(string filter) => OpenFileFn();
    public string? PickSaveFile(string filter, string defaultName) => SaveFileFn();
    public string? PickFolder(string title) => FolderFn();
}

public sealed class FakeClipboard : IClipboardService
{
    private PixelImage? _image;
    public uint SequenceNumber { get; private set; } = 1;
    public bool Fail { get; set; }

    public PixelImage? GetImage() => _image;

    public string? Text { get; set; }
    public string? GetText() => Text;
    public bool TrySetText(string text) { if (Fail) return false; Text = text; SequenceNumber++; return true; }

    public bool TrySetImage(PixelImage image)
    {
        if (Fail) return false;
        _image = image.Clone();
        SequenceNumber++;
        return true;
    }

    /// <summary>Another application put its own picture.</summary>
    public void SetExternal(PixelImage image)
    {
        _image = image;
        SequenceNumber++;
    }
}

public sealed class FakeImages : IImageStore
{
    public Dictionary<string, PixelImage> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PixelImage Load(string path) =>
        Files.TryGetValue(path, out var img) ? img.Clone() : throw new FileNotFoundException(path);

    public void SavePng(PixelImage image, string path) => Files[path] = image.Clone();
}

public sealed class FakeTheme : IThemeService
{
    public List<string> Applied { get; } = new();
    public void Apply(string theme) => Applied.Add(theme);
}

/// <summary>A timer controlled by the test: Fire() imitates a tick.</summary>
public sealed class FakeTimer(TimeSpan interval) : IUiTimer
{
    public TimeSpan Interval { get; set; } = interval;
    public bool IsEnabled { get; private set; }
    public event Action? Tick;
    public void Start() => IsEnabled = true;
    public void Stop() => IsEnabled = false;

    public void Fire()
    {
        if (IsEnabled) Tick?.Invoke();
    }
}

public sealed class FakeTimers : IUiTimerFactory
{
    public List<FakeTimer> Created { get; } = new();

    public IUiTimer Create(TimeSpan interval)
    {
        var t = new FakeTimer(interval);
        Created.Add(t);
        return t;
    }

    /// <summary>Firing of all enabled timers created after the since mark.</summary>
    public void FireAll(int since = 0)
    {
        foreach (var t in Created.Skip(since).ToList()) t.Fire();
    }
}

public sealed class FakeShell : IShellService
{
    public List<string> Opened { get; } = new();
    public void OpenFolder(string path) => Opened.Add(path);
}

public sealed class FakeBatchRunner : IBatchRunner
{
    public BatchResult Result { get; set; } = new(0, 0, 0, 0, []);
    public Exception? Throw { get; set; }
    public List<(string Template, string In, string Out, bool Recurse)> Calls { get; } = new();

    public Task<BatchResult> RunAsync(string templatePath, string inputDir, string outputDir, bool recurse)
    {
        Calls.Add((templatePath, inputDir, outputDir, recurse));
        return Throw != null ? Task.FromException<BatchResult>(Throw) : Task.FromResult(Result);
    }
}

/// <summary>The assembled main model with fake services and a temporary settings folder.</summary>
public sealed class Rig : IDisposable
{
    public FakeDialogs Dialogs { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public FakeImages Images { get; } = new();
    public FakeTheme Theme { get; } = new();
    public FakeTimers Timers { get; } = new();
    public FakeShell Shell { get; } = new();
    public FakeBatchRunner Batch { get; } = new();
    public AppServices Services { get; }
    public AppSettings Settings { get; }
    public string Dir { get; }
    public MainViewModel Vm { get; }

    public EditorSession S => Vm.Session;
    public Document Doc => Vm.Session.Doc;
    public Layer Layer => Vm.Session.ActiveLayer;

    public Rig()
    {
        Dir = Path.Combine(Path.GetTempPath(), "pixed-vm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Settings = AppSettings.Load(Path.Combine(Dir, "settings"));
        Settings.DefaultWidth = Settings.DefaultHeight = 32;   // the tests assume a 32×32 canvas; the real default is 64×64
        Services = new AppServices(Dialogs, Clipboard, Images, Theme, Timers, Batch, Shell);
        Vm = new MainViewModel(Services, Settings);
        Vm.Canvas.NewLayerForObjects = false; // the old tests assume drawing in the current layer; the new behavior is checked by separate tests
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }

    public static readonly uint Red = Pixel.Rgb(255, 0, 0);
    public static readonly uint Blue = Pixel.Rgb(0, 0, 255);

    /// <summary>Draws a stroke with the current tool from (x0,y0) to (x1,y1).</summary>
    public void Stroke(int x0, int y0, int x1, int y1, bool right = false)
    {
        Vm.Canvas.PointerDown(x0, y0, right, alt: false);
        Vm.Canvas.PointerMove(x1, y1);
        Vm.Canvas.PointerUp();
    }
}

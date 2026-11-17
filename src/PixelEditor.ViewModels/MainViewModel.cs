// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.IO;
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

public sealed record RecentFileItem(string Path, string Display, System.Windows.Input.ICommand OpenCommand);

/// <summary>A "Language" menu item: the language name, a mark for the current one and the select command.</summary>
public sealed class LanguageEntry : ObservableObject
{
    private bool _isChecked;
    public LanguageEntry(string code, string name, System.Windows.Input.ICommand command) { Code = code; Name = name; Command = command; }
    public string Code { get; }
    public string Name { get; }
    public System.Windows.Input.ICommand Command { get; }
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}

/// <summary>The root: assembles the child models, is responsible for files (new, open, save, import), editing, the theme and the window life cycle.</summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    private readonly IThemeService _theme;
    private readonly IUiTimer _autosaveTimer;
    private string _documentName = Loc.T("app.untitled");

    public MainViewModel(AppServices services, AppSettings settings, Loc? localizer = null)
    {
        Localizer = localizer ?? Loc.Instance;
        _theme = services.Theme;
        Settings = settings;
        Session = new EditorSession(services, settings);
        Colors = new ColorsViewModel(Session);
        Canvas = new CanvasViewModel(Session, Colors);
        Layers = new LayersViewModel(Session);
        Sheet = new SheetViewModel(Session);
        Automation = new AutomationViewModel(Session, Canvas, Colors);
        Rsi = new RsiViewModel(Session);
        Session.ConfirmDiscard = ConfirmDiscard;

        NewCommand = new RelayCommand(New);
        OpenCommand = new RelayCommand(Open);
        StartScreenCommand = new RelayCommand(ShowStartScreen);
        OpenRecentCommand = new RelayCommand(p => OpenRecent(p is RecentFileItem r ? r.Path : (string)p!));
        SaveCommand = new RelayCommand(() => SaveProject(false));
        SaveAsCommand = new RelayCommand(() => SaveProject(true));
        ExportPngCommand = new RelayCommand(ExportPng);
        ImportCommand = new RelayCommand(Import);
        ImportClipboardCommand = new RelayCommand(ImportClipboard);
        ExitCommand = new RelayCommand(() => CloseRequested?.Invoke());
        AboutCommand = new RelayCommand(() => Dialogs.ShowInfo(Loc.T("about.text", AppInfo.Name, AppInfo.Version, AppInfo.Copyright, AppInfo.License, Log.Directory), Loc.T("about.title")));
        OpenLogFolderCommand = new RelayCommand(() => { System.IO.Directory.CreateDirectory(Log.Directory); Session.Services.Shell.OpenFolder(Log.Directory); });
        UndoCommand = new RelayCommand(Undo, () => Canvas.HasFloat || Canvas.IsTextEditing || Session.Doc.History.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => Session.Doc.History.CanRedo);
        SetThemeCommand = new RelayCommand(p => SetTheme((string)p!));
        OpenPreviewCommand = new RelayCommand(OpenPreview);
        ToggleThemeCommand = new RelayCommand(() => SetTheme(IsThemeDark ? "Light" : "Dark"));

        SetLanguageCommand = new RelayCommand(p => SetLanguage((string)p!));
        RefreshLanguages();
        Localizer.LanguageChanged += OnLanguageChanged;

        Session.StateChanged += OnStateChanged;
        OnStateChanged();
        RefreshRecent();

        // autosave once a minute if there are unsaved edits
        _autosaveTimer = services.Timers.Create(TimeSpan.FromSeconds(60));
        _autosaveTimer.Tick += Autosave;
        _autosaveTimer.Start();
    }

    public AppSettings Settings { get; }

    /// <summary>Localization (in the application the shared instance <see cref="Loc.Instance"/>).</summary>
    public Loc Localizer { get; }

    /// <summary>The "Language" menu items.</summary>
    public ObservableCollection<LanguageEntry> Languages { get; } = new();
    public RelayCommand SetLanguageCommand { get; }
    public string CurrentLanguageCode => Localizer.CurrentLanguage.ToUpperInvariant();

    /// <summary>Chooses the UI language and remembers it in the settings.</summary>
    private void SetLanguage(string code)
    {
        if (!Localizer.SetLanguage(code)) return;
        Settings.Language = Localizer.CurrentLanguage;
        Settings.Save();
        RefreshLanguages();
    }

    private void RefreshLanguages()
    {
        OnPropertyChanged(nameof(CurrentLanguageCode));
        Languages.Clear();
        foreach (var l in Localizer.Available)
            Languages.Add(new LanguageEntry(l.Code, l.Name, new RelayCommand(() => SetLanguage(l.Code))) { IsChecked = l.Code == Localizer.CurrentLanguage });
    }

    // texts computed in advance are recomputed when the language changes
    private void OnLanguageChanged()
    {
        OnStateChanged();
        OnPropertyChanged(nameof(ThemeLabel));
        RefreshRecent();
    }

    public EditorSession Session { get; }
    public ColorsViewModel Colors { get; }
    public CanvasViewModel Canvas { get; }
    public LayersViewModel Layers { get; }
    public SheetViewModel Sheet { get; }
    public AutomationViewModel Automation { get; }
    public RsiViewModel Rsi { get; }

    public string DocumentName
    {
        get => _documentName;
        private set => SetProperty(ref _documentName, value);
    }

    public ObservableCollection<RecentFileItem> RecentFiles { get; } = new();

    public bool IsThemeLight => Settings.Theme == "Light";
    public bool IsThemeDark => Settings.Theme == "Dark";

    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand StartScreenCommand { get; }
    public RelayCommand OpenRecentCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand ExportPngCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ImportClipboardCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand OpenPreviewCommand { get; }

    private PreviewViewModel? _preview;

    /// <summary>Opens the real-size preview window (a single one: a repeated call duplicates nothing).</summary>
    private void OpenPreview()
    {
        if (_preview is { IsOpen: true }) return;
        _preview = new PreviewViewModel(Session);
        Dialogs.Show(_preview);
    }

    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand SetThemeCommand { get; }

    /// <summary>The switch in the window title: dark / light.</summary>
    public RelayCommand ToggleThemeCommand { get; }

    public string ThemeLabel => IsThemeDark ? Loc.T("menu.dark") : Loc.T("menu.light");

    /// <summary>Asks the UI to close the window (the "Exit" item).</summary>
    public event Action? CloseRequested;

    private IDialogService Dialogs => Session.Dialogs;

    private void OnStateChanged()
    {
        var name = Session.ProjectPath != null ? Path.GetFileName(Session.ProjectPath) : Loc.T("app.untitled");
        DocumentName = $"{name}{(Session.Dirty ? " •" : "")}";
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    // ---------------------------------------------------------------- life cycle

    /// <summary>Called after the window is shown: offers to restore the autosave or opens a file from the command line.</summary>
    public void Startup(string? startupFile)
    {
        if (File.Exists(Settings.AutosavePath))
        {
            var when = File.GetLastWriteTime(Settings.AutosavePath);
            if (Dialogs.Confirm(Loc.T("app.an_autosave_from_was_found", when), "Pixel Editor"))
            {
                try
                {
                    Session.SetDocument(ProjectIO.Load(Settings.AutosavePath), null);
                    Session.Dirty = true;
                    Session.Status(Loc.T("app.project_restored_from_autosave_save"));
                    return;
                }
                catch (Exception ex) { Dialogs.ShowError(ex); }
            }
            Settings.DeleteAutosave();
        }
        if (startupFile != null) { OpenProjectFile(startupFile); return; }
        if (Settings.ShowStartScreen) ShowStartScreen();
    }

    /// <summary>
    /// The start screen (also the File menu item): shown modally, and the chosen action runs only after it has closed.
    /// Cancelling the action (for example the New dialog) does not bring the start screen back.
    /// </summary>
    public void ShowStartScreen()
    {
        var start = new StartScreenViewModel(this);
        Dialogs.ShowDialog(start);
        start.Chosen?.Invoke();
    }

    /// <summary>Called by the UI on a timer: saves a draft if there are unsaved edits.</summary>
    public void Autosave()
    {
        if (!Session.Dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Settings.AutosavePath)!);
            ProjectIO.Save(Session.Doc, Settings.AutosavePath);
        }
        catch { /* autosave must not interfere with work */ }
    }

    /// <summary>Whether the window may be closed. On agreement it deletes the autosave.</summary>
    public bool TryClose()
    {
        if (!ConfirmDiscard()) return false;
        _autosaveTimer.Stop();
        Settings.DeleteAutosave();
        return true;
    }

    public bool ConfirmDiscard()
    {
        Session.FlushPending();
        if (!Session.Dirty) return true;
        return Dialogs.AskSaveChanges() switch
        {
            SaveChoice.Save => SaveProject(false),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    // ---------------------------------------------------------------- editing

    private void Undo()
    {
        // while text is being typed, undo steps through the typing (and cancels the session at the start)
        if (Canvas.TextUndo()) return;
        // while an area is floating, undo cancels its move
        if (Canvas.TryCancelFloat()) return;
        Session.Doc.History.Undo();
    }

    private void Redo()
    {
        if (Canvas.TextRedo()) return;
        Session.FlushPending();
        Session.Doc.History.Redo();
    }

    // ---------------------------------------------------------------- files

    private void New()
    {
        if (!ConfirmDiscard()) return;
        var r = Dialogs.AskNewDocument(Session.Doc.Width, Session.Doc.Height);
        if (r == null) return;
        CreateBlank(r.Width, r.Height);
    }

    /// <summary>A blank document of a given size (the one-click sizes of the start screen); asks about unsaved edits first.</summary>
    public void NewBlank(int width, int height)
    {
        if (ConfirmDiscard()) CreateBlank(width, height);
    }

    private void CreateBlank(int width, int height)
    {
        Session.SetDocument(new Document(width, height), null);
        Settings.DefaultWidth = width;         // the next launch and a new document will start with this size
        Settings.DefaultHeight = height;
        Settings.Save();
    }

    private void Open()
    {
        if (!ConfirmDiscard()) return;
        var path = Dialogs.PickOpenFile(Loc.T("app.pixel_editor_project_pixproj_pixproj"));
        if (path != null) OpenProjectFile(path);
    }

    private void OpenRecent(string path)
    {
        if (ConfirmDiscard()) OpenProjectFile(path);
    }

    public void OpenProjectFile(string path)
    {
        try
        {
            Session.SetDocument(ProjectIO.Load(path), path);
            Settings.AddRecent(path);
            RefreshRecent();
        }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    public void RefreshRecent()
    {
        RecentFiles.Clear();
        foreach (var f in Settings.Recent.Where(File.Exists))
        {
            var path = f;
            RecentFiles.Add(new RecentFileItem(path, $"{Path.GetFileName(path)}   ({Path.GetDirectoryName(path)})",
                new RelayCommand(() => OpenRecent(path))));
        }
    }

    public bool SaveProject(bool forceDialog)
    {
        Session.FlushPending();
        var path = Session.ProjectPath;
        if (path == null || forceDialog)
        {
            path = Dialogs.PickSaveFile(Loc.T("app.pixel_editor_project_pixproj_pixproj"), "sprite.pixproj");
            if (path == null) return false;
        }
        try
        {
            ProjectIO.Save(Session.Doc, path);
            Session.ProjectPath = path;
            Session.Dirty = false;
            Settings.AddRecent(path);
            RefreshRecent();
            Settings.DeleteAutosave();
            Session.Status(Loc.T("app.project_saved"));
            return true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError(ex);
            return false;
        }
    }

    private void ExportPng()
    {
        Session.FlushPending();
        var path = Dialogs.PickSaveFile("PNG (*.png)|*.png", "sprite.png");
        if (path == null) return;
        try
        {
            Session.Images.SavePng(Session.Doc.Flatten(), path);
            Session.Status(Loc.T("app.exported", path));
        }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    // ---------------------------------------------------------------- import

    private void Import()
    {
        var path = Dialogs.PickOpenFile(Loc.T("app.images_png_jpg_jpeg_bmp"));
        if (path != null) ImportFile(path);
    }

    public void ImportFile(string path)
    {
        try { ImportImage(Session.Images.Load(path), Path.GetFileName(path)); }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    private void ImportClipboard()
    {
        var img = Session.Clipboard.GetImage();
        if (img == null) { Session.Status(Loc.T("app.there_is_no_image_on")); return; }
        ImportImage(img, Loc.T("app.clipboard"));
    }

    private void ImportImage(PixelImage source, string name)
    {
        Session.FlushPending();
        var r = Dialogs.AskImport(Session.Services.Timers, source, name, Colors.Palette.ToList(), canAddLayer: true);
        if (r == null) return;

        if (r.Target == ImportTarget.NewDocument)
        {
            if (ConfirmDiscard()) Session.SetDocument(Document.FromImage(r.Image), null);
            return;
        }

        // as a layer into the current document: by the top left corner, the excess is cropped
        var doc = Session.Doc;
        var px = new uint[doc.Width * doc.Height];
        for (int y = 0; y < Math.Min(doc.Height, r.Image.Height); y++)
            for (int x = 0; x < Math.Min(doc.Width, r.Image.Width); x++)
                px[y * doc.Width + x] = Pixel.Binarize(r.Image.Pixels[y * r.Image.Width + x]);
        var layer = doc.AddLayer(Path.GetFileNameWithoutExtension(name), doc.Layers.Count, px);
        Session.SetActiveLayer(layer);
        if (r.Image.Width > doc.Width || r.Image.Height > doc.Height)
            Session.Status(Loc.T("app.the_image_is_larger_than"));
    }

    /// <summary>A file dropped on the window: a project is opened, a picture is imported, a .json is run as a template.</summary>
    public void HandleDroppedFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pixproj") { if (ConfirmDiscard()) OpenProjectFile(path); }
        else if (ImageExtensions.Contains(ext)) ImportFile(path);
        else if (ext == ".json") Automation.RunPipelineFile(path);
    }

    // ---------------------------------------------------------------- theme

    private void SetTheme(string theme)
    {
        Settings.Theme = theme;
        Settings.Save();
        _theme.Apply(theme);
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(ThemeLabel));
    }
}

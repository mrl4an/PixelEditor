// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

public sealed class LayerItemViewModel : ObservableObject
{
    private readonly EditorSession session;
    private readonly Action<LayerItemViewModel>? _onBeginEdit;
    private bool _isEditing;
    private string _editName = "";

    /// <param name="onBeginEdit">Called before this row enters name editing (the list selects the row and closes other editors).</param>
    public LayerItemViewModel(Layer layer, EditorSession session, Action<LayerItemViewModel>? onBeginEdit = null)
    {
        Layer = layer;
        this.session = session;
        _onBeginEdit = onBeginEdit;
        BeginEditCommand = new RelayCommand(BeginEdit);
        CommitEditCommand = new RelayCommand(() => CommitEdit());
        CancelEditCommand = new RelayCommand(CancelEdit);
    }

    public Layer Layer { get; }

    /// <summary>The name is plain text until the pencil, F2 or a double click turns the row into an editor.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    /// <summary>The text in the editor; applied only by <see cref="CommitEdit"/>.</summary>
    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public RelayCommand BeginEditCommand { get; }
    public RelayCommand CommitEditCommand { get; }
    public RelayCommand CancelEditCommand { get; }

    public void BeginEdit()
    {
        if (IsEditing) return;
        _onBeginEdit?.Invoke(this);
        EditName = Name;
        IsEditing = true;
    }

    /// <summary>Enter or focus loss. An empty name is rejected (the old one stays); the rename is one undo step.</summary>
    public bool CommitEdit()
    {
        if (!IsEditing) return false;
        IsEditing = false;
        var text = EditName.Trim();
        if (text.Length == 0)
        {
            session.Status(Loc.T("layers.name_empty"));
            return false;
        }
        if (text == Layer.Name) return false;
        Name = text;
        return true;
    }

    /// <summary>Esc: the editor closes and the name stays as it was.</summary>
    public void CancelEdit() => IsEditing = false;

    /// <summary>A layer thumbnail for the list; updated through Refresh after edits.</summary>
    public PixelImage Thumbnail => Layer.ToImage();
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(Locked));
        OnPropertyChanged(nameof(Thumbnail));
    }

    public string Name
    {
        get => Layer.Name;
        set
        {
            if (Layer.Name == value || string.IsNullOrWhiteSpace(value)) return;
            session.FlushPending();
            session.Doc.RenameLayer(Layer, value);
        }
    }

    public bool Visible
    {
        get => Layer.Visible;
        set
        {
            if (Layer.Visible == value) return;
            session.FlushPending();
            session.Doc.SetLayerVisible(Layer, value);
        }
    }

    public bool Locked
    {
        get => Layer.Locked;
        set
        {
            if (Layer.Locked == value) return;
            session.FlushPending();
            session.Doc.SetLayerLocked(Layer, value);
        }
    }
}

public sealed class LayersViewModel : ObservableObject
{
    private readonly EditorSession _s;
    private LayerItemViewModel? _selected;
    private bool _rebuilding;

    public LayersViewModel(EditorSession session)
    {
        _s = session;
        _s.LayersChanged += Rebuild;
        _s.StateChanged += RefreshCommands;
        _s.ContentChanged += () => { foreach (var item in Items) item.Refresh(); };
        // the active layer could be changed not from the list (import as a layer, merge): the list selection follows it
        _s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorSession.ActiveLayer)) SyncSelection();
        };

        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove, () => _s.Doc.Layers.Count > 1);
        DuplicateCommand = new RelayCommand(Duplicate);
        MoveUpCommand = new RelayCommand(() => Move(+1), () => _s.ActiveLayerIndex < _s.Doc.Layers.Count - 1);
        MoveDownCommand = new RelayCommand(() => Move(-1), () => _s.ActiveLayerIndex > 0);
        MergeDownCommand = new RelayCommand(MergeDown, () => _s.Doc.CanMergeDown(_s.ActiveLayer));
        MergeVisibleCommand = new RelayCommand(MergeVisible, () => _s.Doc.CanMergeVisible());
        ShowOnlyCommand = new RelayCommand(() => ShowLayers(onlyActive: true));
        ShowAllCommand = new RelayCommand(() => ShowLayers(onlyActive: false));
        RenameSelectedCommand = new RelayCommand(() => SelectedItem?.BeginEdit());
        Rebuild();
    }

    /// <summary>F2: starts renaming the selected layer.</summary>
    public RelayCommand RenameSelectedCommand { get; }

    /// <summary>A row starts renaming: it becomes the selected layer and any other open editor is committed.</summary>
    private void OnBeginEdit(LayerItemViewModel item)
    {
        foreach (var other in Items)
            if (!ReferenceEquals(other, item)) other.CommitEdit();
        SelectedItem = item;
    }

    /// <summary>Top to bottom: the top layer of the document first.</summary>
    public ObservableCollection<LayerItemViewModel> Items { get; } = new();

    public LayerItemViewModel? SelectedItem
    {
        get => _selected;
        set
        {
            if (_rebuilding || value == null) return;
            if (!SetProperty(ref _selected, value)) return;
            if (value.Layer != _s.ActiveLayer) _s.FlushPending();
            _s.SetActiveLayer(value.Layer);
        }
    }

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand MergeDownCommand { get; }
    public RelayCommand MergeVisibleCommand { get; }
    public RelayCommand ShowOnlyCommand { get; }
    public RelayCommand ShowAllCommand { get; }

    private void Rebuild()
    {
        _rebuilding = true;
        Items.Clear();
        foreach (var l in _s.Doc.Layers.Reverse())
            Items.Add(new LayerItemViewModel(l, _s, OnBeginEdit));
        _rebuilding = false;
        SyncSelection();
        RefreshCommands();
    }

    private void SyncSelection()
    {
        var item = Items.FirstOrDefault(i => i.Layer == _s.ActiveLayer);
        if (ReferenceEquals(item, _selected)) return;
        _rebuilding = true;
        _selected = item;
        _rebuilding = false;
        OnPropertyChanged(nameof(SelectedItem));
    }

    private void RefreshCommands()
    {
        RemoveCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        MergeDownCommand.RaiseCanExecuteChanged();
        MergeVisibleCommand.RaiseCanExecuteChanged();
    }

    private void Select(Layer layer)
    {
        _s.SetActiveLayer(layer);
        SyncSelection();
    }

    private void Add()
    {
        _s.FlushPending();
        Select(_s.Doc.AddLayer(Loc.T("layers.layer", _s.Doc.Layers.Count + 1), _s.ActiveLayerIndex + 1));
    }

    private void Remove()
    {
        _s.FlushPending();
        if (!_s.Doc.RemoveLayer(_s.ActiveLayer)) _s.Status(Loc.T("layers.the_only_layer_cannot_be"));
    }

    private void Duplicate()
    {
        _s.FlushPending();
        Select(_s.Doc.DuplicateLayer(_s.ActiveLayer));
    }

    private void Move(int delta)
    {
        _s.FlushPending();
        _s.Doc.MoveLayer(_s.ActiveLayer, _s.ActiveLayerIndex + delta);
    }

    private void MergeDown()
    {
        _s.FlushPending();
        int below = _s.ActiveLayerIndex - 1;
        if (below < 0) { _s.Status(Loc.T("layers.there_is_no_layer_below")); return; }
        var target = _s.Doc.Layers[below];
        if (_s.Doc.MergeDown(_s.ActiveLayer)) Select(target);
    }

    /// <summary>All visible unlocked layers become one (one undo step); hidden and locked layers stay.</summary>
    private void MergeVisible()
    {
        _s.FlushPending();
        var merged = _s.Doc.MergeVisible();
        if (merged == null) { _s.Status(Loc.T("layers.nothing_to_merge")); return; }
        Select(merged);
        _s.Status(Loc.T("layers.visible_merged"));
    }

    private void ShowLayers(bool onlyActive)
    {
        _s.FlushPending();
        _s.Doc.ShowLayers(onlyActive ? _s.ActiveLayer : null);
    }
}

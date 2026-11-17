// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>One sheet animation: a row name and its frames.</summary>
public sealed record AnimationRow(string Name, IReadOnlyList<PixelImage> Frames);

/// <summary>
/// Frame-by-frame animation preview and GIF export. The playback timer comes from the timer service.
/// Sheet mode (RSI or an animation sheet): several animation rows with a switch, each frame has its own delay,
/// which can be edited and saved back into the sheet.
/// </summary>
public sealed class AnimationViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private readonly IUiTimer _timer;
    private readonly IReadOnlyList<AnimationRow>? _rows;
    private readonly double[][]? _delays;            // seconds: [row][frame] (sheet mode)
    private readonly Action<double[][]>? _saveDelays;
    private IReadOnlyList<PixelImage> _frames;
    private int _index, _rowIndex;
    private bool _playing;
    private string _fps = "8", _frameDelayText = "";
    private bool _loop = true;
    private bool _advancing;

    public AnimationViewModel(IDialogService dialogs, IUiTimerFactory timers, IReadOnlyList<PixelImage> frames, string description)
    {
        _dialogs = dialogs;
        _frames = frames;
        SourceDescription = description;
        _timer = timers.Create(TimeSpan.FromMilliseconds(DelayMs));
        _timer.Tick += Advance;
        PlayCommand = new RelayCommand(TogglePlay, () => Frames.Count > 1);
        ExportGifCommand = new RelayCommand(ExportGif);
        ApplyToRowCommand = new RelayCommand(() => ApplyDelay(allRows: false), () => _delays != null);
        ApplyToAllCommand = new RelayCommand(() => ApplyDelay(allRows: true), () => _delays != null);
        SaveDelaysCommand = new RelayCommand(SaveDelays, () => _delays != null && _saveDelays != null);
        if (frames.Count > 1) SetPlaying(true);
    }

    /// <summary>Sheet mode: animation rows and per-frame delays. saveDelays is called by "Save delays to the sheet".</summary>
    public AnimationViewModel(IDialogService dialogs, IUiTimerFactory timers, IReadOnlyList<AnimationRow> rows, double[][] delays,
        string description, Action<double[][]>? saveDelays = null)
        : this(dialogs, timers, rows[0].Frames, description)
    {
        _rows = rows;
        _delays = delays.Select(r => r.ToArray()).ToArray();
        _saveDelays = saveDelays;
        _timer.Interval = TimeSpan.FromMilliseconds(CurrentDelayMs());
        RefreshDelayText();
    }

    public override string Title => Loc.T("anim.animation");
    public IReadOnlyList<PixelImage> Frames => _frames;
    public string SourceDescription { get; }

    // ---- sheet rows ----

    public bool HasRows => _rows is { Count: > 1 };
    public bool UsesSheetDelays => _delays != null;
    public bool UsesFps => _delays == null;
    public IReadOnlyList<string> RowNames => _rows?.Select(r => r.Name).ToList() ?? [];

    public int SelectedRowIndex
    {
        get => _rowIndex;
        set
        {
            if (_rows == null || value < 0 || value >= _rows.Count || value == _rowIndex) return;
            _rowIndex = value;
            _frames = _rows[value].Frames;
            _index = 0;
            OnPropertyChanged(nameof(SelectedRowIndex));
            OnPropertyChanged(nameof(Frames));
            OnPropertyChanged(nameof(MaxIndex));
            OnPropertyChanged(nameof(CanScrub));
            RaiseFrameChanged();
            PlayCommand.RaiseCanExecuteChanged();
            RefreshDelayText();
        }
    }

    // ---- frames ----

    public int MaxIndex => Math.Max(0, Frames.Count - 1);
    public bool CanScrub => Frames.Count > 1;
    public PixelImage CurrentFrame => Frames[_index];
    public string FrameLabel => $"{_index + 1} / {Frames.Count}";

    /// <summary>The slider value. Moving it by hand stops playback.</summary>
    public double FrameValue
    {
        get => _index;
        set
        {
            int i = Math.Clamp((int)Math.Round(value), 0, MaxIndex);
            if (i == _index) return;
            if (!_advancing) SetPlaying(false);
            _index = i;
            RaiseFrameChanged();
        }
    }

    public bool IsPlaying => _playing;
    public string PlayButtonText => _playing ? Loc.T("anim.pause") : Loc.T("anim.play");

    public string FpsText
    {
        get => _fps;
        set
        {
            if (!SetProperty(ref _fps, value)) return;
            if (_delays == null) _timer.Interval = TimeSpan.FromMilliseconds(DelayMs);
        }
    }

    public bool Loop { get => _loop; set => SetProperty(ref _loop, value); }

    /// <summary>Frame delay in milliseconds from the entered frames per second (1..60, 8 by default).</summary>
    public int DelayMs
    {
        get
        {
            double fps = double.TryParse(_fps, out var f) && f > 0 ? Math.Min(f, 60) : 8;
            return (int)Math.Round(1000 / fps);
        }
    }

    // ---- per-frame delays (sheet mode) ----

    /// <summary>The current frame delay in seconds. Input changes only this frame until "For the row" or "For all" is pressed.</summary>
    public string FrameDelayText
    {
        get => _frameDelayText;
        set
        {
            if (!SetProperty(ref _frameDelayText, value) || _delays == null) return;
            if (TryParseSeconds(value, out var s)) SetCurrentDelay(s);
        }
    }

    /// <summary>Row summary: the total cycle duration.</summary>
    public string RowSummary => _delays == null ? "" : Loc.T("anim.row_loop_s_frames", _delays[RowDelayIndex].Take(Frames.Count).Sum().ToString("0.###", CultureInfo.InvariantCulture), Frames.Count);

    public RelayCommand PlayCommand { get; }
    public RelayCommand ExportGifCommand { get; }
    public RelayCommand ApplyToRowCommand { get; }
    public RelayCommand ApplyToAllCommand { get; }
    public RelayCommand SaveDelaysCommand { get; }

    /// <summary>The current delays (a copy) for checking and saving.</summary>
    public double[][]? Delays => _delays?.Select(r => r.ToArray()).ToArray();

    private int RowDelayIndex => Math.Min(_rowIndex, (_delays?.Length ?? 1) - 1);

    private static bool TryParseSeconds(string text, out double seconds) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds > 0 && seconds <= 60;

    private double DelayAt(int row, int frame)
    {
        var r = _delays![Math.Min(row, _delays.Length - 1)];
        return r[Math.Min(frame, r.Length - 1)];
    }

    private int CurrentDelayMs() => _delays == null ? DelayMs : Math.Max(10, (int)Math.Round(DelayAt(_rowIndex, _index) * 1000));

    private void SetCurrentDelay(double seconds)
    {
        var row = _delays![RowDelayIndex];
        if (_index < row.Length) row[_index] = seconds;
        OnPropertyChanged(nameof(RowSummary));
    }

    private void RefreshDelayText()
    {
        if (_delays == null) return;
        _frameDelayText = DelayAt(_rowIndex, _index).ToString("0.###", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(FrameDelayText));
        OnPropertyChanged(nameof(RowSummary));
    }

    private void ApplyDelay(bool allRows)
    {
        if (_delays == null || !TryParseSeconds(_frameDelayText, out var s)) return;
        var rows = allRows ? Enumerable.Range(0, _delays.Length) : [RowDelayIndex];
        foreach (var r in rows)
            for (int i = 0; i < _delays[r].Length; i++) _delays[r][i] = s;
        OnPropertyChanged(nameof(RowSummary));
    }

    private void SaveDelays()
    {
        if (_delays == null || _saveDelays == null) return;
        _saveDelays(Delays!);
        _dialogs.ShowInfo(Loc.T("anim.frame_delays_saved_to_the"), Loc.T("anim.animation"));
    }

    // ---- playback ----

    private void RaiseFrameChanged()
    {
        OnPropertyChanged(nameof(FrameValue));
        OnPropertyChanged(nameof(CurrentFrame));
        OnPropertyChanged(nameof(FrameLabel));
        if (_delays != null)
        {
            RefreshDelayText();
            _timer.Interval = TimeSpan.FromMilliseconds(CurrentDelayMs());
        }
    }

    private void SetPlaying(bool playing)
    {
        if (_playing == playing) return;
        _playing = playing;
        if (playing) _timer.Start(); else _timer.Stop();
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayButtonText));
    }

    private void TogglePlay()
    {
        if (!_playing && _index >= MaxIndex && !_loop)
        {
            _index = 0;
            RaiseFrameChanged();
        }
        SetPlaying(!_playing);
    }

    private void Advance()
    {
        int next = _index + 1;
        if (next >= Frames.Count)
        {
            if (!_loop) { SetPlaying(false); return; }
            next = 0;
        }
        _advancing = true;
        _index = next;
        RaiseFrameChanged();
        _advancing = false;
    }

    private void ExportGif()
    {
        var path = _dialogs.PickSaveFile("GIF (*.gif)|*.gif", "animation.gif");
        if (path == null) return;
        try
        {
            var frames = Frames.Select(f => Transform.BinarizeAlpha(f, 128)).ToList();
            int delay = _delays == null ? DelayMs : Math.Max(10, (int)Math.Round(_delays[RowDelayIndex].Take(frames.Count).Average() * 1000));
            File.WriteAllBytes(path, GifWriter.Encode(frames, delay, _loop));
            _dialogs.ShowInfo(Loc.T("anim.frames_saved_delay_ms", frames.Count, delay), "GIF");
        }
        catch (Exception ex) { _dialogs.ShowError(ex); }
    }

    protected override void OnClosed() => _timer.Stop();
}

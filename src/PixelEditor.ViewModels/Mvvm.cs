// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PixelEditor.ViewModels;

/// <summary>
/// Postpones an action until the events calm down (for example recomputing statistics after a series of strokes).
/// Without a timer factory it runs the action at once: convenient in tests.
/// </summary>
public sealed class Debouncer
{
    private readonly IUiTimer? _timer;
    private readonly Action _action;
    private bool _pending;

    public Debouncer(IUiTimerFactory? timers, TimeSpan delay, Action action)
    {
        _action = action;
        if (timers == null) return;
        _timer = timers.Create(delay);
        _timer.Tick += OnTick;
    }

    public void Trigger()
    {
        if (_timer == null) { _action(); return; }
        _pending = true;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Runs the postponed action immediately if one is waiting.</summary>
    public void Flush()
    {
        if (!_pending) return;
        _timer?.Stop();
        _pending = false;
        _action();
    }

    private void OnTick()
    {
        _timer!.Stop();
        if (!_pending) return;
        _pending = false;
        _action();
    }
}

/// <summary>A dynamic menu item: a header and a ready command.</summary>
public sealed record MenuEntry(string Header, ICommand Command);

/// <summary>The base class of view models: INotifyPropertyChanged without third-party libraries.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A simple command. CanExecuteChanged is raised manually through RaiseCanExecuteChanged,
/// because the library does not depend on WPF (CommandManager).
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

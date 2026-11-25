// src/Panel/ViewModels/MainViewModel.cs
namespace RobloxAutoManager.Panel.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Panel.Views;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string RemovedSignal = "__REMOVED__";

    private readonly IAccountWatcher _watcher;
    private readonly ILogService _logs;
    private readonly Func<AddAccountDialog> _dialogFactory;

    private string _statusMessage = "Ready. Add accounts and press Start.";
    private Account? _selectedAccount;
    private bool _isWatching;

    public ObservableCollection<Account> Accounts { get; } = new();

    public ICommand AddAccountCommand { get; }
    public ICommand StartWatchCommand { get; }
    public ICommand StopWatchCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public event Action<string>? RequestOpenSettings;
    public event Action? RequestForceQuit;

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public Account? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            _selectedAccount = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public MainViewModel(
        IAccountWatcher watcher,
        ILogService logs,
        Func<AddAccountDialog> dialogFactory)
    {
        _watcher = watcher;
        _logs = logs;
        _dialogFactory = dialogFactory;

        _watcher.AccountStateChanged += OnAccountStateChanged;

        AddAccountCommand = new RelayCommand(OnAddAccount);

        StartWatchCommand = new RelayCommand(
            async () =>
            {
                try
                {
                    await _watcher.StartWatchingAsync();
                    _isWatching = true;
                    StatusMessage = $"Watching {Accounts.Count} accounts...";
                    _logs.Log(LogLevel.Info, "Panel", "Watcher started by operator.");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Start failed: {ex.Message}";
                    _logs.Log(LogLevel.Error, "Panel", "Start failed", details: ex.ToString());
                }
                finally
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            },
            () => !_isWatching && Accounts.Count > 0);

        StopWatchCommand = new RelayCommand(
            async () =>
            {
                try
                {
                    await _watcher.StopWatchingAsync();
                    _isWatching = false;
                    StatusMessage = "Watcher stopped.";
                    _logs.Log(LogLevel.Info, "Panel", "Watcher stopped by operator.");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Stop failed: {ex.Message}";
                }
                finally
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            },
            () => _isWatching);

        RemoveSelectedCommand = new RelayCommand(
            async () =>
            {
                if (SelectedAccount is null) return;
                string name = SelectedAccount.Username;

                try
                {
                    await _watcher.RemoveAccountAsync(name);
                    StatusMessage = $"Removed {name}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Remove failed: {ex.Message}";
                }
            },
            () => SelectedAccount is not null);

        OpenSettingsCommand = new RelayCommand(
            () =>
            {
                if (SelectedAccount is null) return;
                RequestOpenSettings?.Invoke(SelectedAccount.Username);
            },
            () => SelectedAccount is not null);
    }

    private void OnAddAccount()
    {
        try
        {
            var dlg = _dialogFactory();
            if (dlg.ShowDialog() != true || dlg.Result is null) return;

            var account = dlg.Result;

            if (Accounts.Any(a => a.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = $"{account.Username} already exists.";
                _logs.Log(LogLevel.Warning, "Panel", "Duplicate add rejected", account.Username);
                return;
            }

            _ = _watcher.AddAccountAsync(account);
            StatusMessage = $"Added {account.Username}. Set a Place ID in Settings.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add failed: {ex.Message}";
            _logs.Log(LogLevel.Error, "Panel", "Add account failed", details: ex.ToString());
        }
    }

    private void OnAccountStateChanged(Account account)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        void Apply()
        {
            int idx = -1;
            for (int i = 0; i < Accounts.Count; i++)
            {
                if (Accounts[i].Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            bool isRemoval = account.LastError == RemovedSignal;

            if (isRemoval)
            {
                if (idx >= 0) Accounts.RemoveAt(idx);
                StatusMessage = $"{account.Username} removed.";
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            if (idx >= 0)
                Accounts[idx] = account;
            else
                Accounts.Add(account);

            StatusMessage = $"{account.Username} → {account.State}";
            CommandManager.InvalidateRequerySuggested();
        }

        if (app.Dispatcher.CheckAccess()) Apply();
        else app.Dispatcher.Invoke(Apply);
    }

    public void ForceQuit() => RequestForceQuit?.Invoke();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
}
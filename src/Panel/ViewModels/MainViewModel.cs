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
                await _watcher.StartWatchingAsync();
                _isWatching = true;
                StatusMessage = $"Watching {Accounts.Count} accounts...";
                _logs.Log(LogLevel.Info, "Panel", "Watcher started by operator.");
            },
            () => !_isWatching && Accounts.Count > 0);

        StopWatchCommand = new RelayCommand(
            async () =>
            {
                await _watcher.StopWatchingAsync();
                _isWatching = false;
                StatusMessage = "Watcher stopped.";
                _logs.Log(LogLevel.Info, "Panel", "Watcher stopped by operator.");
            },
            () => _isWatching);

        RemoveSelectedCommand = new RelayCommand(
            async () =>
            {
                if (SelectedAccount is null) return;
                string name = SelectedAccount.Username;
                await _watcher.RemoveAccountAsync(name);
                _logs.Log(LogLevel.Info, "Panel", $"Account removed: {name}", name);
                StatusMessage = $"Removed {name}";
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
        _logs.Log(LogLevel.Info, "Panel", $"Account added: {account.Username}", account.Username);
        StatusMessage = $"Added {account.Username}";
    }

    private void OnAccountStateChanged(Account account)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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

            if (idx >= 0)
            {
                if (account.State == AccountState.Offline && !Accounts.Contains(account))
                    Accounts.RemoveAt(idx);
                else
                    Accounts[idx] = account;
            }
            else
            {
                Accounts.Add(account);
            }

            StatusMessage = $"{account.Username} → {account.State}";
            CommandManager.InvalidateRequerySuggested();
        });
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
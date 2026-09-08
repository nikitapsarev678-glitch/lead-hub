using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadHub.App.Services;
using LeadHub.Core.Parser;

namespace LeadHub.App.ViewModels;

public partial class RunViewModel : ObservableObject
{
    private readonly ParseRunner _runner;
    private readonly SingBoxRuntime _vpn;

    public ObservableCollection<WorkerProgress> Workers => _runner.Workers;
    public ObservableCollection<string> LogLines => _runner.LogLines;

    [ObservableProperty] private long _leads;
    [ObservableProperty] private long _validated;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _vpnStatus = "";

    public RunViewModel(AppState state, ParseRunner runner, SingBoxRuntime vpn)
    {
        _runner = runner;
        _vpn = vpn;
        _runner.CountsChanged += () => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Leads = _runner.TotalLeads;
            Validated = _runner.TotalValidated;
        });
        _runner.RunFinished += () => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsRunning = false;
            Leads = _runner.TotalLeads;
        });
        _vpn.StatusChanged += s => Application.Current.Dispatcher.BeginInvoke(() => VpnStatus = s);
        IsRunning = runner.IsRunning;
    }

    [RelayCommand]
    private void Stop()
    {
        _runner.Stop();
        IsRunning = false;
    }
}

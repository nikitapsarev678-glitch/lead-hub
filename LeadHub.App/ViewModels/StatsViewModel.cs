using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadHub.App.Services;
using LeadHub.Core.Store;

namespace LeadHub.App.ViewModels;

public partial class StatsViewModel : ObservableObject
{
    private readonly AppState _state;

    public ObservableCollection<UsageRepository.UsageRow> AccountUsage { get; } = new();
    public ObservableCollection<UsageRepository.UsageRow> VpnUsage { get; } = new();
    public ObservableCollection<RunRow> Runs { get; } = new();

    [ObservableProperty] private long _totalLeads;
    [ObservableProperty] private long _leadsWhatsapp;
    [ObservableProperty] private long _leadsTelegram;
    [ObservableProperty] private long _leadsPending;
    [ObservableProperty] private long _leadsSent;

    public StatsViewModel(AppState state) => _state = state;

    public void Reload()
    {
        AccountUsage.Clear();
        foreach (var row in _state.Usage.AccountUsage()) AccountUsage.Add(row);
        VpnUsage.Clear();
        foreach (var row in _state.Usage.VpnUsage()) VpnUsage.Add(row);
        Runs.Clear();
        foreach (var run in _state.Runs.History()) Runs.Add(run);

        var all = _state.Leads.All();
        TotalLeads = all.Count;
        LeadsWhatsapp = all.Count(l => l.Section == Core.Parser.LeadSection.WhatsappReserve);
        LeadsTelegram = all.Count(l => l.Section == Core.Parser.LeadSection.TelegramResolved);
        LeadsPending = all.Count(l => l.Section == Core.Parser.LeadSection.NeedsVerification);
        LeadsSent = all.Count(l => l.Sent);
    }
}

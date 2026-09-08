using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadHub.App.Services;
using LeadHub.Core.Export;
using LeadHub.Core.Parser;
using LeadHub.Core.Store;
using Microsoft.Win32;

namespace LeadHub.App.ViewModels;

public partial class LeadsViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly ParseRunner _runner;

    public ObservableCollection<LeadRecord> Leads { get; } = new();

    public IReadOnlyList<string> SectionLabels { get; } = new[]
    {
        "Все", "WhatsApp — резерв", "Telegram", "Нужна проверка",
    };

    [ObservableProperty] private int _sectionIndex;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasLeads;

    public LeadsViewModel(AppState state, ParseRunner runner)
    {
        _state = state;
        _runner = runner;
        _runner.CountsChanged += () => Application.Current.Dispatcher.BeginInvoke(Reload);
        Reload();
    }

    public void Reload()
    {
        var search = SearchText.Trim().ToLowerInvariant();
        var all = _state.Leads.All().AsEnumerable();

        all = SectionIndex switch
        {
            1 => all.Where(l => l.Section == LeadSection.WhatsappReserve),
            2 => all.Where(l => l.Section == LeadSection.TelegramResolved),
            3 => all.Where(l => l.Section == LeadSection.NeedsVerification),
            _ => all,
        };
        if (search.Length > 0)
            all = all.Where(l =>
                $"{l.Handle} {l.FullName} {l.City} {l.Niche} {l.Phone}".ToLowerInvariant().Contains(search));

        Leads.Clear();
        foreach (var lead in all) Leads.Add(lead);
        HasLeads = Leads.Count > 0;
    }

    partial void OnSectionIndexChanged(int value) => Reload();
    partial void OnSearchTextChanged(string value) => Reload();

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { Status = "Не удалось открыть ссылку: " + ex.Message; }
    }

    [RelayCommand]
    private void CopyText(LeadRecord lead)
    {
        var text = lead.OutreachText.Length > 0 ? lead.OutreachText : MessageGenerator.Generate(lead);
        try { Clipboard.SetText(text); Status = "Текст скопирован. Проверь, что ник в тексте — тот же, что у открытой строки."; }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void ToggleSent(LeadRecord lead)
    {
        // Отметка «Написал» ставится вручную и означает видимо подтверждённую отправку.
        _state.Leads.MarkSent(lead.Id, !lead.Sent);
        Reload();
    }

    [RelayCommand]
    private void SaveEditedText(LeadRecord lead) => _state.Leads.SetOutreachText(lead.Id, lead.OutreachText);

    [RelayCommand]
    private void ExportCsv()
    {
        if (Leads.Count == 0) { Status = "Нет лидов для экспорта."; return; }
        var dialog = new SaveFileDialog
        {
            Filter = "CSV для Excel (*.csv)|*.csv",
            FileName = $"instagram-{SectionKey()}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var path = CsvExporter.Export(Leads.ToList(), dialog.FileName, SectionFilterKey());
            Status = "CSV выгружен: " + path;
        }
        catch (Exception ex) { Status = "Ошибка CSV: " + ex.Message; }
    }

    [RelayCommand]
    private void ExportHtml()
    {
        if (Leads.Count == 0) { Status = "Нет лидов для экспорта."; return; }
        var path = Path.Combine(AppPaths.ExportDir, $"instagram-leads-{DateTime.Now:yyyyMMdd-HHmm}.html");
        try
        {
            HtmlHelperExporter.Export(Leads.ToList(), "Instagram-лиды LeadHub", path);
            Status = "HTML-хелпер готов: " + path + " — открой на компьютере или передай на телефон.";
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) { Status = "Ошибка HTML: " + ex.Message; }
    }

    private string SectionKey() => SectionIndex switch
    {
        1 => "whatsapp", 2 => "telegram", 3 => "needs-check", _ => "all",
    };

    private string? SectionFilterKey() => SectionIndex switch
    {
        1 => "whatsapp_reserve", 2 => "telegram_resolved", 3 => "needs_verification", _ => null,
    };
}

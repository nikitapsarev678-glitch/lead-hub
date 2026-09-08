using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadHub.App.Services;
using LeadHub.Core.Vpn;

namespace LeadHub.App.ViewModels;

public partial class SlotViewModel : ObservableObject
{
    public int Index { get; init; }
    public int Port { get; init; }
    [ObservableProperty] private VpnNode? _assignedNode;
}

public partial class VpnHubViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly SingBoxRuntime _vpn;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public ObservableCollection<VpnNode> Nodes { get; } = new();
    public ObservableCollection<SlotViewModel> Slots { get; } = new();
    public ObservableCollection<VpnNode> SlotChoices { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _importText = "";

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _vpnStatus = "";

    public VpnHubViewModel(AppState state, SingBoxRuntime vpn)
    {
        _state = state;
        _vpn = vpn;
        _vpn.StatusChanged += s => Application.Current.Dispatcher.BeginInvoke(() => VpnStatus = s);
        foreach (var slot in SingBoxRuntime.DefaultSlots())
            Slots.Add(new SlotViewModel { Index = slot.Index, Port = slot.Port });
        Reload();
    }

    public void Reload()
    {
        Nodes.Clear();
        foreach (var n in _state.Vpns.All().OrderBy(n => n.CountryName).ThenBy(n => n.Name))
            Nodes.Add(n);
        SlotChoices.Clear();
        foreach (var n in Nodes.Where(n => n.Enabled)) SlotChoices.Add(n);
        foreach (var s in Slots)
            s.AssignedNode = Nodes.FirstOrDefault(n => n.Id == ReadSlotSetting(s.Index));
        VpnStatus = _vpn.StatusText;
    }

    private long ReadSlotSetting(int slot)
    {
        return long.TryParse(_state.Settings.Get($"slot{slot}_node"), out var id) ? id : 0;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var text = ImportText.Trim();
        if (text.Length == 0) return;
        IsBusy = true;
        Status = "Импорт…";
        try
        {
            // Подписка по URL?
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var url = text.Split('\n').First().Trim();
                var content = await Http.GetStringAsync(url);
                text = content;
            }

            var result = SubscriptionDecoder.Decode(text);
            var imported = 0;
            foreach (var node in result.Nodes)
            {
                var country = CountryGuesser.Guess(node.Name);
                if (country == null) country = await new GeoIpClient().LookupAsync(node.Server);
                node.CountryCode = country?.Code ?? "";
                node.CountryName = country?.NameRu ?? "Другие";
                var id = _state.Vpns.Insert(node);
                if (id > 0) imported++;
            }
            Reload();
            ImportText = "";
            var errors = result.Errors.Count > 0 ? $", пропущено с ошибками: {result.Errors.Count}" : "";
            Status = $"Импортировано узлов: {imported} (всего {Nodes.Count}){errors}.";
        }
        catch (Exception ex)
        {
            Status = "Ошибка импорта: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PingAllAsync()
    {
        if (!_vpn.IsRunning)
        {
            Status = "Сначала запустите sing-box — без ядра пинги недоступны.";
            return;
        }
        IsBusy = true;
        Status = "Проверяю пинги всех узлов…";
        try
        {
            var list = _state.Vpns.All().Where(n => n.Enabled).ToList();
            await _vpn.PingAllAsync(list);
            Reload();
            Status = $"Пинги обновлены. Живых узлов: {list.Count(n => n.IsHealthy)} из {list.Count}.";
        }
        catch (Exception ex)
        {
            Status = "Ошибка пинга: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task StartVpnAsync()
    {
        IsBusy = true;
        try
        {
            await _vpn.StartAsync();
            Status = "sing-box запущен. Теперь можно проверить пинги.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task StopVpnAsync()
    {
        await _vpn.StopAsync();
        Status = "sing-box остановлен.";
    }

    [RelayCommand]
    private void ToggleEnabled(VpnNode node)
    {
        _state.Vpns.SetEnabled(node.Id, !node.Enabled);
        Reload();
    }

    [RelayCommand]
    private void DeleteNode(VpnNode node)
    {
        _state.Vpns.Delete(node.Id);
        Reload();
        Status = $"Узел «{node.Name}» удалён.";
    }

    [RelayCommand]
    private async Task AssignSlot(SlotViewModel slot)
    {
        if (slot.AssignedNode == null) return;
        _vpn.SaveSlotAssignment(slot.Index, slot.AssignedNode.Id);
        if (_vpn.IsRunning)
            await _vpn.AssignSlotAsync(slot.Index, slot.AssignedNode);
        else
            Status = $"Слот {slot.Index} закреплён за {slot.AssignedNode.Name} (вступит в силу при старте sing-box).";
    }

    [RelayCommand]
    private void ExportLinks()
    {
        var nodes = _state.Vpns.All();
        if (nodes.Count == 0)
        {
            Status = "Экспортировать нечего.";
            return;
        }
        var path = Path.Combine(AppPaths.ExportDir, $"vpn-export-{DateTime.Now:yyyyMMdd-HHmm}.txt");
        Directory.CreateDirectory(AppPaths.ExportDir);
        File.WriteAllLines(path, nodes.Select(VlessLink.ToLink));
        Status = $"Ссылки выгружены: {path} ({nodes.Count} шт.)";
    }
}

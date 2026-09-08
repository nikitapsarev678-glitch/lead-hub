using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadHub.App.Services;
using LeadHub.Core.Parser;
using LeadHub.Core.Store;
using LeadHub.Core.Vpn;

namespace LeadHub.App.ViewModels;

public partial class NicheVm : ObservableObject
{
    public string Group { get; init; } = "";
    public string Label { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
}

public partial class CityVm : ObservableObject
{
    public string Name { get; init; } = "";
    public int Tier { get; init; }
    public string TierLabel => Tier switch { 1 => "Волна 1 · миллионники", 2 => "Волна 2 · региональные центры", _ => "Волна 3 · 100–500 тыс." };
    [ObservableProperty] private bool _isSelected;
}

public partial class ParseViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly ParseRunner _runner;
    private readonly SingBoxRuntime _vpn;

    public ObservableCollection<NicheVm> Niches { get; } = new();
    public ObservableCollection<CityVm> Cities { get; } = new();
    public ObservableCollection<IgAccount> Accounts { get; } = new();
    public ObservableCollection<VpnNode> VpnNodes { get; } = new();
    public ObservableCollection<string> PresetNames { get; } = new();

    public IReadOnlyList<string> ContactModeLabels { get; } = new[]
    { "WhatsApp в приоритете", "Telegram в приоритете", "Только WhatsApp", "Только Telegram" };
    public IReadOnlyList<string> SpeedLabels { get; } = new[] { "Осторожно", "Обычно (скилл)", "Ночно" };

    [ObservableProperty] private string _presetName = "Мой пресет";
    [ObservableProperty] private int _contactModeIndex;
    [ObservableProperty] private int _speedIndex = 1;
    [ObservableProperty] private int _targetLeads = 100;
    [ObservableProperty] private int _minFollowers = 20;
    [ObservableProperty] private int _maxFollowers = 50000;
    [ObservableProperty] private int _minMedia = 5;
    [ObservableProperty] private int _maxPostAgeDays = 45;
    [ObservableProperty] private bool _requireRecentActivity = true;
    [ObservableProperty] private string _excludeNichesRe = "торт|кондитер|десерт|воздушн.*шар|детск.*школ|частн.*детск|детск.*сад";
    [ObservableProperty] private bool _usePersonalReject = true;
    [ObservableProperty] private string _customKeywordsText = "";
    [ObservableProperty] private bool _tier1 = true;
    [ObservableProperty] private bool _tier2 = true;
    [ObservableProperty] private bool _tier3 = false;
    [ObservableProperty] private bool _dedupAgainstHistory = true;
    [ObservableProperty] private int _workers = 1;
    [ObservableProperty] private int _dailyCap = 5000;
    [ObservableProperty] private int _cooldownMinutes = 120;
    [ObservableProperty] private bool _autoRotate = true;
    [ObservableProperty] private string _status = "";

    /// <summary>Ниша выбрана городами вручную (флаг «выбраны конкретные города»).</summary>
    public bool HasSelectedCities => Cities.Any(c => c.IsSelected);
    public bool HasSelectedNiches => Niches.Any(n => n.IsSelected) || CustomKeywordsText.Trim().Length > 0;

    public event Action? RunStarted;

    public ParseViewModel(AppState state, ParseRunner runner, SingBoxRuntime vpn)
    {
        _state = state;
        _runner = runner;
        _vpn = vpn;
        Reload();
        LoadPresets();
    }

    public void Reload()
    {
        var keepNiches = Niches.ToDictionary(n => n.Label, n => n.IsSelected);
        var keepCities = Cities.ToDictionary(c => c.Name, c => c.IsSelected);
        Niches.Clear();
        foreach (var n in SearchMatrix.Niches)
            Niches.Add(new NicheVm { Group = n.Group, Label = n.Label, IsSelected = keepNiches.GetValueOrDefault(n.Label) });
        Cities.Clear();
        foreach (var c in SearchMatrix.Cities)
            Cities.Add(new CityVm { Name = c.Name, Tier = c.Tier, IsSelected = keepCities.GetValueOrDefault(c.Name) });
        Accounts.Clear();
        foreach (var a in _state.Accounts.All()) Accounts.Add(a);
        VpnNodes.Clear();
        foreach (var v in _state.Vpns.Enabled()) VpnNodes.Add(v);
        OnPropertyChanged(nameof(HasSelectedNiches));
        OnPropertyChanged(nameof(HasSelectedCities));
    }

    private void LoadPresets()
    {
        PresetNames.Clear();
        PresetNames.Add(PresetName);
        foreach (var (_, name) in _state.Presets.All()) PresetNames.Add(name);
    }

    public ParsePreset BuildPreset()
    {
        var preset = new ParsePreset
        {
            Name = PresetName,
            ContactMode = ContactModeIndex switch
            {
                1 => ContactMode.TelegramFirst,
                2 => ContactMode.WhatsAppOnly,
                3 => ContactMode.TelegramOnly,
                _ => ContactMode.WhatsAppFirst,
            },
            Speed = SpeedIndex switch { 0 => PacingSpeed.Careful, 2 => PacingSpeed.Night, _ => PacingSpeed.Normal },
            TargetLeads = Math.Max(1, TargetLeads),
            MinFollowers = MinFollowers,
            MaxFollowers = MaxFollowers,
            MinMedia = MinMedia,
            MaxPostAgeDays = MaxPostAgeDays,
            RequireRecentActivity = RequireRecentActivity,
            ExcludeNichesRe = ExcludeNichesRe,
            UsePersonalReject = UsePersonalReject,
            CustomKeywords = CustomKeywordsText.Split('\n').Select(k => k.Trim()).Where(k => k.Length > 0).ToList(),
            SelectedNiches = Niches.Where(n => n.IsSelected).Select(n => n.Label).ToList(),
            SelectedCities = Cities.Where(c => c.IsSelected).Select(c => c.Name).ToList(),
            CityTiers = new List<int>(),
            DedupAgainstHistory = DedupAgainstHistory,
            Workers = Math.Clamp(Workers, 1, SingBoxRuntime.MaxSlots),
            DailyRequestCapPerAccount = DailyCap,
            CooldownMinutes = CooldownMinutes,
            AutoRotate = AutoRotate,
            ManualAccountIds = AutoRotate ? new List<long>() : Accounts.Where(IsAccountPicked).Select(a => a.Id).ToList(),
            ManualVpnNodeIds = AutoRotate ? new List<long>() : VpnNodes.Where(IsVpnPicked).Select(v => v.Id).ToList(),
        };
        if (Tier1) preset.CityTiers.Add(1);
        if (Tier2) preset.CityTiers.Add(2);
        if (Tier3) preset.CityTiers.Add(3);
        return preset;
    }

    // Ручной выбор аккаунтов/VPN хранится в словарях (чекбоксы в списках)
    private readonly HashSet<long> _pickedAccounts = new();
    private readonly HashSet<long> _pickedVpns = new();

    public void ToggleAccountPick(IgAccount account)
    {
        if (!_pickedAccounts.Remove(account.Id)) _pickedAccounts.Add(account.Id);
    }

    public void ToggleVpnPick(VpnNode node)
    {
        if (!_pickedVpns.Remove(node.Id)) _pickedVpns.Add(node.Id);
    }

    private bool IsAccountPicked(IgAccount a) => _pickedAccounts.Contains(a.Id);
    private bool IsVpnPicked(VpnNode v) => _pickedVpns.Contains(v.Id);

    [RelayCommand]
    private void SelectAllNiches() { foreach (var n in Niches) n.IsSelected = true; OnPropertyChanged(nameof(HasSelectedNiches)); }

    [RelayCommand]
    private void ClearNiches() { foreach (var n in Niches) n.IsSelected = false; OnPropertyChanged(nameof(HasSelectedNiches)); }

    /// <summary>«Любимые» ниши из скилла: дорогая аренда, загородный отдых, мебель на заказ, магазины с ассортиментом.</summary>
    [RelayCommand]
    private void SelectFavoriteNiches()
    {
        var favorites = new[]
        {
            "аренда спецтехники", "аренда автовышек и подъёмников", "аренда строительного инструмента",
            "аренда генераторов и компрессоров", "загородные дома и усадьбы", "база отдыха", "глэмпинг",
            "гостевой дом", "банный комплекс", "кухни на заказ под ключ", "шкафы и гардеробные на заказ",
            "мебель под ключ", "мебельный магазин", "магазин сантехники", "свадебный салон",
            "аренда фотостудии", "аренда катеров", "аренда квадроциклов",
        };
        foreach (var n in Niches) n.IsSelected = favorites.Contains(n.Label, StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(HasSelectedNiches));
    }

    [RelayCommand]
    private void SelectAllCities() { foreach (var c in Cities) c.IsSelected = true; OnPropertyChanged(nameof(HasSelectedCities)); }

    [RelayCommand]
    private void ClearCities() { foreach (var c in Cities) c.IsSelected = false; OnPropertyChanged(nameof(HasSelectedCities)); }

    [RelayCommand]
    private void SavePreset()
    {
        var preset = BuildPreset();
        _state.Presets.Save(preset);
        LoadPresets();
        Status = $"Пресет «{preset.Name}» сохранён.";
    }

    [RelayCommand]
    private void LoadPresetByName(string name)
    {
        var preset = _state.Presets.All().Where(p => p.name == name).Select(p => p.id).Select(id => _state.Presets.Load(id)).FirstOrDefault(p => p != null);
        if (preset == null)
        {
            Status = "Пресет не найден.";
            return;
        }
        ApplyPreset(preset);
        Status = $"Пресет «{preset.Name}» загружен.";
    }

    public void ApplyPreset(ParsePreset preset)
    {
        PresetName = preset.Name;
        ContactModeIndex = preset.ContactMode switch
        {
            ContactMode.TelegramFirst => 1,
            ContactMode.WhatsAppOnly => 2,
            ContactMode.TelegramOnly => 3,
            _ => 0,
        };
        SpeedIndex = preset.Speed switch { PacingSpeed.Careful => 0, PacingSpeed.Night => 2, _ => 1 };
        TargetLeads = preset.TargetLeads;
        MinFollowers = preset.MinFollowers;
        MaxFollowers = preset.MaxFollowers;
        MinMedia = preset.MinMedia;
        MaxPostAgeDays = preset.MaxPostAgeDays;
        RequireRecentActivity = preset.RequireRecentActivity;
        ExcludeNichesRe = preset.ExcludeNichesRe;
        UsePersonalReject = preset.UsePersonalReject;
        CustomKeywordsText = string.Join("\n", preset.CustomKeywords);
        Tier1 = preset.CityTiers.Contains(1);
        Tier2 = preset.CityTiers.Contains(2);
        Tier3 = preset.CityTiers.Contains(3);
        DedupAgainstHistory = preset.DedupAgainstHistory;
        Workers = preset.Workers;
        DailyCap = preset.DailyRequestCapPerAccount;
        CooldownMinutes = preset.CooldownMinutes;
        AutoRotate = preset.AutoRotate;
        foreach (var n in Niches) n.IsSelected = preset.SelectedNiches.Contains(n.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var c in Cities) c.IsSelected = preset.SelectedCities.Contains(c.Name, StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(HasSelectedNiches));
        OnPropertyChanged(nameof(HasSelectedCities));
    }

    [RelayCommand]
    private void StartRun()
    {
        var preset = BuildPreset();
        var errors = new List<string>();
        if (preset.CityTiers.Count == 0) errors.Add("не выбрана ни одна волна городов");
        if (preset.SelectedNiches.Count == 0 && preset.CustomKeywords.Count == 0) errors.Add("не выбрана ни одна ниша/ключевое слово");
        if (!_vpn.IsRunning) errors.Add("sing-box не запущен (вкладка «VPN»)");
        if (VpnNodes.Count == 0) errors.Add("нет включённых VPN-узлов");
        var activeAccounts = Accounts.Count(a => a.Status is "active" or "needs_check");
        if (activeAccounts == 0) errors.Add("нет ни одного аккаунта Instagram (вкладка «Аккаунты»)");
        if (!AutoRotate && _pickedAccounts.Count == 0) errors.Add("ручной режим: выберите аккаунты вручную");
        if (errors.Count > 0)
        {
            Status = "Не запущено — " + string.Join("; ", errors) + ".";
            return;
        }

        try
        {
            _runner.Start(preset);
            Status = "Прогон запущен. Смотрите вкладку «Прогон».";
            RunStarted?.Invoke();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}

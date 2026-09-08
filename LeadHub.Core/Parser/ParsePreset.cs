using System.Text.Json.Serialization;

namespace LeadHub.Core.Parser;

/// <summary>
/// Пресет панели парсинга — все характеристики из скилла instagram-whatsapp-lead-parser.
/// Сохраняется/загружается в БД, сериализуется в run-settings.
/// </summary>
public sealed class ParsePreset
{
    public string Name { get; set; } = "Новый пресет";

    // — Тема поиска —
    public List<string> SelectedNiches { get; set; } = new();      // label'ы из SearchMatrix
    public List<string> CustomKeywords { get; set; } = new();      // свои ключевые фразы
    public List<string> SelectedCities { get; set; } = new();      // города (все — если пусто)
    public List<int> CityTiers { get; set; } = new() { 1, 2 };     // волны: 1 миллионники, 2 региональные центры, 3 100–500к

    // — Цель и контакты —
    public int TargetLeads { get; set; } = 100;
    public ContactMode ContactMode { get; set; } = ContactMode.WhatsAppFirst;

    // — Фильтры квалификации (порты из скилла) —
    public int MinFollowers { get; set; } = 20;
    public int MaxFollowers { get; set; } = 50000;
    public int MinMedia { get; set; } = 5;
    public int MaxPostAgeDays { get; set; } = 45;
    public bool RequireRecentActivity { get; set; } = true;        // пост ≤45 дней или сторис за 48ч
    public string ExcludeNichesRe { get; set; } = "торт|кондитер|десерт|воздушн.*шар|детск.*школ|частн.*детск|детск.*сад";
    public bool UsePersonalReject { get; set; } = true;            // отсев тренеров/блогеров/одиночных мастеров
    public bool ExcludeSiteInProfile { get; set; } = true;

    // — Дедупликация —
    public bool DedupAgainstHistory { get; set; } = true;

    // — Скорость и лимиты (пейсинг из скилла) —
    public PacingSpeed Speed { get; set; } = PacingSpeed.Normal;
    public int DailyRequestCapPerAccount { get; set; } = 5000;
    public int CooldownMinutes { get; set; } = 120;                // кулдаун аккаунта при 429/challenge

    // — Ресурсы —
    public int Workers { get; set; } = 1;                          // параллельных воркеров = слотов
    public bool AutoRotate { get; set; } = true;                   // авто-выбор наименее используемых
    public List<long> ManualAccountIds { get; set; } = new();
    public List<long> ManualVpnNodeIds { get; set; } = new();

    // — Внешние источники —
    public bool UseBraveSearch { get; set; } = false;

    public ParseOptions ToOptions() => new()
    {
        ContactMode = ContactMode,
        MinFollowers = MinFollowers,
        MaxFollowers = MaxFollowers,
        MinMedia = MinMedia,
        MaxPostAgeDays = RequireRecentActivity ? MaxPostAgeDays : 3650,
    };

    public (int DiscoveryPauseMs, int DiscoveryJitterMs, int ValidationDelayMs, int ValidationJitterMs, int MaxPerSecondPerWorker) Pacing() => Speed switch
    {
        PacingSpeed.Careful => (3200, 1800, 800, 400, 1),
        PacingSpeed.Night => (2400, 1400, 600, 300, 1),
        _ => (2000, 1200, 480, 220, 2),   // Normal — значения скилла
    };
}

public enum PacingSpeed { Careful, Normal, Night }

public enum ContactQueryTermsMode
{
    /// <summary>Термины контактов по режиму: WhatsApp-first/only → WhatsApp-термины, Telegram-first/only → Telegram-термины.</summary>
    Auto,
}

public static class ContactQueryTerms
{
    public static readonly string[] WhatsApp = { "WhatsApp", "ватсап", "вотсап", "wa.me", "телефон", "+7" };
    public static readonly string[] Telegram = { "Telegram", "телеграм", "t.me", "tg" };

    public static string[] For(ContactMode mode) => mode switch
    {
        ContactMode.TelegramFirst or ContactMode.TelegramOnly => Telegram,
        _ => WhatsApp,
    };
}

namespace LeadHub.Core.Parser;

/// <summary>Кандидат из discovery (поиск/Brave) до проверки профиля.</summary>
public sealed class Candidate
{
    public string Handle { get; set; } = "";
    public string Niche { get; set; } = "";
    public string NicheGroup { get; set; } = "";
    public string SourceCity { get; set; } = "";
    public string SourceQuery { get; set; } = "";
    public string Title { get; set; } = "";
    public string FullTitle { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Private { get; set; }
    public bool Following { get; set; }
    public string SocialContext { get; set; } = "";
}

public sealed class IgPostItem
{
    public string Code { get; set; } = "";
    public long TakenAt { get; set; }
    public string CaptionText { get; set; } = "";
}

/// <summary>Публичные поля профиля Instagram (web_profile_info + embed).</summary>
public sealed class IgProfile
{
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Biography { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsPrivate { get; set; }
    public bool IsBusiness { get; set; }
    public long FollowerCount { get; set; }
    public long MediaCount { get; set; }
    public List<string> BioLinks { get; set; } = new();
    public string ExternalUrl { get; set; } = "";
    public string ContactPhoneNumber { get; set; } = "";
    public string PublicPhoneNumber { get; set; } = "";
    public string CityName { get; set; } = "";
    public long LatestReelMedia { get; set; }          // timestamp последней сторис
    public bool IsWhatsappLinked { get; set; }
    public bool FollowingViewer { get; set; }          // аккаунт пользователя подписан на бизнес
    public int MutualFollowersCount { get; set; } = -1; // -1 = неизвестно
    public List<IgPostItem> LatestPosts { get; set; } = new();
    public long Pk { get; set; }
}

public enum ContactMode { WhatsAppFirst, TelegramFirst, WhatsAppOnly, TelegramOnly }

public enum LeadSection { TelegramResolved, WhatsappReserve, NeedsVerification }

/// <summary>Итоговый лид (строка таблицы).</summary>
public sealed class LeadRecord
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string Handle { get; set; } = "";
    public string InstagramUrl => $"https://www.instagram.com/{Handle}/";
    public string FullName { get; set; } = "";
    public string City { get; set; } = "";
    public string Niche { get; set; } = "";
    public string NicheGroup { get; set; } = "";
    public string Phone { get; set; } = "";
    public string TelegramUrl { get; set; } = "";
    public string WhatsappUrl { get; set; } = "";
    public string WhatsappStatus { get; set; } = "unchecked";   // published | published_profile_phone | missing_public_link | unchecked
    public string TelegramStatus { get; set; } = "unchecked";   // resolved | unchecked | not_found | rate_limited | error
    public LeadSection Section { get; set; } = LeadSection.NeedsVerification;
    public string ReasonsCsv { get; set; } = "";
    public string OutreachText { get; set; } = "";
    public bool Sent { get; set; }
    public DateTime? SentAt { get; set; }
    public string NotSentReason { get; set; } = "";
    public string Comment { get; set; } = "";
    public long FollowerCount { get; set; }
    public long MediaCount { get; set; }
    public string LatestPostDate { get; set; } = "";
    public int LatestPostAgeDays { get; set; } = -1;
    public bool ActiveStory { get; set; }
    public string BioSite { get; set; } = "";
    public bool QualifiedPreSite { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string SectionLabel => Section switch
    {
        LeadSection.TelegramResolved => "Telegram",
        LeadSection.WhatsappReserve => "WhatsApp — Telegram не найден",
        _ => "Нужна проверка",
    };
}

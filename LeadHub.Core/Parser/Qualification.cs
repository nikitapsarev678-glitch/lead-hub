namespace LeadHub.Core.Parser;

public sealed record LocationInfo(bool Confirmed, string City, string Evidence, string ForeignReason);

public sealed record QualificationResult
{
    public bool QualifiedPreSite { get; init; }
    public List<string> Reasons { get; init; } = new();
    public MessengerInfo Messenger { get; init; } = null!;
    public BioSiteInfo BioSite { get; init; } = null!;
    public string PublicPhone { get; init; } = "";
    public LocationInfo Location { get; init; } = null!;
    public long LatestPostTs { get; init; }
    public string LatestPostDate { get; init; } = "";
    public int? LatestPostAgeDays { get; init; }
    public string LatestPostUrl { get; init; } = "";
    public string LatestStoryDate { get; init; } = "";
    public bool ActiveStory { get; init; }
}

/// <summary>
/// Квалификация бизнес-профиля. Порт profileQualification/qualification/inferLocation из скилла
/// instagram-whatsapp-lead-parser, пороги 1:1.
/// </summary>
public sealed class Qualifier(ParseOptions? options = null)
{
    private readonly ParseOptions _options = options ?? new ParseOptions();

    public static readonly Regex PersonalRejectRe = new(
        @"(?:^|\b)(тренер|фитнес|йог[аи]|психолог|коуч|нутрициолог|блогер|эксперт|наставник|визажист|бровист|стилист|фотограф|электрик|electric|врач|доктор|doctor|dentist|мастер\s+маникюра|таролог|астролог|модель|актрис|личный\s+блог|personal\s+blog)(?:\b|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly (Regex Re, string Reason)[] ForeignPatterns =
    {
        (Re(@"казахстан|kazakhstan|🇰🇿|алматы|almaty|астан[аы]|astana|семей|semey|шымкент|shymkent|караганда|актобе|павлодар|тараз|атырау|костанай|\.kz\b"), "kazakhstan_marker"),
        (Re(@"беларус|белорус|belarus|🇧🇾|минск|minsk|гомель|витебск|могил[её]в|гродно|брест|\.by\b"), "belarus_marker"),
        (Re(@"украин|ukraine|🇺🇦|киев|kyiv|kiev|хар[ьк]ков|одесс[аы]|днепр|львов|\.ua\b"), "ukraine_marker"),
        (Re(@"кыргыз|киргиз|kyrgyz|🇰🇬|бишкек|bishkek|\.kg\b"), "kyrgyzstan_marker"),
        (Re(@"узбекистан|uzbekistan|🇺🇿|ташкент|tashkent|нукус|nukus|самарканд|samarkand|\.uz\b"), "uzbekistan_marker"),
        (Re(@"таджикистан|tajikistan|🇹🇯|душанбе|dushanbe|турсунзаде|\.tj\b"), "tajikistan_marker"),
        (Re(@"азербайджан|azerbaijan|🇦🇿|баку|baku|\.az\b"), "azerbaijan_marker"),
        (Re(@"грузи[яи]|georgia|🇬🇪|тбилиси|tbilisi|\.ge\b"), "georgia_marker"),
        (Re(@"армени[яи]|armenia|🇦🇲|ереван|yerevan|\.am\b"), "armenia_marker"),
    };

    private static readonly (Regex Re, string City)[] LocationAliases =
    {
        (Re(@"(?:^|[^а-яё])москв(?:а|ы|е|у|ой)(?:[^а-яё]|$)"), "Москва"),
        (Re(@"(?:^|[^а-яё])московск(?:ая|ой|ую)\s+обл(?:асть|\.)?(?:[^а-яё]|$)"), "Московская область"),
        (Re(@"(?:^|[^а-яё])санкт[-\s]?петербург(?:а|е)?(?:[^а-яё]|$)|(?:^|[^а-яё])спб(?:[^а-яё]|$)"), "Санкт-Петербург"),
        (Re(@"(?:^|[^а-яё])снежинск(?:а|е)?(?:[^а-яё]|$)"), "Снежинск"),
        (Re(@"(?:^|[^а-яё])ейск(?:а|е)?(?:[^а-яё]|$)"), "Ейск"),
        (Re(@"(?:^|[^а-яё])бикин(?:а|е)?(?:[^а-яё]|$)"), "Бикин"),
        (Re(@"(?:^|[^а-яё])назран(?:ь|и)(?:[^а-яё]|$)"), "Назрань"),
        (Re(@"(?:^|[^а-яё])дербент(?:а|е)?(?:[^а-яё]|$)"), "Дербент"),
        (Re(@"(?:^|[^а-яё])буйнакск(?:а|е)?(?:[^а-яё]|$)"), "Буйнакск"),
        (Re(@"(?:^|[^а-яё])мамедкал(?:а|е|ы)(?:[^а-яё]|$)"), "Мамедкала"),
        (Re(@"(?:^|[^а-яё])видно(?:е|го|м)(?:[^а-яё]|$)"), "Видное"),
        (Re(@"(?:^|[^а-яё])серпухов(?:а|е)?(?:[^а-яё]|$)"), "Серпухов"),
        (Re(@"(?:^|[^а-яё])чехов(?:а|е)?(?:[^а-яё]|$)"), "Чехов"),
        (Re(@"(?:^|[^а-яё])протвино(?:[^а-яё]|$)"), "Протвино"),
        (Re(@"(?:^|[^а-яё])корол[её]в(?:а|е)?(?:[^а-яё]|$)"), "Королёв"),
        (Re(@"(?:^|[^а-яё])благовещенск(?:а|е)?(?:[^а-яё]|$)"), "Благовещенск"),
        (Re(@"(?:^|[^а-яё])нов(?:ый|ого|ом)\s+уренгой(?:[^а-яё]|$)"), "Новый Уренгой"),
        (Re(@"(?:^|[^а-яё])ефремов(?:а|е)?(?:[^а-яё]|$)"), "Ефремов"),
        (Re(@"(?:^|[^а-яё])твер(?:ь|и)(?:[^а-яё]|$)"), "Тверь"),
        (Re(@"(?:^|[^а-яё])осети(?:я|и)|(?:^|[^а-яё])алани(?:я|и)(?:[^а-яё]|$)"), "Северная Осетия"),
        (Re(@"(?:^|[^а-яё])дагестан(?:а|е)?(?:[^а-яё]|$)"), "Дагестан"),
        (Re(@"(?:^|[^а-яё])чечн(?:я|и)|(?:^|[^а-яё])чеченск(?:ая|ой)\s+республик(?:а|е|и)(?:[^а-яё]|$)"), "Чеченская Республика"),
        (Re(@"(?:^|[^а-яё])крым(?:а|е)?(?:[^а-яё]|$)"), "Крым"),
    };

    private static readonly Regex CityInBioRe = new(@"(?:г\.?|город)\s*([А-ЯЁ][А-Яа-яЁё-]{2,}(?:\s+[А-ЯЁ][А-Яа-яЁё-]{2,})?)", RegexOptions.Compiled);
    private static readonly Regex BiographyPhoneRe = new(@"(?:\+?7|8)[\s().-]*\d{3}[\s().-]*\d{3}[\s.-]*\d{2}[\s.-]*\d{2}", RegexOptions.Compiled);

    /// <summary>Порт inferLocation.</summary>
    public LocationInfo InferLocation(IgProfile user, Candidate candidate)
    {
        var raw = $"{user.FullName}\n{user.Biography}\n{user.CityName}";
        var text = raw.ToLowerInvariant();
        foreach (var (pattern, reason) in ForeignPatterns)
            if (pattern.IsMatch(text)) return new LocationInfo(false, "", "", reason);

        var known = SearchMatrix.Cities.FirstOrDefault(c => text.Contains(c.Name.ToLowerInvariant()));
        if (known != null) return new LocationInfo(true, known.Name, "known_city_in_profile", "");

        foreach (var (pattern, city) in LocationAliases)
            if (pattern.IsMatch(raw)) return new LocationInfo(true, city, "russian_location_alias_in_profile", "");

        var apiCity = (user.CityName ?? "").Trim();
        if (apiCity.Length > 0 && Regex.IsMatch(apiCity, @"(?:,\s*)?Russia\b", RegexOptions.IgnoreCase))
            return new LocationInfo(true, Regex.Replace(apiCity, @",?\s*Russia\b", "", RegexOptions.IgnoreCase).Trim(), "instagram_business_city_russia", "");
        if (apiCity.Length > 0 && Regex.IsMatch(apiCity, @"[а-яё]{3}", RegexOptions.IgnoreCase))
            return new LocationInfo(true, apiCity, "instagram_business_city", "");

        var cityMatch = CityInBioRe.Match(raw);
        if (cityMatch.Success) return new LocationInfo(true, cityMatch.Groups[1].Value.Trim(), "city_in_biography", "");

        var source = (candidate.SourceCity ?? "").Trim();
        if (source.Length > 0 && source != "Россия" && text.Contains(source.ToLowerInvariant()))
            return new LocationInfo(true, source, "source_city_in_profile", "");
        return new LocationInfo(false, "", "", "");
    }

    /// <summary>Порт profileQualification: причины отсева (без свежести постов).</summary>
    public List<string> ProfileReasons(Candidate candidate, IgProfile user, out MessengerInfo messenger, out BioSiteInfo bioSite, out string publicPhone, out LocationInfo location)
    {
        messenger = ContactExtractor.FindMessenger(user);
        bioSite = ContactExtractor.DetectBioSite(user, messenger);
        var combined = $"{user.FullName} {user.Biography} {user.Category}";
        var biographyPhone = BiographyPhoneRe.Match(user.Biography ?? "").Value;
        publicPhone = ContactExtractor.NormalizePhone(
            FirstNonEmpty(user.ContactPhoneNumber, user.PublicPhoneNumber, biographyPhone, messenger.Whatsapp));
        var messengerPhone = ContactExtractor.NormalizePhone(messenger.Whatsapp);
        var rawContactDigits = Regex.Replace($"{messenger.Whatsapp} {user.ContactPhoneNumber} {user.PublicPhoneNumber}", @"\D", "");
        location = InferLocation(user, candidate);

        var reasons = new List<string>();
        if (user.IsPrivate) reasons.Add("private_profile");
        if (user.MediaCount < _options.MinMedia) reasons.Add("too_few_posts");
        if (messenger.Telegram == "" && messenger.Whatsapp == "" && !(_options.ContactMode == ContactMode.TelegramFirst && publicPhone != ""))
            reasons.Add("messenger_not_confirmed_in_profile");
        if (bioSite.Found) reasons.Add("site_or_landing_in_profile");
        if (PersonalRejectRe.IsMatch(combined)) reasons.Add("personal_or_trainer_profile");
        if (!NicheMatch(candidate, user)) reasons.Add("niche_not_confirmed");
        if (user.FollowerCount < _options.MinFollowers) reasons.Add("very_small_audience");
        if (user.FollowerCount > _options.MaxFollowers) reasons.Add("audience_above_cap");
        if (location.ForeignReason.Length > 0) reasons.Add(location.ForeignReason);
        else if (!location.Confirmed) reasons.Add("location_not_confirmed");
        if ((publicPhone.StartsWith("76") || publicPhone.StartsWith("77")) ||
            (messengerPhone.StartsWith("76") || messengerPhone.StartsWith("77")))
            reasons.Add("probable_kazakhstan_phone");
        if (Regex.IsMatch(rawContactDigits, @"^(?:998|992|996|994|995|993|374|373)"))
            reasons.Add("foreign_phone_code");
        if (user.FollowingViewer) reasons.Add("already_following");
        if (user.MutualFollowersCount > 0) reasons.Add("mutual_followers_present");
        return reasons.Distinct().ToList();
    }

    /// <summary>Полная квалификация со свежестью (порт qualification).</summary>
    public QualificationResult Qualify(Candidate candidate, IgProfile user, long nowUnix)
    {
        var reasons = ProfileReasons(candidate, user, out var messenger, out var bioSite, out var publicPhone, out var location);

        var latest = user.LatestPosts.OrderByDescending(p => p.TakenAt).FirstOrDefault();
        var latestTs = latest?.TakenAt ?? 0;
        int? ageDays = latestTs > 0 ? (int)((nowUnix - latestTs) / 86400) : null;
        var latestStoryTs = user.LatestReelMedia;
        var storyActive = latestStoryTs > 0 && nowUnix - latestStoryTs <= 2 * 86400;
        if ((ageDays == null || ageDays > _options.MaxPostAgeDays) && !storyActive)
            reasons.Add("no_recent_post_45d_or_active_story");
        if (bioSite.Found) reasons.Add("site_or_landing_in_profile");

        reasons = reasons.Distinct().ToList();
        return new QualificationResult
        {
            QualifiedPreSite = reasons.Count == 0,
            Reasons = reasons,
            Messenger = messenger,
            BioSite = bioSite,
            PublicPhone = publicPhone,
            Location = location,
            LatestPostTs = latestTs,
            LatestPostDate = latestTs > 0 ? DateTimeOffset.FromUnixTimeSeconds(latestTs).LocalDateTime.ToString("yyyy-MM-dd") : "",
            LatestPostAgeDays = ageDays,
            LatestPostUrl = latest is { Code.Length: > 0 } ? $"https://www.instagram.com/p/{latest.Code}/" : "",
            LatestStoryDate = latestStoryTs > 0 ? DateTimeOffset.FromUnixTimeSeconds(latestStoryTs).LocalDateTime.ToString("yyyy-MM-dd") : "",
            ActiveStory = storyActive,
        };
    }

    /// <summary>Порт candidateNicheMatch: подтверждение ниши по ключам в профиле.</summary>
    public bool NicheMatch(Candidate candidate, IgProfile user)
    {
        var keys = SearchMatrix.Niches.FirstOrDefault(n => n.Label == candidate.Niche)?.Keys ?? Array.Empty<string>();
        var text = $"{user.FullName} {user.Biography} {string.Join(" ", user.LatestPosts.Take(3).Select(p => p.CaptionText))}".ToLowerInvariant();
        return keys.Length == 0 || keys.Any(k => text.Contains(k.ToLowerInvariant()));
    }

    private static Regex Re(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
}

/// <summary>Пороги квалификации (значения панели парсинга).</summary>
public sealed class ParseOptions
{
    public ContactMode ContactMode { get; set; } = ContactMode.WhatsAppFirst;
    public int MinFollowers { get; set; } = 20;
    public int MaxFollowers { get; set; } = 50000;
    public int MinMedia { get; set; } = 5;
    public int MaxPostAgeDays { get; set; } = 45;
}

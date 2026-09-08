namespace LeadHub.Core.Parser;

public sealed record MessengerInfo(
    string Telegram,
    string Whatsapp,
    string TelegramEvidence,
    string WhatsappEvidence,
    string Text,
    IReadOnlyList<string> Links);

public sealed record BioSiteInfo(bool Found, string Url, string Reason);

/// <summary>Извлечение контактов (WhatsApp/Telegram/телефон) и сайта из профиля. Порт findMessenger/detectBioSite из скилла.</summary>
public static class ContactExtractor
{
    private static readonly Regex UrlRe = new(@"https?://[^\s<>""'()\[\]]+", RegexOptions.Compiled);
    private static readonly Regex PhoneRe = new(@"(?:\+?7|8)[\s().-]*\d{3}[\s().-]*\d{3}[\s.-]*\d{2}[\s.-]*\d{2}", RegexOptions.Compiled);
    private static readonly Regex TelegramTextRe = new(@"(?:telegram|телеграм)\s*[:—-]?\s*@?([A-Za-z][A-Za-z0-9_]{4,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WhatsAppMentionRe = new(@"whats\s*app|ватсап|вотсап|wa\s*[:—-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Хосты, которые НЕ считаются сайтом бизнеса (соцсети, мессенджеры, каталоги, почта)
    private static readonly string[] AllowedProfileHosts =
    {
        "instagram.com", "t.me", "telegram.me", "wa.me", "whatsapp.com", "api.whatsapp.com",
        "vk.com", "vk.ru", "youtube.com", "youtu.be", "rutube.ru", "avito.ru", "2gis.ru",
        "yandex.ru", "yandex.com", "dzen.ru", "ok.ru", "tlgg.ru", "mail.ru", "inbox.ru", "bk.ru", "list.ru", "gmail.com",
    };

    public static string UrlHost(string url)
    {
        try
        {
            var host = new Uri(url, UriKind.Absolute).Host.ToLowerInvariant();
            return host.StartsWith("www.") ? host[4..] : host;
        }
        catch { return ""; }
    }

    public static IReadOnlyList<string> ExtractUrls(string text) =>
        UrlRe.Matches(text ?? "").Select(m => m.Value.TrimEnd('.', ',')).ToList();

    /// <summary>Нормализация телефона в 7XXXXXXXXXX. Порт normalizePhone.</summary>
    public static string NormalizePhone(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var url = new Uri(text);
                var host = UrlHost(text);
                if (host == "wa.me" && Regex.IsMatch(url.AbsolutePath, @"^/\+?7[3489]\d{9}/?$"))
                    text = url.AbsolutePath.Replace("/", "");
                else if (host is "api.whatsapp.com" or "whatsapp.com" or "web.whatsapp.com" &&
                         url.AbsolutePath.TrimEnd('/') == "/send")
                {
                    var q = System.Web.HttpUtility.ParseQueryString(url.Query);
                    if (q.AllKeys.Contains("phone") && q.GetValues("phone")!.Length == 1) text = q["phone"]!;
                    else return "";
                }
                else return "";
            }
            catch { return ""; }
        }
        if (text.Length == 0 || !Regex.IsMatch(text, @"^[+\d\s().-]+$")) return "";
        var digits = Regex.Replace(text, @"\D", "");
        if (digits.Length == 11 && digits.StartsWith('8')) digits = "7" + digits[1..];
        if (digits.Length == 10) digits = "7" + digits;
        return Regex.IsMatch(digits, @"^7[3489]\d{9}$") ? digits : "";
    }

    public static string FirstPhoneInText(string text)
    {
        var m = PhoneRe.Match(text ?? "");
        return m.Success ? NormalizePhone(m.Value) : "";
    }

    /// <summary>Порт findMessenger: Telegram/WhatsApp из ссылок профиля, bio, подписей постов, полей IG.</summary>
    public static MessengerInfo FindMessenger(IgProfile user)
    {
        var captions = string.Join("\n", user.LatestPosts.Take(3).Select(p => p.CaptionText));
        var links = new List<string>();
        links.AddRange(user.BioLinks.Where(l => !string.IsNullOrEmpty(l)));
        if (!string.IsNullOrEmpty(user.ExternalUrl)) links.Add(user.ExternalUrl);
        links.AddRange(ExtractUrls(user.Biography + "\n" + captions));

        string telegram = links.FirstOrDefault(v =>
        {
            var host = UrlHost(v);
            return host == "t.me" || host.EndsWith(".t.me") || host == "telegram.me" || host.EndsWith(".telegram.me");
        }) ?? "";
        string whatsapp = links.FirstOrDefault(v =>
            Regex.IsMatch(v, @"wa\.me/", RegexOptions.IgnoreCase) || Regex.IsMatch(v, @"(?:api\.)?whatsapp\.com/", RegexOptions.IgnoreCase)) ?? "";

        var messengerText = user.Biography + "\n" + captions;
        if (telegram == "")
        {
            var tg = TelegramTextRe.Match(messengerText);
            if (tg.Success) telegram = $"https://t.me/{tg.Groups[1].Value}";
        }
        if (whatsapp == "" && WhatsAppMentionRe.IsMatch(messengerText))
        {
            var phone = FirstPhoneInText(messengerText);
            if (phone == "") phone = NormalizePhone(user.ContactPhoneNumber);
            if (phone == "") phone = NormalizePhone(user.PublicPhoneNumber);
            if (phone != "") whatsapp = $"https://wa.me/{phone}";
        }
        if (whatsapp == "" && user.IsWhatsappLinked)
        {
            var phone = NormalizePhone(user.ContactPhoneNumber);
            if (phone == "") phone = NormalizePhone(user.PublicPhoneNumber);
            if (phone != "") whatsapp = $"https://wa.me/{phone}";
        }

        var telegramEvidence = telegram == "" ? "" : links.Contains(telegram) ? "profile_link" : "profile_text";
        var whatsappEvidence = whatsapp == ""
            ? ""
            : user.IsWhatsappLinked ? "instagram_whatsapp_linked"
            : links.Contains(whatsapp) ? "profile_link" : "profile_text";
        return new MessengerInfo(telegram, whatsapp, telegramEvidence, whatsappEvidence, messengerText, links);
    }

    /// <summary>Порт detectBioSite: есть ли в профиле сторонний сайт/лендинг (не соцсеть/каталог/мессенджер).</summary>
    public static BioSiteInfo DetectBioSite(IgProfile user, MessengerInfo messenger)
    {
        var profileLinks = user.BioLinks.Where(l => !string.IsNullOrEmpty(l)).ToList();
        if (!string.IsNullOrEmpty(user.ExternalUrl)) profileLinks.Add(user.ExternalUrl);
        foreach (var link in profileLinks)
            if (!IsAllowedProfileLink(link)) return new BioSiteInfo(true, link, "external_link_in_profile");

        foreach (var link in ExtractUrls(user.Biography + "\n" + messenger.Text))
            if (!IsAllowedProfileLink(link)) return new BioSiteInfo(true, link, "domain_in_bio_or_recent_caption");

        return new BioSiteInfo(false, "", "");
    }

    public static bool IsAllowedProfileLink(string url)
    {
        var host = UrlHost(url);
        if (host == "") return true; // не распарсилось — не считаем сайтом
        foreach (var allowed in AllowedProfileHosts)
            if (host == allowed || host.EndsWith("." + allowed)) return true;
        return false;
    }
}

namespace LeadHub.Core.Parser;

/// <summary>Сессия аккаунта Instagram: минимальный набор cookies.</summary>
public sealed class CookieSession
{
    public Dictionary<string, string> Cookies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? SessionId => Cookies.TryGetValue("sessionid", out var v) ? v : null;
    public string? CsrfToken => Cookies.TryGetValue("csrftoken", out var v) ? v : null;
    public string? DsUserId => Cookies.TryGetValue("ds_user_id", out var v) ? v : null;

    public bool IsUsable => !string.IsNullOrEmpty(SessionId) && !string.IsNullOrEmpty(CsrfToken);

    /// <summary>
    /// Разобрать cookies из строки в любом из форматов:
    /// header "sessionid=..; csrftoken=..", JSON [{name,value}...], Netscape cookies.txt.
    /// </summary>
    public static CookieSession Parse(string raw)
    {
        var result = new CookieSession();
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return result;

        if (raw.StartsWith("[") || raw.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var value = item.TryGetProperty("value", out var v) ? v.GetString() : null;
                        if (!string.IsNullOrEmpty(name)) result.Cookies[name!] = value ?? "";
                    }
                    return result;
                }
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        if (prop.Value.ValueKind == JsonValueKind.String) result.Cookies[prop.Name] = prop.Value.GetString() ?? "";
                    return result;
                }
            }
            catch (JsonException) { /* не JSON — пробуем дальше */ }
        }

        foreach (var line in raw.Split('\n'))
        {
            var l = line.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            // Netscape: domain \t include \t path \t secure \t expiry \t name \t value
            if (l.Contains('\t'))
            {
                var parts = l.Split('\t');
                if (parts.Length >= 7 && long.TryParse(parts[4], out _))
                {
                    result.Cookies[parts[5].Trim()] = parts[6].Trim();
                    continue;
                }
            }
            // header: name=value; name=value
            foreach (var pair in l.Split(';'))
            {
                var idx = pair.IndexOf('=');
                if (idx > 0)
                {
                    var name = pair[..idx].Trim();
                    var value = pair[(idx + 1)..].Trim();
                    if (name.Length > 0) result.Cookies[name] = value;
                }
            }
        }
        return result;
    }
}

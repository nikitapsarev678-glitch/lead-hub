using System.Net;

namespace LeadHub.Core.Parser;

/// <summary>Instagram отдал сигнал анти-абуза: 429/401/403/challenge/login_required/Please wait.</summary>
public sealed class IgRateLimitException(string pattern, int status) : Exception($"Instagram ограничение: {pattern} (HTTP {status})")
{
    public string Pattern { get; } = pattern;
    public int Status { get; } = status;
}

/// <summary>Клиент веб-API Instagram для одной пары (аккаунт, слот-прокси). Эндпоинты из скилла.</summary>
public sealed class IgWebClient : IDisposable
{
    private const string IgAppId = "936619743392459";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies;
    public string AccountUsername { get; }
    public int ProxyPort { get; }

    private static readonly Regex[] StopPatterns =
    {
        new(@"login_required", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"challenge_required|checkpoint_required", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"rate limit|too many requests", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"please wait|try again later", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Suspicious Activity", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    public IgWebClient(CookieSession session, string accountUsername, int proxyPort)
    {
        AccountUsername = accountUsername;
        ProxyPort = proxyPort;
        _cookies = new CookieContainer();
        foreach (var (name, value) in session.Cookies)
        {
            try { _cookies.Add(new Cookie(name, value, "/", ".instagram.com")); }
            catch (CookieException) { /* невалидное значение — пропускаем */ }
        }

        var handler = new SocketsHttpHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        };
        if (proxyPort > 0)
            handler.Proxy = new WebProxy($"socks5://127.0.0.1:{proxyPort}");

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Referrer = new Uri("https://www.instagram.com/");
        _http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _http.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        _http.DefaultRequestHeaders.Add("x-ig-app-id", IgAppId);
        _http.DefaultRequestHeaders.Add("X-ASBD-ID", "129477");
        _http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        _http.DefaultRequestHeaders.Add("Origin", "https://www.instagram.com");
        if (session.CsrfToken is { } token)
            _http.DefaultRequestHeaders.Add("X-CSRFToken", token);
    }

    /// <summary>Поиск (topsearch): кандидаты по запросу «ниша + город + контактный терм».</summary>
    public async Task<List<Candidate>> SearchTopAsync(string query, string niche, string nicheGroup, string sourceCity)
    {
        var url = $"https://www.instagram.com/api/v1/web/search/topsearch/?context=blended&query={Uri.EscapeDataString(query)}&include_reel=true";
        var body = await GetStringCheckedAsync(url);
        var candidates = new List<Candidate>();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in users.EnumerateArray())
            {
                if (!entry.TryGetProperty("user", out var u)) continue;
                var username = u.TryGetProperty("username", out var un) ? un.GetString() : null;
                if (string.IsNullOrWhiteSpace(username)) continue;

                Candidate c = new()
                {
                    Handle = username,
                    Niche = niche,
                    NicheGroup = nicheGroup,
                    SourceCity = sourceCity,
                    SourceQuery = query,
                    Title = u.TryGetProperty("full_name", out var fn) ? fn.GetString() ?? "" : "",
                    Private = u.TryGetProperty("is_private", out var priv) && priv.GetBoolean(),
                };
                if (entry.TryGetProperty("social_context", out var sc))
                {
                    var s = sc.ValueKind == JsonValueKind.String ? sc.GetString() : null;
                    c.SocialContext = s ?? "";
                    if (!string.IsNullOrEmpty(s)) c.Following = s.Contains("Подписаны", StringComparison.Ordinal) || s.Contains("Following", StringComparison.Ordinal);
                }
                candidates.Add(c);
            }
        }
        return candidates;
    }

    /// <summary>Полный публичный профиль (web_profile_info): поля + последние посты.</summary>
    public async Task<IgProfile?> FetchProfileAsync(string username)
    {
        var url = $"https://www.instagram.com/api/v1/users/web_profile_info/?username={Uri.EscapeDataString(username)}";
        var body = await GetStringCheckedAsync(url);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("user", out var u) || u.ValueKind != JsonValueKind.Object)
            return null;

        var profile = new IgProfile { Username = username };
        Str(u, "full_name", v => profile.FullName = v);
        Str(u, "biography", v => profile.Biography = v);
        Str(u, "category_name", v => profile.Category = v);
        Str(u, "category", v => { if (profile.Category == "") profile.Category = v; });
        Str(u, "external_url", v => profile.ExternalUrl = v);
        Str(u, "phone_number", v => profile.PublicPhoneNumber = v);
        Str(u, "city_name", v => profile.CityName = v);
        Str(u, "username", v => profile.Username = v);
        Num(u, "pk", v => profile.Pk = v);
        Bool(u, "is_private", v => profile.IsPrivate = v);
        Bool(u, "is_business", v => profile.IsBusiness = v);
        Num(u, "edge_followed_by", v => profile.FollowerCount = v, "count");
        if (u.TryGetProperty("edge_owner_to_timeline_media", out var media))
        {
            Num(media, "count", v => profile.MediaCount = v);
            if (media.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in edges.EnumerateArray())
                {
                    if (!edge.TryGetProperty("node", out var node)) continue;
                    var post = new IgPostItem();
                    Str(node, "shortcode", v => post.Code = v);
                    Num(node, "taken_at_timestamp", v => post.TakenAt = v);
                    Num(node, "taken_at", v => { if (post.TakenAt == 0) post.TakenAt = v; });
                    if (node.TryGetProperty("edge_media_to_caption", out var cap) &&
                        cap.TryGetProperty("edges", out var capEdges) && capEdges.GetArrayLength() > 0 &&
                        capEdges[0].TryGetProperty("node", out var capNode))
                        Str(capNode, "text", v => post.CaptionText = v);
                    profile.LatestPosts.Add(post);
                }
            }
        }
        if (u.TryGetProperty("bio_links", out var bioLinks) && bioLinks.ValueKind == JsonValueKind.Array)
            foreach (var bl in bioLinks.EnumerateArray())
                Str(bl, "url", v => { if (!string.IsNullOrEmpty(v)) profile.BioLinks.Add(v); });
        Num(u, "latest_reel_media", v => profile.LatestReelMedia = v);
        return profile;
    }

    /// <summary>embed-страница как резервный источник свежести/счётчиков (порт /handle/embed/).</summary>
    public async Task<IgProfile?> FetchEmbedAsync(string username)
    {
        var url = $"https://www.instagram.com/{Uri.EscapeDataString(username)}/embed/";
        var body = await GetStringCheckedAsync(url);

        var profile = new IgProfile { Username = username };
        var m = Regex.Match(body, @"""username""\s*:\s*""([^""\\]+)""");
        if (m.Success) profile.Username = m.Groups[1].Value;
        m = Regex.Match(body, @"""(?:followers_count|edge_followed_by)""\s*:\s*(\d+)");
        if (m.Success) profile.FollowerCount = long.Parse(m.Groups[1].Value);
        m = Regex.Match(body, @"""(?:posts_count|count)""\s*:\s*(\d+)");
        if (m.Success && profile.MediaCount == 0) profile.MediaCount = long.Parse(m.Groups[1].Value);
        foreach (Match t in Regex.Matches(body, @"""taken_at""\s*:\s*(\d{10})"))
        {
            var ts = long.Parse(t.Groups[1].Value);
            if (ts > 1500000000) profile.LatestPosts.Add(new IgPostItem { TakenAt = ts });
        }
        profile.LatestPosts = profile.LatestPosts.OrderByDescending(p => p.TakenAt).ToList();
        return profile;
    }

    /// <summary>Лёгкая проверка живости сессии: профиль ds_user_id.</summary>
    public async Task<bool> CheckSessionAliveAsync()
    {
        try
        {
            var body = await GetStringCheckedAsync("https://www.instagram.com/api/v1/users/webinfo/");
            return body.Contains("\"viewer\"", StringComparison.Ordinal);
        }
        catch (IgRateLimitException) { throw; }
        catch { return false; }
    }

    public IReadOnlyList<Candidate> ParseBraveResults(string html, string niche, string nicheGroup, string sourceCity)
    {
        var results = new List<Candidate>();
        var re = new Regex(@"\{title:""((?:\\.|[^""\\])*)"",url:""(https://(?:www\.)?instagram\.com/([A-Za-z0-9._]+)/?)"",full_title:""((?:\\.|[^""\\])*)"",description:""((?:\\.|[^""\\])*)""");
        foreach (Match m in re.Matches(html))
        {
            var handle = m.Groups[3].Value;
            if (handle.Length < 3 || ReservedPaths.Contains(handle.ToLowerInvariant())) continue;
            results.Add(new Candidate
            {
                Handle = handle,
                Niche = niche,
                NicheGroup = nicheGroup,
                SourceCity = sourceCity,
                Title = DecodeJsString(m.Groups[1].Value),
                FullTitle = DecodeJsString(m.Groups[4].Value),
                Description = DecodeJsString(m.Groups[5].Value),
            });
        }
        return results;
    }

    private static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    { "p", "reel", "reels", "stories", "explore", "accounts", "direct", "about", "developer", "developers", "privacy", "legal", "web", "challenge", "tv" };

    private async Task<string> GetStringCheckedAsync(string url)
    {
        HttpResponseMessage resp;
        try { resp = await _http.GetAsync(url); }
        catch (HttpRequestException) { throw; }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync();
            CheckStatusForRateLimit(resp.StatusCode, body);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode} для {url}");
            return body;
        }
    }

    private static void CheckStatusForRateLimit(HttpStatusCode? status, string? body)
    {
        var code = status is null ? 0 : (int)status;
        if (code is 401 or 403 or 429)
        {
            var pattern = body != null ? StopPatterns.FirstOrDefault(p => p.IsMatch(body))?.ToString() : null;
            throw new IgRateLimitException(pattern ?? $"http_{code}", code);
        }
        if (body != null) CheckBodyForRateLimit(body);
    }

    private static void CheckBodyForRateLimit(string body)
    {
        foreach (var p in StopPatterns)
            if (p.IsMatch(body))
                throw new IgRateLimitException(p.ToString(), 200);
    }

    private static string DecodeJsString(string value) =>
        value.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static void Str(JsonElement el, string prop, Action<string> set)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (s != null) set(s);
        }
    }

    private static void Num(JsonElement el, string prop, Action<long> set, string? nested = null)
    {
        if (!el.TryGetProperty(prop, out var v)) return;
        if (nested != null)
        {
            if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty(nested, out var n) && n.ValueKind == JsonValueKind.Number)
                set(n.GetInt64());
            return;
        }
        if (v.ValueKind == JsonValueKind.Number) set(v.GetInt64());
    }

    private static void Bool(JsonElement el, string prop, Action<bool> set)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
            set(v.GetBoolean());
    }

    public void Dispose() => _http.Dispose();
}

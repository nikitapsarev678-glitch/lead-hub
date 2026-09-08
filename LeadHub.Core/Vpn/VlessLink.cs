using System.Text;

namespace LeadHub.Core.Vpn;

public static class VlessLink
{
    private static readonly Regex LinkRe = new(@"vless://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Извлечь все vless:// ссылки из произвольного текста.</summary>
    public static IReadOnlyList<string> ExtractLinks(string text) =>
        LinkRe.Matches(text ?? "").Select(m => m.Value.TrimEnd(',', ';', ')')).ToList();

    /// <summary>Распарсить одну vless:// ссылку. Кидает FormatException с внятным сообщением.</summary>
    public static VpnNode Parse(string link)
    {
        link = link.Trim();
        if (!link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Ссылка не vless://: " + Trunc(link));

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new FormatException("Не удалось разобрать ссылку: " + Trunc(link));

        var q = System.Web.HttpUtility.ParseQueryString(uri.Query.Length > 0 ? uri.Query.Substring(1) : "");
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#')).Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = $"{uri.Host}:{uri.Port}";

        var node = new VpnNode
        {
            RawLink = link,
            Name = name,
            Server = uri.Host,
            Port = uri.Port < 0 ? 443 : uri.Port,
            Uuid = Uri.UnescapeDataString(uri.UserInfo),
            Flow = q["flow"] ?? "",
            Security = (q["security"] ?? "tls").ToLowerInvariant(),
            Sni = q["sni"] ?? q["peer"] ?? "",
            Fingerprint = string.IsNullOrEmpty(q["fp"]) ? "chrome" : q["fp"]!,
            PublicKey = q["pbk"] ?? "",
            ShortId = q["sid"] ?? "",
            Transport = (q["type"] ?? "tcp").ToLowerInvariant(),
            Path = Uri.UnescapeDataString(q["path"] ?? "").Replace("\\", ""),
            HostHeader = Uri.UnescapeDataString(q["host"] ?? ""),
            ServiceName = q["serviceName"] ?? "",
        };
        if (string.IsNullOrEmpty(node.Sni)) node.Sni = node.Server;
        if (node.Uuid.Length != 36) throw new FormatException($"UUID некорректный в узле «{name}»: {Trunc(node.Uuid)}");
        return node;
    }

    /// <summary>Собрать узел обратно в vless:// ссылку (для экспорта).</summary>
    public static string ToLink(VpnNode n)
    {
        var q = new List<string>();
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) q.Add($"{key}={Uri.EscapeDataString(value!)}"); }
        Add("encryption", "none");
        Add("flow", n.Flow);
        Add("security", n.Security);
        Add("sni", n.Sni);
        Add("fp", n.Fingerprint);
        Add("pbk", n.PublicKey);
        Add("sid", n.ShortId);
        Add("type", n.Transport);
        Add("path", n.Path);
        Add("host", n.HostHeader);
        Add("serviceName", n.ServiceName);
        var query = q.Count > 0 ? "?" + string.Join("&", q) : "";
        var frag = string.IsNullOrEmpty(n.Name) ? "" : "#" + Uri.EscapeDataString(n.Name);
        return $"vless://{n.Uuid}@{n.Server}:{n.Port}{query}{frag}";
    }

    /// <summary>Дедупликация: по Server:Port:Uuid.</summary>
    public static string DedupKey(VpnNode n) => $"{n.Server}:{n.Port}:{n.Uuid}";

    private static string Trunc(string s) => s.Length <= 60 ? s : s[..60] + "…";
}

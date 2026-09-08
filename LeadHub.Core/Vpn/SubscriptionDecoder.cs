namespace LeadHub.Core.Vpn;

public sealed record SubscriptionResult(IReadOnlyList<VpnNode> Nodes, IReadOnlyList<string> Errors);

public static class SubscriptionDecoder
{
    /// <summary>
    /// Разобрать вставленный текст или содержимое подписки: строки vless://, целиком base64,
    /// base64 построчно. Некорректные ссылки попадают в Errors; если ссылок нет совсем — тоже.
    /// </summary>
    public static SubscriptionResult Decode(string content)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content)) return new(new List<VpnNode>(), new List<string> { "Пустой ввод" });

        var links = VlessLink.ExtractLinks(content).ToList();
        if (links.Count == 0)
        {
            var decoded = TryBase64(content);
            if (decoded != null)
            {
                links = VlessLink.ExtractLinks(decoded).ToList();
                if (links.Count == 0)
                {
                    // построчный base64
                    var sb = new StringBuilder();
                    foreach (var line in decoded.Split('\n'))
                    {
                        var l2 = TryBase64(line.Trim());
                        if (l2 != null) sb.AppendLine(l2);
                        else if (line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) sb.AppendLine(line);
                    }
                    links = VlessLink.ExtractLinks(sb.ToString()).ToList();
                }
            }
        }
        if (links.Count == 0)
        {
            errors.Add("vless:// ссылки не найдены (это не подписка VLESS и не список ссылок).");
            return new(new List<VpnNode>(), errors);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<VpnNode>();
        foreach (var link in links)
        {
            try
            {
                var node = VlessLink.Parse(link);
                if (seen.Add(VlessLink.DedupKey(node))) nodes.Add(node);
            }
            catch (FormatException ex)
            {
                errors.Add(ex.Message);
            }
        }
        return new(nodes, errors);
    }

    private static string? TryBase64(string text)
    {
        var t = text.Trim().Replace("-", "+").Replace("_", "/").Replace("\r", "").Replace("\n", "").Replace(" ", "");
        if (t.Length < 8 || t.Length % 4 == 1 || !t.All(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=')) return null;
        var pad = (4 - t.Length % 4) % 4;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(t + new string('=', pad))); }
        catch { return null; }
    }
}

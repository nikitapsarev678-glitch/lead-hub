using System.Net.Http.Json;

namespace LeadHub.Core.Vpn;

public sealed record ClashProxyInfo(string Name, string Type, string? Now, List<string> All);

/// <summary>Клиент Clash API sing-box: пинги (delay), переключение selector, список прокси.</summary>
public sealed class ClashApiClient : IDisposable
{
    private readonly HttpClient _http;

    public ClashApiClient(int port = 9090)
    {
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<bool> IsAliveAsync()
    {
        try
        {
            using var resp = await _http.GetAsync("version");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Пинг узла в мс через тест задержки до URL. null — узел недоступен.</summary>
    public async Task<int?> TestDelayAsync(string proxyName, string testUrl = "http://www.gstatic.com/generate_204", int timeoutMs = 5000)
    {
        try
        {
            var url = $"proxies/{Uri.EscapeDataString(proxyName)}/delay?timeout={timeoutMs}&url={Uri.EscapeDataString(testUrl)}";
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadFromJsonAsync<DelayResponse>();
            return json?.Delay;
        }
        catch { return null; }
    }

    /// <summary>Переключить selector слота на узел (без рестарта sing-box).</summary>
    public async Task<bool> SelectAsync(string selectorTag, string nodeName)
    {
        try
        {
            var url = $"proxies/{Uri.EscapeDataString(selectorTag)}";
            using var resp = await _http.PutAsJsonAsync(url, new { name = nodeName });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Текущее состояние selector'ов и всех прокси.</summary>
    public async Task<Dictionary<string, ClashProxyInfo>> GetProxiesAsync()
    {
        try
        {
            using var resp = await _http.GetAsync("proxies");
            if (!resp.IsSuccessStatusCode) return new();
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var result = new Dictionary<string, ClashProxyInfo>();
            if (doc.RootElement.TryGetProperty("proxies", out var proxies))
            {
                foreach (var prop in proxies.EnumerateObject())
                {
                    var type = prop.Value.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    var now = prop.Value.TryGetProperty("now", out var nw) ? nw.GetString() : null;
                    var all = new List<string>();
                    if (prop.Value.TryGetProperty("all", out var allEl))
                        foreach (var a in allEl.EnumerateArray()) all.Add(a.GetString() ?? "");
                    result[prop.Name] = new ClashProxyInfo(prop.Name, type, now, all);
                }
            }
            return result;
        }
        catch { return new(); }
    }

    public void Dispose() => _http.Dispose();

    private sealed record DelayResponse(int Delay);
}

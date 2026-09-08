namespace LeadHub.Core.Vpn;

/// <summary>Узел VPN, импортированный из vless:// ссылки.</summary>
public sealed class VpnNode
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Server { get; set; } = "";
    public int Port { get; set; }
    public string Uuid { get; set; } = "";
    public string Flow { get; set; } = "";
    /// <summary>tls | reality | none</summary>
    public string Security { get; set; } = "tls";
    public string Sni { get; set; } = "";
    public string Fingerprint { get; set; } = "chrome";
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";
    /// <summary>tcp | ws | grpc | http</summary>
    public string Transport { get; set; } = "tcp";
    public string Path { get; set; } = "";
    public string HostHeader { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string RawLink { get; set; } = "";

    // Runtime / DB fields
    public string CountryCode { get; set; } = "";
    public string CountryName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int UseCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int LastLatencyMs { get; set; } = -1;
    public DateTime? LastCheckedAt { get; set; }
    public bool AddedFromSubscription { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public string Flag => string.IsNullOrEmpty(CountryCode) || CountryCode.Length != 2
        ? "🌐"
        : string.Concat(CountryCode.ToUpperInvariant().Select(c => char.ConvertFromUtf32(0x1F1E6 + c - 'A')));

    public string TransportLabel =>
        Transport == "ws" ? "VLESS · WS" :
        Transport == "grpc" ? "VLESS · gRPC" :
        Security == "reality" ? "VLESS · TCP · Reality" : "VLESS · TCP";

    public bool IsHealthy => Enabled && LastLatencyMs > 0;

    public override string ToString() => $"{Name} ({Server}:{Port})";
}

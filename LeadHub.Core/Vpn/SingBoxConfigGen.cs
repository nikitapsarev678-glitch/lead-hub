using System.Text.Json;

namespace LeadHub.Core.Vpn;

/// <summary>Модель слота: независимый исходящий узел + локальный порт SOCKS5.</summary>
public sealed class VpnSlot
{
    public int Index { get; init; }          // 1..N
    public int Port { get; init; }           // 1080 + Index - 1
    public string? AssignedNodeTag { get; set; }
    public long? AssignedNodeId { get; set; }
}

/// <summary>
/// Генератор конфига sing-box (≥ 1.11): mixed-inbound на каждый слот, vless-outbound на каждый узел,
/// selector на каждый слот, route-правила inbound→selector, clash_api.
/// Один процесс sing-box даёт каждому слоту свой IP, переключение узла — через Clash API без рестарта.
/// </summary>
public static class SingBoxConfigGen
{
    public static string NodeTag(long id) => $"node-{id}";
    public static string SelectorTag(int slot) => $"slot-{slot}";
    public static string InboundTag(int slot) => $"in-slot-{slot}";

    public static string Generate(
        IReadOnlyList<VpnNode> nodes,
        IReadOnlyList<VpnSlot> slots,
        int clashApiPort = 9090)
    {
        if (nodes.Count == 0) throw new InvalidOperationException("Нет VPN-узлов для генерации конфига");
        if (slots.Count == 0) throw new InvalidOperationException("Нет слотов для генерации конфига");

        var outboundTags = nodes.Select(n => NodeTag(n.Id)).ToList();

        var config = new Dictionary<string, object>
        {
            ["log"] = new Dictionary<string, object> { ["level"] = "warn", ["timestamp"] = true },
            ["inbounds"] = slots.Select(s => (object)new Dictionary<string, object>
            {
                ["type"] = "mixed",
                ["tag"] = InboundTag(s.Index),
                ["listen"] = "127.0.0.1",
                ["port"] = s.Port,
            }).ToList(),
            ["outbounds"] = BuildOutbounds(nodes, slots, outboundTags),
            ["route"] = new Dictionary<string, object>
            {
                ["rules"] = slots.Select(s => (object)new Dictionary<string, object>
                {
                    ["inbound"] = new List<string> { InboundTag(s.Index) },
                    ["action"] = "route",
                    ["outbound"] = SelectorTag(s.Index),
                }).ToList(),
            },
            ["experimental"] = new Dictionary<string, object>
            {
                ["clash_api"] = new Dictionary<string, object>
                {
                    ["external_controller"] = $"127.0.0.1:{clashApiPort}",
                    ["default_mode"] = "global",
                },
                ["cache_file"] = new Dictionary<string, object> { ["enabled"] = true },
            },
        };

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonSerializer.Serialize(config, options);
    }

    private static List<object> BuildOutbounds(IReadOnlyList<VpnNode> nodes, IReadOnlyList<VpnSlot> slots, List<string> outboundTags)
    {
        var result = new List<object>();
        foreach (var n in nodes) result.Add(BuildNodeOutbound(n));

        var direct = new Dictionary<string, object> { ["type"] = "direct", ["tag"] = "direct" };
        var selectorPool = outboundTags.Append("direct").ToList();
        foreach (var s in slots)
        {
            var def = s.AssignedNodeTag != null && selectorPool.Contains(s.AssignedNodeTag)
                ? s.AssignedNodeTag
                : outboundTags[0];
            result.Add(new Dictionary<string, object>
            {
                ["type"] = "selector",
                ["tag"] = SelectorTag(s.Index),
                ["outbounds"] = selectorPool,
                ["default"] = def,
                ["interrupt_exist_connections"] = false,
            });
        }
        result.Add(direct);
        return result;
    }

    private static Dictionary<string, object> BuildNodeOutbound(VpnNode n)
    {
        var ob = new Dictionary<string, object>
        {
            ["type"] = "vless",
            ["tag"] = NodeTag(n.Id),
            ["server"] = n.Server,
            ["server_port"] = n.Port,
            ["uuid"] = n.Uuid,
        };
        if (!string.IsNullOrEmpty(n.Flow)) ob["flow"] = n.Flow;

        var sec = n.Security == "reality" || n.Security == "tls" ? n.Security : "tls";
        var tls = new Dictionary<string, object>
        {
            ["enabled"] = true,
            ["server_name"] = string.IsNullOrEmpty(n.Sni) ? n.Server : n.Sni,
            ["insecure"] = false,
            ["utls"] = new Dictionary<string, object> { ["enabled"] = true, ["fingerprint"] = string.IsNullOrEmpty(n.Fingerprint) ? "chrome" : n.Fingerprint },
        };
        if (sec == "reality" && !string.IsNullOrEmpty(n.PublicKey))
        {
            tls["reality"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["public_key"] = n.PublicKey,
                ["short_id"] = n.ShortId ?? "",
            };
        }
        ob["tls"] = tls;

        switch (n.Transport)
        {
            case "ws":
            {
                var ws = new Dictionary<string, object> { ["type"] = "ws" };
                if (!string.IsNullOrEmpty(n.Path)) ws["path"] = n.Path;
                if (!string.IsNullOrEmpty(n.HostHeader))
                    ws["headers"] = new Dictionary<string, object> { ["Host"] = n.HostHeader };
                ob["transport"] = ws;
                break;
            }
            case "grpc":
                ob["transport"] = new Dictionary<string, object>
                {
                    ["type"] = "grpc",
                    ["service_name"] = n.ServiceName ?? "",
                };
                break;
            default: // tcp — транспорт не указываем
                break;
        }
        return ob;
    }
}

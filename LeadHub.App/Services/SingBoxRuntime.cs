using System.Diagnostics;
using System.IO;
using LeadHub.Core.Store;
using LeadHub.Core.Vpn;

namespace LeadHub.App.Services;

/// <summary>
/// Управление процессом sing-box: генерация конфига, запуск, стоп, пинги через Clash API,
/// переключение узлов слотов на лету.
/// </summary>
public sealed class SingBoxRuntime : IDisposable
{
    private readonly AppState _state;
    private Process? _process;
    private string? _exePath;

    public const int BaseProxyPort = 10801;
    public const int ClashApiPort = 9090;
    public const int MaxSlots = 8;

    public ClashApiClient Clash { get; } = new(ClashApiPort);
    public bool IsRunning => _process is { HasExited: false };
    public string StatusText { get; private set; } = "sing-box не запущен";

    public event Action<string>? StatusChanged;

    public SingBoxRuntime(AppState state) => _state = state;

    public static List<VpnSlot> DefaultSlots() =>
        Enumerable.Range(1, MaxSlots).Select(i => new VpnSlot { Index = i, Port = BaseProxyPort + i - 1 }).ToList();

    /// <summary>Запустить sing-box с текущими включёнными узлами. Нужно ≥1 узел.</summary>
    public async Task StartAsync()
    {
        var nodes = _state.Vpns.Enabled().ToList();
        if (nodes.Count == 0)
            throw new InvalidOperationException("Нет включённых VPN-узлов — сначала импортируйте ссылки на вкладке «VPN».");

        _exePath = AppPaths.FindSingBox(_state.Settings.Get("singbox_path"));
        if (_exePath == null)
            throw new InvalidOperationException(
                "sing-box.exe не найден. Положите его в tools/sing-box/ рядом с программой или укажите путь в настройках.");

        if (IsRunning) await StopAsync();

        var slots = SlotsFromSettings();
        var config = SingBoxConfigGen.Generate(nodes, slots, ClashApiPort);
        await File.WriteAllTextAsync(AppPaths.SingBoxConfig, config);

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            Arguments = $"run -c \"{AppPaths.SingBoxConfig}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        _process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить sing-box");
        _process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) LogStatus($"[sing-box] {line}"); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) LogStatus($"[sing-box] {line}"); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // ждём Clash API
        for (var i = 0; i < 40; i++)
        {
            if (IsRunning && await Clash.IsAliveAsync()) break;
            if (!IsRunning) throw new InvalidOperationException($"sing-box завершился с кодом {_process.ExitCode}. Проверьте узлы (лог в настройках).");
            await Task.Delay(250);
        }
        if (!await Clash.IsAliveAsync())
        {
            await StopAsync();
            throw new InvalidOperationException("Clash API не ответил за 10 секунд — sing-box не поднялся.");
        }
        StatusText = $"sing-box работает · узлов: {nodes.Count} · слотов: {slots.Count}";
        LogStatus(StatusText);
    }

    public async Task StopAsync()
    {
        if (_process is { HasExited: false } p)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* уже завершён */ }
            try { await p.WaitForExitAsync(); } catch { /* уже завершён */ }
        }
        _process = null;
        StatusText = "sing-box остановлен";
        LogStatus(StatusText);
    }

    /// <summary>Пропинговать все узлы (мс). null = узел недоступен.</summary>
    public async Task<Dictionary<long, int?>> PingAllAsync(IReadOnlyList<VpnNode> nodes, string testUrl = "http://www.gstatic.com/generate_204")
    {
        var result = new Dictionary<long, int?>();
        var batches = nodes.Chunk(12);
        foreach (var batch in batches)
        {
            var tasks = batch.Select(async n => (n.Id, delay: await Clash.TestDelayAsync(SingBoxConfigGen.NodeTag(n.Id), testUrl)));
            foreach (var (id, delay) in await Task.WhenAll(tasks)) result[id] = delay;
        }
        foreach (var n in nodes)
        {
            n.LastLatencyMs = result.TryGetValue(n.Id, out var d) && d != null ? d.Value : -1;
            n.LastCheckedAt = DateTime.UtcNow;
            _state.Vpns.UpdateLatency(n);
        }
        LogStatus($"Пинг проверен: {nodes.Count(n => n.IsHealthy)} из {nodes.Count} узлов живые.");
        return result;
    }

    /// <summary>Назначить узел слоту (без перезапуска sing-box).</summary>
    public async Task<bool> AssignSlotAsync(int slotIndex, VpnNode node)
    {
        if (!IsRunning) return false;
        var ok = await Clash.SelectAsync(SingBoxConfigGen.SelectorTag(slotIndex), SingBoxConfigGen.NodeTag(node.Id));
        if (ok) LogStatus($"Слот {slotIndex} → {node.Flag} {node.Name} (порт {BaseProxyPort + slotIndex - 1}).");
        return ok;
    }

    private List<VpnSlot> SlotsFromSettings()
    {
        var slots = DefaultSlots();
        // восстановить назначенные узлы из настроек slot1..slot8
        foreach (var s in slots)
        {
            if (long.TryParse(_state.Settings.Get($"slot{s.Index}_node"), out var nodeId))
                s.AssignedNodeId = nodeId;
            if (nodeId > 0)
            {
                var node = _state.Vpns.All().FirstOrDefault(n => n.Id == nodeId);
                if (node != null) s.AssignedNodeTag = SingBoxConfigGen.NodeTag(node.Id);
            }
        }
        return slots;
    }

    public void SaveSlotAssignment(int slotIndex, long nodeId)
    {
        _state.Settings.Set($"slot{slotIndex}_node", nodeId.ToString());
        var node = _state.Vpns.All().FirstOrDefault(n => n.Id == nodeId);
        if (node != null) LogStatus($"Слот {slotIndex} закреплён за {node.Name}.");
    }

    private void LogStatus(string message)
    {
        StatusText = message;
        StatusChanged?.Invoke(message);
    }

    public void Dispose()
    {
        try { if (IsRunning) _process?.Kill(entireProcessTree: true); } catch { /* завершение */ }
        Clash.Dispose();
    }
}

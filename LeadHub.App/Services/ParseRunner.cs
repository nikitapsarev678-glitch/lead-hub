using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LeadHub.Core.Parser;
using LeadHub.Core.Store;

namespace LeadHub.App.Services;

/// <summary>Мост между ParseEngine и UI: прогон в фоне, события — в UI-поток.</summary>
public sealed class ParseRunner : IDisposable
{
    private readonly AppState _state;
    private readonly SingBoxRuntime _vpn;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public bool IsRunning => _task is { IsCompleted: false };
    public ObservableCollection<WorkerProgress> Workers { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public long TotalLeads { get; private set; }
    public long TotalValidated { get; private set; }

    public event Action? RunFinished;
    public event Action? CountsChanged;

    public ParseRunner(AppState state, SingBoxRuntime vpn)
    {
        _state = state;
        _vpn = vpn;
    }

    public void Start(ParsePreset preset)
    {
        if (IsRunning) throw new InvalidOperationException("Прогон уже идёт — сначала остановите его.");
        if (!_vpn.IsRunning) throw new InvalidOperationException("Сначала запустите sing-box на вкладке «VPN».");

        Workers.Clear();
        LogLines.Clear();
        for (var i = 0; i < preset.Workers; i++) Workers.Add(new WorkerProgress { SlotIndex = i + 1, State = "ожидание" });
        TotalLeads = 0;
        TotalValidated = 0;

        var engine = new ParseEngine(_state.Db, _state.Rotator, _state.ResolveSession);
        var dispatcher = Application.Current.Dispatcher;

        engine.Progress += p => _ = dispatcher.BeginInvoke(() =>
        {
            var idx = p.SlotIndex - 1;
            if (idx >= 0 && idx < Workers.Count)
            {
                Workers[idx] = p;
            }
        });
        engine.Log += line => _ = dispatcher.BeginInvoke(() =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (LogLines.Count > 800) LogLines.RemoveAt(0);
        });
        engine.LeadSaved += lead => _ = dispatcher.BeginInvoke(() => { TotalLeads++; CountsChanged?.Invoke(); });

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var basePort = SingBoxRuntime.BaseProxyPort;

        _task = Task.Run(async () =>
        {
            try
            {
                var summary = await engine.RunAsync(preset, basePort, ct);
                _ = dispatcher.BeginInvoke(() =>
                {
                    TotalValidated = summary.Validated;
                    LogLines.Add($"[{DateTime.Now:HH:mm:ss}] Итог: лидов {summary.Leads}, профилей проверено {summary.Validated}. Причина: {summary.StopReason}.");
                });
            }
            catch (Exception ex)
            {
                _ = dispatcher.BeginInvoke(() => LogLines.Add($"[{DateTime.Now:HH:mm:ss}] Ошибка прогона: {ex.Message}"));
            }
            finally
            {
                _ = dispatcher.BeginInvoke(() =>
                {
                    foreach (var w in Workers) if (w.State != "stopped") w.State = "готово";
                    RunFinished?.Invoke();
                });
            }
        }, ct);
        _ = _task;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); }
        catch { /* уже отменён */ }
    }

    public void Dispose() => Stop();
}

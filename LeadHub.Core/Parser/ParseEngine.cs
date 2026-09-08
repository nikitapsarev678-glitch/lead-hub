using System.Collections.Concurrent;
using LeadHub.Core.Rotation;
using LeadHub.Core.Store;

namespace LeadHub.Core.Parser;

public sealed class WorkerProgress
{
    public int SlotIndex { get; set; }
    public string Account { get; set; } = "";
    public string VpnLabel { get; set; } = "";
    public long Discovered { get; set; }
    public long Validated { get; set; }
    public long Qualified { get; set; }
    public long Leads { get; set; }
    public string CurrentQuery { get; set; } = "";
    public string State { get; set; } = "idle"; // running | cooldown | waiting | stopped | done

    public WorkerProgress Clone() => new()
    {
        SlotIndex = SlotIndex, Account = Account, VpnLabel = VpnLabel,
        Discovered = Discovered, Validated = Validated, Qualified = Qualified, Leads = Leads,
        CurrentQuery = CurrentQuery, State = State,
    };
}

public sealed class RunSummary
{
    public long RunId { get; set; }
    public long Discovered { get; set; }
    public long Validated { get; set; }
    public long Qualified { get; set; }
    public long Leads { get; set; }
    public string StopReason { get; set; } = "";
}

/// <summary>
/// Движок парсинга: discovery (topsearch) → валидация (web_profile_info + embed) → квалификация
/// (порты скилла) → лиды в БД. Пейсинг из скилла, авто-ротация аккаунтов/VPN при лимитах Instagram.
/// </summary>
public sealed class ParseEngine(Db db, Rotator rotator, Func<long, CookieSession?> sessionResolver)
{
    private readonly LeadRepository _leads = new(db);
    private readonly ExclusionRepository _exclusions = new(db);
    private readonly UsageRepository _usage = new(db);
    private readonly RunRepository _runs = new(db);

    public event Action<WorkerProgress>? Progress;
    public event Action<string>? Log;
    public event Action<LeadRecord>? LeadSaved;

    // Общие счётчики прогонa
    private long _discoveredTotal, _validatedTotal, _qualifiedTotal, _leadsTotal;
    private readonly ConcurrentDictionary<string, byte> _seenHandles = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RunSummary> RunAsync(ParsePreset preset, int baseProxyPort, CancellationToken ct)
    {
        var runId = _runs.Start($"Прогон {DateTime.Now:dd.MM HH:mm}", preset);
        Log?.Invoke($"Прогон #{runId}: цель {preset.TargetLeads} лидов · режим {preset.ContactMode} · воркеров {preset.Workers} · скорость {preset.Speed}.");

        if (preset.DedupAgainstHistory)
        {
            foreach (var lead in _leads.All()) _seenHandles.TryAdd(lead.Handle, 0);
            foreach (var h in _exclusions.All().Handles) _seenHandles.TryAdd(h, 0);
            Log?.Invoke($"Дедупликация: {_seenHandles.Count} известных handles исключены.");
        }

        var queries = BuildQueryPlan(preset);
        var queue = new ConcurrentQueue<SearchTask>();
        foreach (var q in queries) queue.Enqueue(q);
        Log?.Invoke($"План запросов: {queries.Count} (ниша × город × формулировка × контактный терм).");

        var (discPause, discJitter, valDelay, valJitter, _) = preset.Pacing();
        var random = new Random();

        var workers = Enumerable.Range(1, preset.Workers).ToList();
        await Task.WhenAll(workers.Select(slot => Task.Run(() =>
            WorkerLoopAsync(slot, runId, preset, queue, discPause, discJitter, valDelay, valJitter, baseProxyPort, random, ct), ct)));

        var reachedTarget = Interlocked.Read(ref _leadsTotal) >= preset.TargetLeads;
        var stopReason = ct.IsCancellationRequested ? "остановлен вручную"
            : reachedTarget ? "цель достигнута" : "очередь запросов исчерпана";
        _runs.Finish(runId, ct.IsCancellationRequested ? "stopped" : reachedTarget ? "finished" : "stopped", stopReason);
        Log?.Invoke($"Прогон завершён: лидов {Interlocked.Read(ref _leadsTotal)}, профилей проверено {Interlocked.Read(ref _validatedTotal)}. Причина: {stopReason}.");

        return new RunSummary
        {
            RunId = runId,
            Discovered = Interlocked.Read(ref _discoveredTotal),
            Validated = Interlocked.Read(ref _validatedTotal),
            Qualified = Interlocked.Read(ref _qualifiedTotal),
            Leads = Interlocked.Read(ref _leadsTotal),
            StopReason = stopReason,
        };
    }

    // ——— План запросов ———

    public sealed record SearchTask(string Query, string Niche, string NicheGroup, string City);

    public static List<SearchTask> BuildQueryPlan(ParsePreset preset)
    {
        var cities = SearchMatrix.Cities
            .Where(c => preset.CityTiers.Contains(c.Tier))
            .Where(c => preset.SelectedCities.Count == 0 || preset.SelectedCities.Contains(c.Name))
            .Select(c => c.Name)
            .ToList();

        var niches = preset.SelectedNiches
            .Select(label => SearchMatrix.Niches.FirstOrDefault(n => n.Label == label))
            .Where(n => n != null!)
            .Cast<Niche>()
            .ToList();

        var terms = ContactQueryTerms.For(preset.ContactMode);
        var tasks = new List<SearchTask>();
        var wave1 = cities.Where(c => SearchMatrix.Cities.First(x => x.Name == c).Tier == 1).ToList();
        var wave2 = cities.Where(c => SearchMatrix.Cities.First(x => x.Name == c).Tier != 1).ToList();

        foreach (var cityWave in new[] { wave1, wave2 })
        {
            foreach (var city in cityWave)
            {
                foreach (var niche in niches)
                    foreach (var phrase in niche.Phrases)
                        foreach (var term in terms.Take(3))
                            tasks.Add(new SearchTask($"{phrase} {city} {term}", niche.Label, niche.Group, city));
                foreach (var keyword in preset.CustomKeywords)
                    foreach (var term in terms.Take(2))
                        tasks.Add(new SearchTask($"{keyword} {city} {term}", keyword, "Свои ключевые слова", city));
            }
        }
        return tasks.OrderBy(t => Guid.NewGuid()).ToList(); // перемешать, чтобы ниши не шли блоками
    }

    // ——— Воркер ———

    private async Task WorkerLoopAsync(int slot, long runId, ParsePreset preset, ConcurrentQueue<SearchTask> queue,
        int discPause, int discJitter, int valDelay, int valJitter, int baseProxyPort, Random random, CancellationToken ct)
    {
        var progress = new WorkerProgress { SlotIndex = slot, State = "running" };
        var report = () =>
        {
            Progress?.Invoke(progress.Clone());
            return true;
        };
        var qualifier = new Qualifier(preset.ToOptions());
        var noPairRetries = 0;

        while (!ct.IsCancellationRequested && Interlocked.Read(ref _leadsTotal) < preset.TargetLeads)
        {
            var pair = rotator.ChoosePair(slot, baseProxyPort + slot - 1, preset.DailyRequestCapPerAccount,
                preset.AutoRotate ? null : preset.ManualAccountIds,
                preset.AutoRotate ? null : preset.ManualVpnNodeIds);
            if (pair == null)
            {
                // Все аккаунты в кулдауне/капе — подождать и повторить (до ~1 часа), потом остановиться.
                noPairRetries++;
                if (noPairRetries > 60)
                {
                    progress.State = "stopped";
                    report();
                    Log?.Invoke($"Слот {slot}: аккаунты исчерпаны (кулдаун/капы) — воркер остановлен.");
                    return;
                }
                progress.State = "waiting";
                report();
                await SafeDelay(60_000, ct);
                continue;
            }
            noPairRetries = 0;

            var session = sessionResolver(pair.Account.Id);
            if (session == null || !session.IsUsable)
            {
                Log?.Invoke($"Слот {slot}: у аккаунта @{pair.Account.Username} нет валидной сессии — кулдаун 24ч.");
                rotator.SetCooldown(pair.Account, TimeSpan.FromHours(24));
                rotator.ReleasePair(pair);
                continue;
            }

            progress.Account = "@" + pair.Account.Username;
            progress.VpnLabel = $"{pair.VpnNode.Flag} {pair.VpnNode.Name}";
            progress.State = "running";
            report();

            using var client = new IgWebClient(session, pair.Account.Username, pair.ProxyPort);
            var networkFailures = 0;
            var usageCounter = 0;
            var rotated = false;

            while (!ct.IsCancellationRequested && Interlocked.Read(ref _leadsTotal) < preset.TargetLeads)
            {
                if (!queue.TryDequeue(out var task))
                {
                    progress.State = "done";
                    report();
                    rotator.ReleasePair(pair);
                    Log?.Invoke($"Слот {slot}: очередь запросов исчерпана.");
                    return;
                }
                progress.CurrentQuery = task.Query;
                report();

                try
                {
                    var found = await client.SearchTopAsync(task.Query, task.Niche, task.NicheGroup, task.City);
                    usageCounter++;
                    Interlocked.Add(ref _discoveredTotal, found.Count);
                    progress.Discovered += found.Count;
                    report();

                    foreach (var candidate in found)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (candidate.Private || candidate.Following || candidate.Handle.Length < 3) continue;
                        if (!_seenHandles.TryAdd(candidate.Handle, 0)) continue;
                        if (Qualifier.PersonalRejectRe.IsMatch($"{candidate.Handle.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ')} {candidate.Title} {candidate.FullTitle}")) continue;

                        IgProfile? profile;
                        try
                        {
                            profile = await client.FetchProfileAsync(candidate.Handle);
                            usageCounter++;
                        }
                        catch (IgRateLimitException) { throw; }
                        catch
                        {
                            try { profile = await client.FetchEmbedAsync(candidate.Handle); usageCounter++; }
                            catch (IgRateLimitException) { throw; }
                            catch { networkFailures++; continue; }
                        }
                        if (profile == null) continue;

                        Interlocked.Increment(ref _validatedTotal);
                        progress.Validated++;
                        report();

                        var result = qualifier.Qualify(candidate, profile, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        if (!result.QualifiedPreSite) continue;

                        Interlocked.Increment(ref _qualifiedTotal);
                        progress.Qualified++;

                        var lead = BuildLead(runId, candidate, profile, result, preset);
                        if (lead != null)
                        {
                            _leads.Insert(lead);
                            Interlocked.Increment(ref _leadsTotal);
                            progress.Leads++;
                            Log?.Invoke($"ЛИД: {lead.FullName} · @{lead.Handle} · {lead.City} · {lead.Niche} · {(lead.WhatsappUrl.Length > 0 ? "WhatsApp ✅" : "Telegram ✅")}");
                            LeadSaved?.Invoke(lead);
                        }

                        if (usageCounter >= 50)
                        {
                            _usage.Record(pair.Account.Id, pair.VpnNode.Id, runId, usageCounter);
                            usageCounter = 0;
                        }

                        await DelayWithJitterAsync(valDelay, valJitter, random, ct);
                    }
                    networkFailures = 0;
                }
                catch (IgRateLimitException ex)
                {
                    Log?.Invoke($"Слот {slot}: ограничение Instagram ({ex.Message}). @{pair.Account.Username} → кулдаун {preset.CooldownMinutes} мин, ротация пары.");
                    if (usageCounter > 0) { _usage.Record(pair.Account.Id, pair.VpnNode.Id, runId, usageCounter); usageCounter = 0; }
                    rotator.SetCooldown(pair.Account, TimeSpan.FromMinutes(preset.CooldownMinutes));
                    rotator.ReleasePair(pair);
                    rotated = true;
                    progress.State = "cooldown";
                    report();
                    queue.Enqueue(task);
                    break;
                }
                catch (Exception ex)
                {
                    networkFailures++;
                    Log?.Invoke($"Слот {slot}: сетевая ошибка ({ex.Message}).");
                    if (networkFailures >= 3)
                    {
                        rotator.ReleasePair(pair);
                        progress.State = "stopped";
                        report();
                        return;
                    }
                    await SafeDelay(60_000, ct);
                }

                await DelayWithJitterAsync(discPause, discJitter, random, ct);
            }

            if (!rotated) rotator.ReleasePair(pair);
        }

        progress.State = "done";
        report();
    }

    private static async Task DelayWithJitterAsync(int baseMs, int jitterMs, Random random, CancellationToken ct) =>
        await SafeDelay(baseMs + random.Next(0, Math.Max(1, jitterMs)), ct);

    private static async Task SafeDelay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); }
        catch (TaskCanceledException) { }
    }

    /// <summary>Сборка лида из прошедшего квалификацию профиля по режиму контактов.</summary>
    internal static LeadRecord? BuildLead(long runId, Candidate candidate, IgProfile profile, QualificationResult result, ParsePreset preset)
    {
        var whatsapp = result.Messenger.Whatsapp;
        var telegram = result.Messenger.Telegram;
        var phone = result.PublicPhone;

        var whatsappStatus = whatsapp.Length > 0
            ? (result.Messenger.WhatsappEvidence == "profile_link" ? "published" : "published_profile_phone")
            : "missing_public_link";

        var hasWa = whatsapp.Length > 0 || phone.Length > 0;
        var hasTg = telegram.Length > 0;
        var section = preset.ContactMode switch
        {
            ContactMode.WhatsAppOnly => hasWa ? LeadSection.WhatsappReserve : LeadSection.NeedsVerification,
            ContactMode.WhatsAppFirst => hasWa ? LeadSection.WhatsappReserve : hasTg ? LeadSection.TelegramResolved : LeadSection.NeedsVerification,
            ContactMode.TelegramOnly => hasTg ? LeadSection.TelegramResolved : LeadSection.NeedsVerification,
            _ => hasTg ? LeadSection.TelegramResolved : hasWa ? LeadSection.WhatsappReserve : LeadSection.NeedsVerification,
        };
        if (section == LeadSection.NeedsVerification) return null;

        if (whatsapp.Length == 0 && phone.Length > 0) whatsapp = $"https://wa.me/{phone}";

        return new LeadRecord
        {
            RunId = runId,
            Handle = candidate.Handle,
            FullName = string.IsNullOrWhiteSpace(profile.FullName) ? candidate.Handle : profile.FullName,
            City = result.Location.City,
            Niche = candidate.Niche,
            NicheGroup = candidate.NicheGroup,
            Phone = phone,
            TelegramUrl = telegram,
            WhatsappUrl = whatsapp,
            WhatsappStatus = whatsappStatus,
            TelegramStatus = telegram.Length > 0 ? "published" : "unchecked",
            Section = section,
            ReasonsCsv = string.Join(", ", result.Reasons),
            OutreachText = MessageGenerator.Generate(candidate.Handle, candidate.Niche, result.Location.City),
            FollowerCount = profile.FollowerCount,
            MediaCount = profile.MediaCount,
            LatestPostDate = result.LatestPostDate,
            LatestPostAgeDays = result.LatestPostAgeDays ?? -1,
            ActiveStory = result.ActiveStory,
            BioSite = result.BioSite.Url,
            QualifiedPreSite = true,
        };
    }
}

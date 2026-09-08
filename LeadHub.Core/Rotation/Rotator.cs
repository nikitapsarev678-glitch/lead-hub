using LeadHub.Core.Store;

namespace LeadHub.Core.Rotation;

public sealed record WorkerPair(IgAccount Account, Vpn.VpnNode VpnNode, int SlotIndex, int ProxyPort);

/// <summary>
/// Ротация: выбор наименее используемой пары «аккаунт + VPN» перед прогоном и при срабатывании
/// лимитов Instagram. Скор аккаунта = запросы за 24ч × 3 + за 7д; VPN — наименее использованный
/// среди здоровых в sticky-стране аккаунта (аккаунт не мигрирует между странами без необходимости).
/// </summary>
public sealed class Rotator(Db db)
{
    private readonly AccountRepository _accounts = new(db);
    private readonly VpnRepository _vpns = new(db);
    private readonly UsageRepository _usage = new(db);
    private readonly HashSet<long> _busyAccounts = new();

    public event Action<string>? Log;

    /// <summary>Все доступные аккаунты (не в кулдауне, не забанены, под дневным капом).</summary>
    public IReadOnlyList<IgAccount> AvailableAccounts(int dailyCap, IEnumerable<long>? onlyIds = null)
    {
        var now = DateTime.UtcNow;
        var ids = onlyIds?.ToHashSet();
        return _accounts.All().Where(a =>
        {
            if (a.Status == "banned") return false;
            if (a.CooldownUntil.HasValue && a.CooldownUntil.Value > now) return false;
            if (ids != null && !ids.Contains(a.Id)) return false;
            return _usage.AccountRequestsSince(a.Id, now.Date) < dailyCap;
        }).ToList();
    }

    /// <summary>Выбрать пару для воркера. sticky: аккаунт с зафиксированной страной получает узел той же страны.</summary>
    public WorkerPair? ChoosePair(int slotIndex, int proxyPort, int dailyCap, IEnumerable<long>? onlyAccountIds = null, IEnumerable<long>? onlyVpnIds = null)
    {
        var vpnIds = onlyVpnIds?.ToHashSet();
        var healthyVpns = _vpns.All()
            .Where(v => v.Enabled && v.IsHealthy && (vpnIds == null || vpnIds.Count == 0 || vpnIds.Contains(v.Id)))
            .ToList();
        if (healthyVpns.Count == 0)
        {
            Log?.Invoke("Нет здоровых VPN-узлов: сначала импортируйте ссылки и проверьте пинги.");
            return null;
        }

        var accounts = AvailableAccounts(dailyCap, onlyAccountIds)
            .OrderBy(a => Score(a.Id))
            .ToList();
        var account = accounts.FirstOrDefault(a => !_busyAccounts.Contains(a.Id));
        if (account == null)
        {
            Log?.Invoke("Нет свободных аккаунтов (кулдаун/дневной кап/все заняты воркерами).");
            return null;
        }

        // Sticky-страна: аккаунт остаётся в одной стране; иначе берём страну наименее используемого узла.
        Vpn.VpnNode vpn;
        var countryVpns = healthyVpns
            .Where(v => v.CountryCode.Length > 0 && v.CountryCode.Equals(account.StickyCountry, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => _usage.VpnUseCount(v.Id))
            .ToList();
        if (countryVpns.Count > 0)
        {
            vpn = countryVpns[0];
        }
        else
        {
            vpn = healthyVpns
                .OrderBy(v => _usage.VpnUseCount(v.Id))
                .First();
            if (account.StickyCountry.Length == 0 && vpn.CountryCode.Length > 0)
            {
                _accounts.SetStickyCountry(account.Id, vpn.CountryCode);
                account.StickyCountry = vpn.CountryCode;
                Log?.Invoke($"Аккаунт @{account.Username} закреплён за страной {vpn.CountryName} ({vpn.CountryCode}).");
            }
        }

        _busyAccounts.Add(account.Id);
        Log?.Invoke($"Слот {slotIndex}: аккаунт @{account.Username} + VPN {vpn.Flag} {vpn.Name} (наименее используемые).");
        return new WorkerPair(account, vpn, slotIndex, proxyPort);
    }

    public void ReleasePair(WorkerPair pair)
    {
        _busyAccounts.Remove(pair.Account.Id);
        _accounts.MarkUsed(pair.Account.Id);
        _vpns.MarkUsed(pair.VpnNode.Id);
    }

    public void SetCooldown(IgAccount account, TimeSpan duration)
    {
        _accounts.SetStatus(account.Id, "cooldown", DateTime.UtcNow + duration);
        Log?.Invoke($"Аккаунт @{account.Username} в кулдауне {duration.TotalMinutes:F0} мин.");
    }

    public double Score(long accountId) =>
        _usage.AccountRequestsSince(accountId, DateTime.UtcNow.Date) * 3 +
        _usage.AccountRequestsSince(accountId, DateTime.UtcNow.AddDays(-7));
}

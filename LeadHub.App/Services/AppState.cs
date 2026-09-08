using System.Text.Json;
using LeadHub.Core.Parser;
using LeadHub.Core.Rotation;
using LeadHub.Core.Store;

namespace LeadHub.App.Services;

/// <summary>Общее состояние приложения: БД, мастер-ключ, репозитории, расшифровка сессий аккаунтов.</summary>
public sealed class AppState : IDisposable
{
    public Db Db { get; }
    public byte[] MasterKey { get; }
    public VpnRepository Vpns { get; }
    public AccountRepository Accounts { get; }
    public LeadRepository Leads { get; }
    public ExclusionRepository Exclusions { get; }
    public PresetRepository Presets { get; }
    public RunRepository Runs { get; }
    public UsageRepository Usage { get; }
    public SettingsRepository Settings { get; }
    public Rotator Rotator { get; }

    private readonly Dictionary<long, CookieSession> _sessionCache = new();

    public AppState()
    {
        Db = new Db(AppPaths.Database);
        MasterKey = Crypto.LoadOrCreateKey(AppPaths.KeyFile);
        Vpns = new VpnRepository(Db);
        Accounts = new AccountRepository(Db);
        Leads = new LeadRepository(Db);
        Exclusions = new ExclusionRepository(Db);
        Presets = new PresetRepository(Db);
        Runs = new RunRepository(Db);
        Usage = new UsageRepository(Db);
        Settings = new SettingsRepository(Db);
        Rotator = new Rotator(Db);
    }

    /// <summary>Расшифровать cookies аккаунта (с кэшем) — для движка парсинга.</summary>
    public CookieSession? ResolveSession(long accountId)
    {
        if (_sessionCache.TryGetValue(accountId, out var cached)) return cached;
        var account = Accounts.All().FirstOrDefault(a => a.Id == accountId);
        if (account == null || account.CookiesEncrypted.Length == 0) return null;
        try
        {
            var json = Crypto.DecryptString(account.CookiesEncrypted, MasterKey);
            var session = JsonSerializer.Deserialize<CookieSession>(json);
            if (session != null) _sessionCache[accountId] = session;
            return session;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Зашифровать и сохранить сессию аккаунта.</summary>
    public long SaveAccount(string username, CookieSession session)
    {
        var json = JsonSerializer.Serialize(session);
        var encrypted = Crypto.EncryptString(json, MasterKey);
        return Accounts.Insert(username, encrypted, session.DsUserId ?? "");
    }

    public CookieSession? LoadSession(IgAccount account)
    {
        try
        {
            var json = Crypto.DecryptString(account.CookiesEncrypted, MasterKey);
            return JsonSerializer.Deserialize<CookieSession>(json);
        }
        catch { return null; }
    }

    public void InvalidateSessions() => _sessionCache.Clear();

    public void Dispose() => Db.Dispose();
}

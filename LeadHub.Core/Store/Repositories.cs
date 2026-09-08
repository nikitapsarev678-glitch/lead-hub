using Dapper;

namespace LeadHub.Core.Store;

public sealed class IgAccount
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string DsUserId { get; set; } = "";
    public byte[] CookiesEncrypted { get; set; } = Array.Empty<byte>();
    public string Status { get; set; } = "active";       // active | needs_check | cooldown | banned
    public DateTime? CooldownUntil { get; set; }
    public string StickyCountry { get; set; } = "";
    public DateTime? LastCheckAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public long UseCount { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class RunRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string PresetJson { get; set; } = "";
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "running";
    public long Discovered { get; set; }
    public long Validated { get; set; }
    public long Qualified { get; set; }
    public long Leads { get; set; }
    public string StopReason { get; set; } = "";
}

public sealed class VpnRepository(Db db)
{
    public IReadOnlyList<Vpn.VpnNode> All()
    {
        var rows = db.Query<dynamic>("SELECT * FROM vpn_nodes ORDER BY country_name, name");
        return rows.Select(ToNode).ToList();
    }

    public IReadOnlyList<Vpn.VpnNode> Enabled() => All().Where(n => n.Enabled).ToList();

    public long Insert(Vpn.VpnNode n)
    {
        db.Execute("""
            INSERT OR IGNORE INTO vpn_nodes(name, server, port, uuid, flow, security, sni, fingerprint, public_key, short_id, transport, path, host_header, service_name, raw_link, country_code, country_name)
            VALUES(@Name, @Server, @Port, @Uuid, @Flow, @Security, @Sni, @Fingerprint, @PublicKey, @ShortId, @Transport, @Path, @HostHeader, @ServiceName, @RawLink, @CountryCode, @CountryName);
            """, n);
        return db.FirstOrDefault<long?>("SELECT id FROM vpn_nodes WHERE server=@Server AND port=@Port AND uuid=@Uuid", n) ?? 0;
    }

    public void UpdateCountry(Vpn.VpnNode n) =>
        db.Execute("UPDATE vpn_nodes SET country_code=@CountryCode, country_name=@CountryName WHERE id=@Id", n);

    public void UpdateLatency(Vpn.VpnNode n) =>
        db.Execute("UPDATE vpn_nodes SET last_latency_ms=@LastLatencyMs, last_checked_at=@LastCheckedAt WHERE id=@Id", n);

    public void MarkUsed(long id) =>
        db.Execute("UPDATE vpn_nodes SET use_count=use_count+1, last_used_at=datetime('now') WHERE id=@id", new { id });

    public void SetEnabled(long id, bool enabled) =>
        db.Execute("UPDATE vpn_nodes SET enabled=@enabled WHERE id=@id", new { enabled = enabled ? 1 : 0 });

    public void Delete(long id) => db.Execute("DELETE FROM vpn_nodes WHERE id=@id", new { id });

    private static Vpn.VpnNode ToNode(dynamic r) => new()
    {
        Id = (long)r.id,
        Name = (string)r.name,
        Server = (string)r.server,
        Port = (int)r.port,
        Uuid = (string)r.uuid,
        Flow = (string?)r.flow ?? "",
        Security = (string?)r.security ?? "tls",
        Sni = (string?)r.sni ?? "",
        Fingerprint = (string?)r.fingerprint ?? "chrome",
        PublicKey = (string?)r.public_key ?? "",
        ShortId = (string?)r.short_id ?? "",
        Transport = (string?)r.transport ?? "tcp",
        Path = (string?)r.path ?? "",
        HostHeader = (string?)r.host_header ?? "",
        ServiceName = (string?)r.service_name ?? "",
        RawLink = (string?)r.raw_link ?? "",
        CountryCode = (string?)r.country_code ?? "",
        CountryName = (string?)r.country_name ?? "",
        Enabled = (long)r.enabled == 1,
        UseCount = (int)r.use_count,
        LastUsedAt = ParseDate(r.last_used_at),
        LastLatencyMs = (int)r.last_latency_ms,
        LastCheckedAt = ParseDate(r.last_checked_at),
        AddedAt = ParseDate(r.added_at) ?? DateTime.UtcNow,
    };

    internal static DateTime? ParseDate(object? value) =>
        value is string s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToUniversalTime() : value is DateTime dt ? dt : null;
}

public sealed class AccountRepository(Db db)
{
    public IReadOnlyList<IgAccount> All() =>
        db.Query<IgAccount>("SELECT id, username, ds_user_id AS DsUserId, cookies_encrypted AS CookiesEncrypted, status, cooldown_until, sticky_country, last_check_at, last_used_at, use_count, notes FROM ig_accounts ORDER BY username").ToList();

    public IgAccount? ByUsername(string username) =>
        db.FirstOrDefault<IgAccount?>("SELECT id, username, ds_user_id AS DsUserId, cookies_encrypted as CookiesEncrypted, status, cooldown_until, sticky_country, last_check_at, last_used_at, use_count, notes FROM ig_accounts WHERE username=@username COLLATE NOCASE", new { username });

    public long Insert(string username, byte[] cookiesEncrypted, string dsUserId)
    {
        db.Execute("""
            INSERT INTO ig_accounts(username, ds_user_id, cookies_encrypted) VALUES(@username, @dsUserId, @cookies)
            ON CONFLICT(username) DO UPDATE SET cookies_encrypted=@cookies, ds_user_id=@dsUserId, status='active', cooldown_until=NULL;
            """, new { username, dsUserId, cookies = cookiesEncrypted });
        return db.FirstOrDefault<long?>("SELECT id FROM ig_accounts WHERE username=@username COLLATE NOCASE", new { username }) ?? 0;
    }

    public void SetStatus(long id, string status, DateTime? cooldownUntil = null) =>
        db.Execute("UPDATE ig_accounts SET status=@status, cooldown_until=@cd WHERE id=@id",
            new { id, status, cd = cooldownUntil?.ToString("yyyy-MM-dd HH:mm:ss") });

    public void SetStickyCountry(long id, string countryCode) =>
        db.Execute("UPDATE ig_accounts SET sticky_country=@cc WHERE id=@id", new { id, cc = countryCode });

    public void MarkChecked(long id) =>
        db.Execute("UPDATE ig_accounts SET last_check_at=datetime('now') WHERE id=@id", new { id });

    public void MarkUsed(long id) =>
        db.Execute("UPDATE ig_accounts SET use_count=use_count+1, last_used_at=datetime('now') WHERE id=@id", new { id });

    public void Delete(long id) => db.Execute("DELETE FROM ig_accounts WHERE id=@id", new { id });
}

public sealed class UsageRepository(Db db)
{
    public void Record(long? accountId, long? vpnNodeId, long runId, int requestCount) =>
        db.Execute("INSERT INTO usage_events(ts, account_id, vpn_node_id, run_id, request_count) VALUES(datetime('now'), @a, @v, @r, @c)",
            new { a = accountId, v = vpnNodeId, r = runId, c = requestCount });

    public long AccountRequestsSince(long accountId, DateTime sinceUtc) =>
        db.ExecuteScalar<long>("SELECT COALESCE(SUM(request_count),0) FROM usage_events WHERE account_id=@id AND ts >= @since",
            new { id = accountId, since = sinceUtc.ToString("yyyy-MM-dd HH:mm:ss") });

    public long VpnUseCount(long vpnNodeId) =>
        db.ExecuteScalar<long>("SELECT COUNT(*) FROM usage_events WHERE vpn_node_id=@id", new { id = vpnNodeId });

    public sealed record UsageRow(string Label, long Today, long Week, long Total);

    public IReadOnlyList<UsageRow> AccountUsage() =>
        db.Query<UsageRow>("""
            SELECT a.username AS Label,
              COALESCE(SUM(CASE WHEN u.ts >= date('now') THEN u.request_count ELSE 0 END),0) AS Today,
              COALESCE(SUM(CASE WHEN u.ts >= datetime('now','-7 days') THEN u.request_count ELSE 0 END),0) AS Week,
              COALESCE(SUM(u.request_count),0) AS Total
            FROM ig_accounts a LEFT JOIN usage_events u ON u.account_id=a.id
            GROUP BY a.id ORDER BY Today DESC
            """).ToList();

    public IReadOnlyList<UsageRow> VpnUsage() =>
        db.Query<UsageRow>("""
            SELECT COALESCE(v.country_name || ' · ' || v.name, v.name) AS Label,
              COUNT(CASE WHEN u.ts >= date('now') THEN 1 END) AS Today,
              COUNT(CASE WHEN u.ts >= datetime('now','-7 days') THEN 1 END) AS Week,
              COUNT(*) AS Total
            FROM vpn_nodes v LEFT JOIN usage_events u ON u.vpn_node_id=v.id
            GROUP BY v.id ORDER BY Today DESC
            """).ToList();
}

public sealed class LeadRepository(Db db)
{
    public IReadOnlyList<Parser.LeadRecord> All() =>
        db.Query<dynamic>("SELECT * FROM leads ORDER BY created_at DESC").Select(ToRecord).ToList();

    public IReadOnlyList<Parser.LeadRecord> BySection(string section) =>
        db.Query<dynamic>("SELECT * FROM leads WHERE section=@section ORDER BY created_at DESC", new { section }).Select(ToRecord).ToList();

    public bool HandleExists(string handle) =>
        db.ExecuteScalar<long>("SELECT COUNT(*) FROM leads WHERE handle=@handle COLLATE NOCASE", new { handle }) > 0;

    public void Insert(Parser.LeadRecord lead)
    {
        var outreach = lead.OutreachText.Length > 0 ? lead.OutreachText : Parser.MessageGenerator.Generate(lead);
        db.Execute("""
            INSERT INTO leads(run_id, handle, full_name, city, niche, niche_group, phone, telegram_url, whatsapp_url,
                whatsapp_status, telegram_status, section, reasons, outreach_text,
                follower_count, media_count, latest_post_date, latest_post_age_days, active_story, bio_site, qualified_pre_site)
            VALUES(@RunId, @Handle, @FullName, @City, @Niche, @NicheGroup, @Phone, @TelegramUrl, @WhatsappUrl,
                @WhatsappStatus, @TelegramStatus, @Section, @Reasons, @outreach,
                @FollowerCount, @MediaCount, @LatestPostDate, @LatestPostAgeDays, @ActiveStory, @BioSite, @QualifiedPreSite)
            ON CONFLICT(handle) DO UPDATE SET
                run_id=excluded.run_id, phone=excluded.phone, telegram_url=excluded.telegram_url,
                whatsapp_url=excluded.whatsapp_url, whatsapp_status=excluded.whatsapp_status,
                telegram_status=excluded.telegram_status, section=excluded.section,
                reasons=excluded.reasons, outreach_text=excluded.outreach_text,
                follower_count=excluded.follower_count, media_count=excluded.media_count,
                latest_post_date=excluded.latest_post_date, latest_post_age_days=excluded.latest_post_age_days,
                active_story=excluded.active_story, bio_site=excluded.bio_site, qualified_pre_site=excluded.qualified_pre_site;
            """, new
        {
            lead.RunId, lead.Handle, lead.FullName, lead.City, lead.Niche, lead.NicheGroup, lead.Phone,
            lead.TelegramUrl, lead.WhatsappUrl, lead.WhatsappStatus, lead.TelegramStatus,
            Section = Export.HtmlHelperExporter.SectionKey(lead.Section), Reasons = lead.ReasonsCsv, outreach,
            lead.FollowerCount, lead.MediaCount, lead.LatestPostDate, lead.LatestPostAgeDays,
            ActiveStory = lead.ActiveStory ? 1 : 0, lead.BioSite, QualifiedPreSite = lead.QualifiedPreSite ? 1 : 0,
        });
    }

    public void MarkSent(long id, bool sent) =>
        db.Execute("UPDATE leads SET sent=@s, sent_at=CASE WHEN @s=1 THEN datetime('now') ELSE sent_at END WHERE id=@id",
            new { id, s = sent ? 1 : 0 });

    public void SetFeedback(long id, string notSentReason, string comment) =>
        db.Execute("UPDATE leads SET not_sent_reason=@r, comment=@c WHERE id=@id", new { id, r = notSentReason, c = comment });

    public void SetOutreachText(long id, string text) =>
        db.Execute("UPDATE leads SET outreach_text=@t WHERE id=@id", new { id, t = text });

    private static Parser.LeadRecord ToRecord(dynamic r) => new()
    {
        Id = (long)r.id,
        RunId = (long)r.run_id,
        Handle = (string)r.handle,
        FullName = (string?)r.full_name ?? "",
        City = (string?)r.city ?? "",
        Niche = (string?)r.niche ?? "",
        NicheGroup = (string?)r.niche_group ?? "",
        Phone = (string?)r.phone ?? "",
        TelegramUrl = (string?)r.telegram_url ?? "",
        WhatsappUrl = (string?)r.whatsapp_url ?? "",
        WhatsappStatus = (string?)r.whatsapp_status ?? "unchecked",
        TelegramStatus = (string?)r.telegram_status ?? "unchecked",
        Section = (string?)r.section switch
        {
            "telegram_resolved" => Parser.LeadSection.TelegramResolved,
            "whatsapp_reserve" => Parser.LeadSection.WhatsappReserve,
            _ => Parser.LeadSection.NeedsVerification,
        },
        ReasonsCsv = (string?)r.reasons ?? "",
        OutreachText = (string?)r.outreach_text ?? "",
        Sent = (long)r.sent == 1,
        SentAt = VpnRepository.ParseDate(r.sent_at),
        NotSentReason = (string?)r.not_sent_reason ?? "",
        Comment = (string?)r.comment ?? "",
        FollowerCount = (long)r.follower_count,
        MediaCount = (long)r.media_count,
        LatestPostDate = (string?)r.latest_post_date ?? "",
        LatestPostAgeDays = (int)r.latest_post_age_days,
        ActiveStory = (long)r.active_story == 1,
        BioSite = (string?)r.bio_site ?? "",
        QualifiedPreSite = (long)r.qualified_pre_site == 1,
    };
}

public sealed class ExclusionRepository(Db db)
{
    public void EnsureTables()
    {
        // exclusions создаются в схеме Db; здесь — точка входа для импорта exclusions.json скилла
    }

    public (HashSet<string> Handles, HashSet<string> Phones) All()
    {
        var rows = db.Query<dynamic>("SELECT handle, phone FROM exclusions");
        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var phones = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (r.handle is string h && h.Length > 0) handles.Add(h.ToLowerInvariant());
            if (r.phone is string p && p.Length > 0) phones.Add(p);
        }
        return (handles, phones);
    }

    /// <summary>Импорт exclusions.json из скилла: {"handles":[{"handle":...}],"phones":[...]}.</summary>
    public (int handles, int phones) ImportFromSkillJson(string path)
    {
        if (!File.Exists(path)) return (0, 0);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        int h = 0, p = 0;
        if (root.TryGetProperty("handles", out var handles) && handles.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in handles.EnumerateArray())
            {
                var handle = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.TryGetProperty("handle", out var hh) ? hh.GetString() : null;
                if (!string.IsNullOrWhiteSpace(handle))
                {
                    db.Execute("INSERT OR IGNORE INTO exclusions(handle, phone, source) VALUES(@h, '', 'skill-import')", new { h = handle.ToLowerInvariant() });
                    h++;
                }
            }
        }
        if (root.TryGetProperty("phones", out var phones) && phones.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in phones.EnumerateArray())
            {
                var phone = item.GetString();
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    db.Execute("INSERT OR IGNORE INTO exclusions(handle, phone, source) VALUES('', @p, 'skill-import')", new { p = phone });
                    p++;
                }
            }
        }
        return (h, p);
    }

    public void AddHandle(string handle, string source) =>
        db.Execute("INSERT OR IGNORE INTO exclusions(handle, phone, source) VALUES(@h, '', @s)",
            new { h = handle.ToLowerInvariant(), s = source });

    public void AddPhone(string phone, string source) =>
        db.Execute("INSERT OR IGNORE INTO exclusions(handle, phone, source) VALUES('', @p, @s)", new { p = phone, s = source });
}

public sealed class PresetRepository(Db db)
{
    public IReadOnlyList<(long id, string name)> All() =>
        db.Query<(long, string)>("SELECT id, name FROM presets ORDER BY name").ToList();

    public Parser.ParsePreset? Load(long id)
    {
        var json = db.FirstOrDefault<string?>("SELECT json FROM presets WHERE id=@id", new { id });
        return json == null ? null : JsonSerializer.Deserialize<Parser.ParsePreset>(json);
    }

    public void Save(Parser.ParsePreset preset)
    {
        var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
        db.Execute("""
            INSERT INTO presets(name, json, updated_at) VALUES(@name, @json, datetime('now'))
            ON CONFLICT(name) DO UPDATE SET json=@json, updated_at=datetime('now');
            """, new { name = preset.Name, json });
    }

    public void Delete(long id) => db.Execute("DELETE FROM presets WHERE id=@id", new { id });
}

public sealed class RunRepository(Db db)
{
    public long Start(string name, Parser.ParsePreset preset)
    {
        db.Execute("INSERT INTO runs(name, preset_json, status) VALUES(@name, @json, 'running')",
            new { name, json = JsonSerializer.Serialize(preset) });
        return db.LastInsertId();
    }

    public void UpdateCounts(long id, long discovered, long validated, long qualified, long leads) =>
        db.Execute("UPDATE runs SET discovered=@d, validated=@v, qualified=@q, leads=@l WHERE id=@id",
            new { id, d = discovered, v = validated, q = qualified, l = leads });

    public void Finish(long id, string status, string stopReason) =>
        db.Execute("UPDATE runs SET status=@s, stop_reason=@r, finished_at=datetime('now') WHERE id=@id",
            new { id, s = status, r = stopReason });

    public IReadOnlyList<RunRow> History() =>
        db.Query<RunRow>("SELECT id, name, preset_json AS PresetJson, started_at AS StartedAt, finished_at AS FinishedAt, status, discovered, validated, qualified, leads, stop_reason AS StopReason FROM runs ORDER BY id DESC LIMIT 100").ToList();
}

public sealed class SettingsRepository(Db db)
{
    public string? Get(string key) => db.FirstOrDefault<string?>("SELECT value FROM settings WHERE key=@key", new { key });

    public void Set(string key, string? value) =>
        db.Execute("INSERT INTO settings(key, value) VALUES(@key, @value) ON CONFLICT(key) DO UPDATE SET value=@value",
            new { key, value });
}

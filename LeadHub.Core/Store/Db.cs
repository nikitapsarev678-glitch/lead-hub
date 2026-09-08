using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace LeadHub.Core.Store;

/// <summary>SQLite-хранилище LeadHub: одна БД leadhub.db, WAL, Dapper.</summary>
public sealed class Db : IDisposable
{
    public string DatabasePath { get; }
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();

    public Db(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _conn = new SqliteConnection($"Data Source={databasePath};Cache=Shared");
        _conn.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        Migrate();
    }

    public int Execute(string sql, object? param = null)
    {
        lock (_lock) { return _conn.Execute(sql, param); }
    }

    public T Scalar<T>(string sql, object? param = null)
    {
        lock (_lock) { return _conn.ExecuteScalar<T>(sql, param)!; }
    }

    public T ExecuteScalar<T>(string sql, object? param = null)
    {
        lock (_lock) { return _conn.ExecuteScalar<T>(sql, param)!; }
    }

    public IEnumerable<T> Query<T>(string sql, object? param = null)
    {
        lock (_lock)
        {
            var rows = _conn.Query<T>(sql, param);
            return rows is null ? Enumerable.Empty<T>() : rows;
        }
    }

    public T? FirstOrDefault<T>(string sql, object? param = null)
    {
        lock (_lock) { return _conn.QueryFirstOrDefault<T>(sql, param)!; }
    }

    public long LastInsertId() => Scalar<long>("SELECT last_insert_rowid();");

    private void Migrate()
    {
        const string schema = """
CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);

CREATE TABLE IF NOT EXISTS vpn_nodes(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL, server TEXT NOT NULL, port INTEGER NOT NULL,
  uuid TEXT NOT NULL, flow TEXT DEFAULT '', security TEXT DEFAULT 'tls',
  sni TEXT DEFAULT '', fingerprint TEXT DEFAULT 'chrome',
  public_key TEXT DEFAULT '', short_id TEXT DEFAULT '',
  transport TEXT DEFAULT 'tcp', path TEXT DEFAULT '', host_header TEXT DEFAULT '', service_name TEXT DEFAULT '',
  raw_link TEXT DEFAULT '',
  country_code TEXT DEFAULT '', country_name TEXT DEFAULT '',
  enabled INTEGER DEFAULT 1, use_count INTEGER DEFAULT 0, last_used_at TEXT,
  last_latency_ms INTEGER DEFAULT -1, last_checked_at TEXT,
  added_at TEXT DEFAULT (datetime('now'))
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_vpn_dedup ON vpn_nodes(server, port, uuid);

CREATE TABLE IF NOT EXISTS ig_accounts(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT NOT NULL UNIQUE COLLATE NOCASE,
  ds_user_id TEXT DEFAULT '',
  cookies_encrypted BLOB NOT NULL,
  status TEXT DEFAULT 'active',
  cooldown_until TEXT,
  sticky_country TEXT DEFAULT '',
  last_check_at TEXT, last_used_at TEXT, use_count INTEGER DEFAULT 0,
  notes TEXT DEFAULT '', created_at TEXT DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS usage_events(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT DEFAULT (datetime('now')),
  account_id INTEGER, vpn_node_id INTEGER, run_id INTEGER,
  request_count INTEGER DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_usage_ts ON usage_events(ts);

CREATE TABLE IF NOT EXISTS runs(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT, preset_json TEXT, started_at TEXT DEFAULT (datetime('now')), finished_at TEXT,
  status TEXT DEFAULT 'running',
  discovered INTEGER DEFAULT 0, validated INTEGER DEFAULT 0, qualified INTEGER DEFAULT 0, leads INTEGER DEFAULT 0,
  stop_reason TEXT DEFAULT ''
);

CREATE TABLE IF NOT EXISTS leads(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id INTEGER,
  handle TEXT NOT NULL UNIQUE COLLATE NOCASE,
  full_name TEXT DEFAULT '', city TEXT DEFAULT '', niche TEXT DEFAULT '', niche_group TEXT DEFAULT '',
  phone TEXT DEFAULT '', telegram_url TEXT DEFAULT '', whatsapp_url TEXT DEFAULT '',
  whatsapp_status TEXT DEFAULT 'unchecked', telegram_status TEXT DEFAULT 'unchecked',
  section TEXT DEFAULT 'needs_verification',
  reasons TEXT DEFAULT '', outreach_text TEXT DEFAULT '',
  sent INTEGER DEFAULT 0, sent_at TEXT, not_sent_reason TEXT DEFAULT '', comment TEXT DEFAULT '',
  follower_count INTEGER DEFAULT 0, media_count INTEGER DEFAULT 0,
  latest_post_date TEXT DEFAULT '', latest_post_age_days INTEGER DEFAULT -1, active_story INTEGER DEFAULT 0,
  bio_site TEXT DEFAULT '', qualified_pre_site INTEGER DEFAULT 0,
  created_at TEXT DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_leads_section ON leads(section);

CREATE TABLE IF NOT EXISTS exclusions(
  handle TEXT PRIMARY KEY COLLATE NOCASE,
  phone TEXT DEFAULT '', source TEXT DEFAULT '', added_at TEXT DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS presets(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT UNIQUE,
  json TEXT,
  created_at TEXT DEFAULT (datetime('now')), updated_at TEXT DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);

CREATE TABLE IF NOT EXISTS validation_cache(
  run_id INTEGER, handle TEXT, raw_json TEXT, checked_at TEXT DEFAULT (datetime('now')),
  PRIMARY KEY (run_id, handle)
);
""";
        Execute(schema);
        Execute("INSERT OR IGNORE INTO meta(key, value) VALUES('schema_version', '1');");
    }

    public void Dispose()
    {
        lock (_lock) { _conn.Dispose(); }
    }
}

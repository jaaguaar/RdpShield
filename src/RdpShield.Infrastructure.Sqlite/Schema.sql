PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;

CREATE TABLE IF NOT EXISTS allowlist (
  ip_or_cidr TEXT PRIMARY KEY,
  comment TEXT NULL,
  created_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_allowlist_created_at ON allowlist(created_at_utc);

CREATE TABLE IF NOT EXISTS bans (
  ip TEXT PRIMARY KEY,
  reason TEXT NOT NULL,
  source TEXT NOT NULL,
  first_seen_utc TEXT NOT NULL,
  last_seen_utc TEXT NOT NULL,
  expires_utc TEXT NOT NULL,
  attempts_in_window INTEGER NOT NULL,
  active INTEGER NOT NULL,
  unbanned_at_utc TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_bans_active_expires ON bans(active, expires_utc);

CREATE TABLE IF NOT EXISTS events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts_utc TEXT NOT NULL,
  level TEXT NOT NULL,
  type TEXT NOT NULL,
  message TEXT NOT NULL,
  ip TEXT NULL,
  source TEXT NULL,
  payload_json TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_events_ts ON events(ts_utc);
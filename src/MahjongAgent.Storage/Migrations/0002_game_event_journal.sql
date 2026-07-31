CREATE TABLE game_sessions (
  session_id TEXT PRIMARY KEY NOT NULL,
  rule_profile_version TEXT NOT NULL,
  phase INTEGER NOT NULL,
  last_event_sequence INTEGER NOT NULL,
  last_event_fingerprint TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL,
  ended_at_utc TEXT NULL,
  CHECK (phase IN (1, 2)),
  CHECK (last_event_sequence >= 1),
  CHECK (length(last_event_fingerprint) = 64)
);

CREATE TABLE game_events (
  session_id TEXT NOT NULL,
  sequence INTEGER NOT NULL,
  event_id TEXT NOT NULL UNIQUE,
  event_kind TEXT NOT NULL,
  event_fingerprint TEXT NOT NULL,
  event_document_checksum TEXT NOT NULL,
  occurred_at_utc TEXT NOT NULL,
  source_kind INTEGER NOT NULL,
  confidence REAL NOT NULL,
  event_json TEXT NOT NULL,
  stored_at_utc TEXT NOT NULL,
  PRIMARY KEY (session_id, sequence),
  FOREIGN KEY (session_id) REFERENCES game_sessions(session_id) ON DELETE CASCADE,
  CHECK (sequence >= 1),
  CHECK (source_kind BETWEEN 0 AND 3),
  CHECK (length(event_fingerprint) = 64),
  CHECK (length(event_document_checksum) = 64),
  CHECK (confidence >= 0 AND confidence <= 1)
);

CREATE INDEX ix_game_events_session_time
  ON game_events(session_id, occurred_at_utc);

CREATE TABLE game_state_snapshots (
  session_id TEXT NOT NULL,
  revision INTEGER NOT NULL,
  last_event_fingerprint TEXT NOT NULL,
  state_json TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  PRIMARY KEY (session_id, revision),
  FOREIGN KEY (session_id) REFERENCES game_sessions(session_id) ON DELETE CASCADE,
  CHECK (revision >= 1),
  CHECK (length(last_event_fingerprint) = 64)
);

CREATE INDEX ix_game_sessions_phase_updated
  ON game_sessions(phase, updated_at_utc DESC);

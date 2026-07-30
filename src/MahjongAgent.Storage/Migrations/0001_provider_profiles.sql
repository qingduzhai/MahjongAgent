CREATE TABLE openai_compatible_provider_profiles (
  id TEXT PRIMARY KEY NOT NULL,
  display_name TEXT NOT NULL,
  base_uri TEXT NOT NULL,
  allow_insecure_http INTEGER NOT NULL,
  chat_completions_path TEXT NOT NULL,
  perception_model TEXT NOT NULL,
  credential_id TEXT NULL,
  api_key_transport INTEGER NOT NULL,
  api_key_header_name TEXT NOT NULL,
  api_key_prefix TEXT NOT NULL,
  capabilities INTEGER NOT NULL,
  structured_output_mode INTEGER NOT NULL,
  image_detail INTEGER NOT NULL,
  additional_headers_json TEXT NOT NULL,
  last_probe_at_utc TEXT NULL,
  last_probe_success INTEGER NULL,
  last_probe_failure_kind INTEGER NULL,
  last_verified_capabilities INTEGER NULL,
  last_actual_model TEXT NULL,
  last_probe_duration_ms INTEGER NULL,
  last_provider_request_id TEXT NULL,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE INDEX ix_provider_profiles_display_name
  ON openai_compatible_provider_profiles(display_name COLLATE NOCASE);

-- pgTAP tests for the Laptop Guardian database schema.
-- Run with: supabase test db
-- or: pg_prove -d <db_url> tests/00001_schema_test.sql

begin;
select plan(65);

-- ============================================================
-- 1. Tables exist
-- ============================================================
select has_table('public', 'profiles', 'profiles table exists');
select has_table('public', 'devices', 'devices table exists');
select has_table('public', 'activity_events', 'activity_events table exists');
select has_table('public', 'pairing_codes', 'pairing_codes table exists');
select has_table('public', 'heartbeats', 'heartbeats table exists');
select has_table('public', 'notification_tokens', 'notification_tokens table exists');
select has_table('public', 'notification_settings', 'notification_settings table exists');
select has_table('public', 'admin_actions', 'admin_actions table exists');

-- ============================================================
-- 2. Primary keys
-- ============================================================
select has_pk('public', 'profiles', 'profiles has primary key');
select has_pk('public', 'devices', 'devices has primary key');
select has_pk('public', 'activity_events', 'activity_events has primary key');
select has_pk('public', 'pairing_codes', 'pairing_codes has primary key');
select has_pk('public', 'heartbeats', 'heartbeats has primary key');
select has_pk('public', 'notification_tokens', 'notification_tokens has primary key');
select has_pk('public', 'notification_settings', 'notification_settings has primary key');
select has_pk('public', 'admin_actions', 'admin_actions has primary key');

-- ============================================================
-- 3. Key columns exist with correct types
-- ============================================================

-- profiles
select has_column('public', 'profiles', 'id', 'profiles.id exists');
select has_column('public', 'profiles', 'full_name', 'profiles.full_name exists');
select has_column('public', 'profiles', 'email', 'profiles.email exists');
select has_column('public', 'profiles', 'created_at', 'profiles.created_at exists');
select has_column('public', 'profiles', 'updated_at', 'profiles.updated_at exists');

-- devices
select has_column('public', 'devices', 'user_id', 'devices.user_id exists');
select has_column('public', 'devices', 'machine_name', 'devices.machine_name exists');
select has_column('public', 'devices', 'api_key_hash', 'devices.api_key_hash exists');
select has_column('public', 'devices', 'status', 'devices.status exists');
select has_column('public', 'devices', 'last_seen_at', 'devices.last_seen_at exists');
select has_column('public', 'devices', 'heartbeat_interval_s', 'devices.heartbeat_interval_s exists');

-- activity_events
select has_column('public', 'activity_events', 'device_id', 'activity_events.device_id exists');
select has_column('public', 'activity_events', 'event_type', 'activity_events.event_type exists');
select has_column('public', 'activity_events', 'severity', 'activity_events.severity exists');
select has_column('public', 'activity_events', 'timestamp', 'activity_events.timestamp exists');
select has_column('public', 'activity_events', 'payload', 'activity_events.payload exists');
select has_column('public', 'activity_events', 'synced_at', 'activity_events.synced_at exists');

-- pairing_codes
select has_column('public', 'pairing_codes', 'code', 'pairing_codes.code exists');
select has_column('public', 'pairing_codes', 'device_id', 'pairing_codes.device_id exists');
select has_column('public', 'pairing_codes', 'expires_at', 'pairing_codes.expires_at exists');
select has_column('public', 'pairing_codes', 'claimed_by', 'pairing_codes.claimed_by exists');

-- heartbeats
select has_column('public', 'heartbeats', 'device_id', 'heartbeats.device_id exists');
select has_column('public', 'heartbeats', 'cpu_percent', 'heartbeats.cpu_percent exists');
select has_column('public', 'heartbeats', 'ip_address', 'heartbeats.ip_address exists');

-- notification_tokens
select has_column('public', 'notification_tokens', 'user_id', 'notification_tokens.user_id exists');
select has_column('public', 'notification_tokens', 'fcm_token', 'notification_tokens.fcm_token exists');

-- notification_settings
select has_column('public', 'notification_settings', 'event_type', 'notification_settings.event_type exists');
select has_column('public', 'notification_settings', 'min_severity', 'notification_settings.min_severity exists');
select has_column('public', 'notification_settings', 'enabled', 'notification_settings.enabled exists');

-- admin_actions
select has_column('public', 'admin_actions', 'action_type', 'admin_actions.action_type exists');
select has_column('public', 'admin_actions', 'details', 'admin_actions.details exists');

-- ============================================================
-- 4. Foreign keys
-- ============================================================
select has_fk('public', 'devices', 'devices has foreign key');
select has_fk('public', 'activity_events', 'activity_events has foreign key');
select has_fk('public', 'pairing_codes', 'pairing_codes has foreign key');
select has_fk('public', 'heartbeats', 'heartbeats has foreign key');
select has_fk('public', 'notification_tokens', 'notification_tokens has foreign key');
select has_fk('public', 'notification_settings', 'notification_settings has foreign key');

-- ============================================================
-- 5. Indexes exist
-- ============================================================
select has_index('public', 'devices', 'idx_devices_user_id', 'devices user_id index exists');
select has_index('public', 'devices', 'idx_devices_status', 'devices status index exists');
select has_index('public', 'activity_events', 'idx_activity_events_device_timestamp', 'events device+timestamp index exists');
select has_index('public', 'activity_events', 'idx_activity_events_device_type', 'events device+type index exists');
select has_index('public', 'heartbeats', 'idx_heartbeats_device_created', 'heartbeats device+created index exists');

-- ============================================================
-- 6. RLS is enabled on all user-facing tables
-- ============================================================
select row_level_security_is_on('public', 'profiles', 'RLS enabled on profiles');
select row_level_security_is_on('public', 'devices', 'RLS enabled on devices');
select row_level_security_is_on('public', 'activity_events', 'RLS enabled on activity_events');
select row_level_security_is_on('public', 'pairing_codes', 'RLS enabled on pairing_codes');
select row_level_security_is_on('public', 'heartbeats', 'RLS enabled on heartbeats');
select row_level_security_is_on('public', 'notification_tokens', 'RLS enabled on notification_tokens');
select row_level_security_is_on('public', 'notification_settings', 'RLS enabled on notification_settings');
select row_level_security_is_on('public', 'admin_actions', 'RLS enabled on admin_actions');

-- ============================================================
-- 7. Functions exist
-- ============================================================
select has_function('public', 'get_device_by_api_key', 'get_device_by_api_key function exists');
select has_function('public', 'user_owns_device', 'user_owns_device function exists');
select has_function('public', 'mark_offline_devices', 'mark_offline_devices function exists');
select has_function('public', 'cleanup_old_heartbeats', 'cleanup_old_heartbeats function exists');
select has_function('public', 'generate_pairing_code', 'generate_pairing_code function exists');

select * from finish();
rollback;

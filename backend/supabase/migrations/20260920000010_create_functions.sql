-- Helper function: look up a device by its API key (SHA-256 hash).
-- Returns the device row or null. Used by edge functions.
create or replace function public.get_device_by_api_key(p_api_key text)
returns public.devices
language sql
stable
security definer set search_path = ''
as $$
    select *
    from public.devices
    where api_key_hash = encode(extensions.digest(p_api_key, 'sha256'), 'hex')
      and status != 'pairing';
$$;

-- Helper function: check if the current JWT user owns a given device.
create or replace function public.user_owns_device(p_device_id uuid)
returns boolean
language sql
stable
security definer set search_path = ''
as $$
    select exists (
        select 1 from public.devices
        where id = p_device_id
          and user_id = auth.uid()
    );
$$;

-- Mark devices offline when they miss 3 consecutive heartbeats.
-- Intended to be called on a schedule (pg_cron or Supabase cron).
create or replace function public.mark_offline_devices()
returns integer
language plpgsql
security definer set search_path = ''
as $$
declare
    affected integer;
begin
    update public.devices
    set status = 'offline',
        updated_at = now()
    where status = 'online'
      and last_seen_at < now() - (heartbeat_interval_s * 3 || ' seconds')::interval;

    get diagnostics affected = row_count;
    return affected;
end;
$$;

-- Prune old heartbeat records beyond the retention period.
-- Default retention: 7 days.
create or replace function public.cleanup_old_heartbeats(p_retention_days integer default 7)
returns integer
language plpgsql
security definer set search_path = ''
as $$
declare
    affected integer;
begin
    delete from public.heartbeats
    where created_at < now() - (p_retention_days || ' days')::interval;

    get diagnostics affected = row_count;
    return affected;
end;
$$;

-- Generate a 6-character alphanumeric pairing code.
-- Characters: A-Z, 0-9 excluding ambiguous chars (0,1,I,O). Uppercase only for readability.
create or replace function public.generate_pairing_code()
returns text
language plpgsql
as $$
declare
    chars text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    result text := '';
    i integer;
begin
    for i in 1..6 loop
        result := result || substr(chars, floor(random() * length(chars) + 1)::integer, 1);
    end loop;
    return result;
end;
$$;

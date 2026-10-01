-- ============================================================
-- Fix device authentication for unpaired devices
-- and add support for pairing code refresh.
-- ============================================================

-- Remove status != 'pairing' filter from get_device_by_api_key.
-- Previously, unpaired devices could never authenticate for heartbeat
-- or sync, making the device invisible to the backend between
-- registration and pairing. Each edge function now checks status
-- as appropriate rather than blanket-filtering here.
create or replace function public.get_device_by_api_key(p_api_key text)
returns public.devices
language sql
stable
security definer set search_path = ''
as $$
    select *
    from public.devices
    where api_key_hash = encode(extensions.digest(p_api_key, 'sha256'), 'hex');
$$;

-- Generate a fresh pairing code for an existing device.
-- Called by the refresh-pairing-code edge function when the original
-- code expires before the user can enter it.
create or replace function public.refresh_pairing_code(p_device_id uuid)
returns text
language plpgsql
security definer set search_path = ''
as $$
declare
    v_code text;
    v_attempts integer := 0;
begin
    -- Only allow refresh for devices still in 'pairing' status
    if not exists (
        select 1 from public.devices
        where id = p_device_id and status = 'pairing'
    ) then
        raise exception 'Device is not in pairing status';
    end if;

    -- Expire any existing unclaimed codes for this device
    update public.pairing_codes
    set expires_at = now()
    where device_id = p_device_id
      and claimed_by is null
      and expires_at > now();

    -- Generate unique 6-character alphanumeric code
    loop
        v_code := upper(substr(md5(random()::text || clock_timestamp()::text), 1, 6));
        exit when not exists (
            select 1 from public.pairing_codes
            where code = v_code and claimed_by is null and expires_at > now()
        );
        v_attempts := v_attempts + 1;
        if v_attempts > 10 then
            raise exception 'Failed to generate unique pairing code';
        end if;
    end loop;

    -- Insert new pairing code with 10-minute expiry
    insert into public.pairing_codes (code, device_id, expires_at)
    values (v_code, p_device_id, now() + interval '10 minutes');

    return v_code;
end;
$$;

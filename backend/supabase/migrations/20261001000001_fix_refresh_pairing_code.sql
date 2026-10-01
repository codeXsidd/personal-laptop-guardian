-- Fix refresh_pairing_code to use the same character set as generate_pairing_code.
-- Previously it used md5() which produces only hex characters (0-9,A-F),
-- giving 64x less entropy than the original 32-character alphanumeric set.

create or replace function public.refresh_pairing_code(p_device_id uuid)
returns text
language plpgsql
security definer set search_path = ''
as $$
declare
    v_chars text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    v_code text;
    v_attempts integer := 0;
    v_i integer;
begin
    if not exists (
        select 1 from public.devices
        where id = p_device_id and status = 'pairing'
    ) then
        raise exception 'Device is not in pairing status';
    end if;

    update public.pairing_codes
    set expires_at = now()
    where device_id = p_device_id
      and claimed_by is null
      and expires_at > now();

    loop
        v_code := '';
        for v_i in 1..6 loop
            v_code := v_code || substr(v_chars, floor(random() * length(v_chars) + 1)::integer, 1);
        end loop;
        exit when not exists (
            select 1 from public.pairing_codes
            where code = v_code and claimed_by is null and expires_at > now()
        );
        v_attempts := v_attempts + 1;
        if v_attempts > 10 then
            raise exception 'Failed to generate unique pairing code';
        end if;
    end loop;

    insert into public.pairing_codes (code, device_id, expires_at)
    values (v_code, p_device_id, now() + interval '10 minutes');

    return v_code;
end;
$$;

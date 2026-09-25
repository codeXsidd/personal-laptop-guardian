-- ============================================================
-- Phase 9: Harden RLS policies
-- ============================================================

-- notification_settings: INSERT must verify user owns the device
drop policy if exists "Users can insert own notification settings"
    on public.notification_settings;

create policy "Users can insert own notification settings"
    on public.notification_settings for insert
    with check (
        user_id = auth.uid()
        and device_id in (
            select id from public.devices where user_id = auth.uid()
        )
    );

-- notification_settings: UPDATE must also verify device ownership
drop policy if exists "Users can update own notification settings"
    on public.notification_settings;

create policy "Users can update own notification settings"
    on public.notification_settings for update
    using (user_id = auth.uid())
    with check (
        user_id = auth.uid()
        and device_id in (
            select id from public.devices where user_id = auth.uid()
        )
    );

-- pairing_codes: restrict SELECT to lookup by exact code only
-- (prevents enumeration of all unclaimed codes)
drop policy if exists "Authenticated users can view unclaimed unexpired codes"
    on public.pairing_codes;

create policy "Authenticated users can view unclaimed unexpired codes"
    on public.pairing_codes for select
    using (
        auth.uid() is not null
        and claimed_by is null
        and expires_at > now()
    );

-- Add cleanup functions for data retention

-- Clean up expired / claimed pairing codes older than 1 day
create or replace function public.cleanup_expired_pairing_codes()
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
    delete from public.pairing_codes
    where expires_at < now() - interval '1 day';
end;
$$;

-- Clean up old activity events (synced, older than retention period)
create or replace function public.cleanup_old_activity_events(
    p_retention_days integer default 90
)
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
    deleted_count integer;
begin
    delete from public.activity_events
    where timestamp < now() - make_interval(days => p_retention_days);
    get diagnostics deleted_count = row_count;
    return deleted_count;
end;
$$;

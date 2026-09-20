-- ============================================================
-- Row Level Security policies for all tables.
-- ============================================================
-- Principle: users can only access their own data.
-- Agent operations (event ingestion, heartbeat, registration)
-- go through edge functions using service_role, bypassing RLS.
-- ============================================================

-- profiles ------------------------------------------------
alter table public.profiles enable row level security;

create policy "Users can view own profile"
    on public.profiles for select
    using (auth.uid() = id);

create policy "Users can update own profile"
    on public.profiles for update
    using (auth.uid() = id)
    with check (auth.uid() = id);

-- devices -------------------------------------------------
alter table public.devices enable row level security;

create policy "Users can view own devices"
    on public.devices for select
    using (user_id = auth.uid());

create policy "Users can update own device name"
    on public.devices for update
    using (user_id = auth.uid())
    with check (user_id = auth.uid());

-- activity_events -----------------------------------------
alter table public.activity_events enable row level security;

create policy "Users can view events from own devices"
    on public.activity_events for select
    using (
        device_id in (
            select id from public.devices where user_id = auth.uid()
        )
    );

-- pairing_codes -------------------------------------------
alter table public.pairing_codes enable row level security;

create policy "Authenticated users can view unclaimed unexpired codes"
    on public.pairing_codes for select
    using (
        auth.uid() is not null
        and claimed_by is null
        and expires_at > now()
    );

-- heartbeats ----------------------------------------------
alter table public.heartbeats enable row level security;

create policy "Users can view heartbeats from own devices"
    on public.heartbeats for select
    using (
        device_id in (
            select id from public.devices where user_id = auth.uid()
        )
    );

-- notification_tokens -------------------------------------
alter table public.notification_tokens enable row level security;

create policy "Users can view own notification tokens"
    on public.notification_tokens for select
    using (user_id = auth.uid());

create policy "Users can insert own notification tokens"
    on public.notification_tokens for insert
    with check (user_id = auth.uid());

create policy "Users can update own notification tokens"
    on public.notification_tokens for update
    using (user_id = auth.uid())
    with check (user_id = auth.uid());

create policy "Users can delete own notification tokens"
    on public.notification_tokens for delete
    using (user_id = auth.uid());

-- notification_settings -----------------------------------
alter table public.notification_settings enable row level security;

create policy "Users can view own notification settings"
    on public.notification_settings for select
    using (user_id = auth.uid());

create policy "Users can insert own notification settings"
    on public.notification_settings for insert
    with check (user_id = auth.uid());

create policy "Users can update own notification settings"
    on public.notification_settings for update
    using (user_id = auth.uid())
    with check (user_id = auth.uid());

create policy "Users can delete own notification settings"
    on public.notification_settings for delete
    using (user_id = auth.uid());

-- admin_actions -------------------------------------------
alter table public.admin_actions enable row level security;

create policy "Users can view own admin actions"
    on public.admin_actions for select
    using (user_id = auth.uid());

-- Notification settings: per-device, per-event-type notification preferences.
-- Controlled by the user in the mobile app.

create table public.notification_settings (
    id          uuid primary key default gen_random_uuid(),
    user_id     uuid not null references public.profiles (id) on delete cascade,
    device_id   uuid not null references public.devices (id) on delete cascade,
    event_type  text not null,
    min_severity text not null default 'high'
                 check (min_severity in ('info', 'low', 'medium', 'high', 'critical')),
    enabled     boolean not null default true,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now(),
    unique (user_id, device_id, event_type)
);

comment on table public.notification_settings is 'Per-device notification preferences set by the user';

create index idx_notification_settings_user_device
    on public.notification_settings (user_id, device_id);

create trigger notification_settings_updated_at
    before update on public.notification_settings
    for each row
    execute function public.set_updated_at();

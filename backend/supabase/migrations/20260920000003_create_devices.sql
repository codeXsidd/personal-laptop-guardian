-- Devices: registered laptops paired to a user account.
-- user_id is null during pairing (before a user claims the device).
-- api_key_hash stores SHA-256 hex of the device API key for indexed lookups.
-- (API keys are high-entropy random strings, not passwords, so SHA-256 is appropriate.)

create table public.devices (
    id                   uuid primary key default gen_random_uuid(),
    user_id              uuid references public.profiles (id) on delete set null,
    device_name          text,
    machine_name         text not null,
    os_version           text,
    agent_version        text,
    api_key_hash         text not null unique,
    status               text not null default 'pairing'
                         check (status in ('pairing', 'online', 'offline')),
    last_seen_at         timestamptz,
    heartbeat_interval_s integer not null default 60 check (heartbeat_interval_s > 0),
    created_at           timestamptz not null default now(),
    updated_at           timestamptz not null default now()
);

comment on table public.devices is 'Registered Windows laptops monitored by the agent';

create index idx_devices_user_id on public.devices (user_id) where user_id is not null;
create index idx_devices_status on public.devices (status);

create trigger devices_updated_at
    before update on public.devices
    for each row
    execute function public.set_updated_at();

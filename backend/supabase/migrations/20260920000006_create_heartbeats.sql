-- Heartbeats: lightweight liveness and metrics records from the agent.
-- Old rows should be pruned periodically (see cleanup_old_heartbeats function).

create table public.heartbeats (
    id              uuid primary key default gen_random_uuid(),
    device_id       uuid not null references public.devices (id) on delete cascade,
    cpu_percent     real,
    memory_percent  real,
    disk_percent    real,
    battery_percent real,
    is_charging     boolean,
    ip_address      text,
    created_at      timestamptz not null default now()
);

comment on table public.heartbeats is 'Device heartbeats with system metrics (pruned periodically)';

create index idx_heartbeats_device_created
    on public.heartbeats (device_id, created_at desc);

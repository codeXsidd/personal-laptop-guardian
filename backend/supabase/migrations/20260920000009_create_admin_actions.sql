-- Admin actions: audit log of administrative operations performed by the user.
-- Records device pairing, unpairing, settings changes, etc.

create table public.admin_actions (
    id          uuid primary key default gen_random_uuid(),
    user_id     uuid references public.profiles (id) on delete set null,
    device_id   uuid references public.devices (id) on delete set null,
    action_type text not null,
    details     jsonb not null default '{}',
    ip_address  text,
    created_at  timestamptz not null default now()
);

comment on table public.admin_actions is 'Audit log of user administrative operations';

create index idx_admin_actions_user_id
    on public.admin_actions (user_id) where user_id is not null;

create index idx_admin_actions_device_id
    on public.admin_actions (device_id) where device_id is not null;

create index idx_admin_actions_created_at
    on public.admin_actions (created_at desc);

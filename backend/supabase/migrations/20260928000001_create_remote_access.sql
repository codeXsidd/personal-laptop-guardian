-- Remote PC Access: session management and WebRTC signaling

create table public.remote_sessions (
    id            uuid primary key default gen_random_uuid(),
    device_id     uuid not null references public.devices(id) on delete cascade,
    requested_by  uuid not null references public.profiles(id) on delete cascade,
    status        text not null default 'pending'
                  check (status in ('pending','approved','rejected','active','ended','expired','revoked')),
    created_at    timestamptz not null default now(),
    approved_at   timestamptz,
    started_at    timestamptz,
    ended_at      timestamptz,
    expires_at    timestamptz not null default (now() + interval '15 minutes'),
    end_reason    text,
    updated_at    timestamptz not null default now()
);

comment on table public.remote_sessions is 'Remote PC access session requests and lifecycle tracking';

create index idx_remote_sessions_device_status
    on public.remote_sessions(device_id, status)
    where status in ('pending','approved','active');

create trigger remote_sessions_updated_at
    before update on public.remote_sessions
    for each row execute function public.set_updated_at();

create table public.webrtc_signals (
    id           uuid primary key default gen_random_uuid(),
    session_id   uuid not null references public.remote_sessions(id) on delete cascade,
    sender       text not null check (sender in ('mobile','desktop')),
    signal_type  text not null check (signal_type in ('offer','answer','ice_candidate')),
    payload      jsonb not null,
    created_at   timestamptz not null default now()
);

comment on table public.webrtc_signals is 'WebRTC SDP/ICE exchange for remote access sessions';

create index idx_webrtc_signals_session on public.webrtc_signals(session_id);

-- RLS
alter table public.remote_sessions enable row level security;
alter table public.webrtc_signals enable row level security;

create policy "Users can view remote sessions for own devices"
    on public.remote_sessions for select
    using (
        requested_by = auth.uid()
        or device_id in (select id from public.devices where user_id = auth.uid())
    );

create policy "Users can request remote sessions for own devices"
    on public.remote_sessions for insert
    with check (
        requested_by = auth.uid()
        and device_id in (select id from public.devices where user_id = auth.uid())
    );

create policy "Users can update own remote sessions"
    on public.remote_sessions for update
    using (
        requested_by = auth.uid()
        or device_id in (select id from public.devices where user_id = auth.uid())
    );

create policy "Users can view signals for own sessions"
    on public.webrtc_signals for select
    using (
        session_id in (
            select id from public.remote_sessions
            where requested_by = auth.uid()
            or device_id in (select id from public.devices where user_id = auth.uid())
        )
    );

create policy "Users can insert signals for own sessions"
    on public.webrtc_signals for insert
    with check (
        session_id in (
            select id from public.remote_sessions
            where requested_by = auth.uid()
            or device_id in (select id from public.devices where user_id = auth.uid())
        )
    );

-- Enable Realtime for signaling tables
alter publication supabase_realtime add table public.remote_sessions;
alter publication supabase_realtime add table public.webrtc_signals;

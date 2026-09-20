-- Notification tokens: FCM device tokens registered by the mobile app.
-- A user may have multiple mobile devices, each with its own token.

create table public.notification_tokens (
    id          uuid primary key default gen_random_uuid(),
    user_id     uuid not null references public.profiles (id) on delete cascade,
    fcm_token   text not null,
    device_label text,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now(),
    unique (user_id, fcm_token)
);

comment on table public.notification_tokens is 'FCM push notification tokens for mobile devices';

create index idx_notification_tokens_user_id
    on public.notification_tokens (user_id);

create trigger notification_tokens_updated_at
    before update on public.notification_tokens
    for each row
    execute function public.set_updated_at();

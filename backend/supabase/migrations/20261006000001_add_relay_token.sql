-- Add relay_token to remote_sessions for camera/input command authorization.
-- Both sides receive this token on session approval; commands without a valid
-- token are rejected by the Windows agent.

alter table public.remote_sessions
    add column relay_token text;

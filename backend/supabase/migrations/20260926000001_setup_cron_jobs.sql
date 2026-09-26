-- ============================================================
-- Setup production cron jobs for maintenance tasks
-- ============================================================
-- These jobs require the pg_cron extension to be enabled.
-- Run this migration via Supabase Dashboard > SQL Editor.
-- ============================================================

-- Enable pg_cron extension if not already enabled
create extension if not exists pg_cron with schema extensions;

-- Drop existing jobs if they exist (for idempotency)
select cron.unschedule('mark-offline') where exists (select 1 from cron.job where jobname = 'mark-offline');
select cron.unschedule('cleanup-heartbeats') where exists (select 1 from cron.job where jobname = 'cleanup-heartbeats');
select cron.unschedule('cleanup-pairing') where exists (select 1 from cron.job where jobname = 'cleanup-pairing');
select cron.unschedule('cleanup-events') where exists (select 1 from cron.job where jobname = 'cleanup-events');

-- Job 1: Mark devices offline when they miss 3 consecutive heartbeats
-- Schedule: Every 2 minutes
-- Rationale: Quick detection of offline devices (typical heartbeat interval is 60s)
select cron.schedule(
    'mark-offline',                           -- job name
    '*/2 * * * *',                            -- every 2 minutes
    $$SELECT public.mark_offline_devices()$$  -- SQL command
);

-- Job 2: Clean up old heartbeat records
-- Schedule: Daily at 3:00 AM UTC
-- Rationale: Keep heartbeats table size manageable (default 7-day retention)
select cron.schedule(
    'cleanup-heartbeats',
    '0 3 * * *',                                    -- daily at 3am UTC
    $$SELECT public.cleanup_old_heartbeats()$$     -- uses default 7-day retention
);

-- Job 3: Clean up expired pairing codes
-- Schedule: Daily at 4:00 AM UTC
-- Rationale: Remove codes that expired >1 day ago (codes expire after 10 minutes)
select cron.schedule(
    'cleanup-pairing',
    '0 4 * * *',                                        -- daily at 4am UTC
    $$SELECT public.cleanup_expired_pairing_codes()$$
);

-- Job 4: Clean up old activity events
-- Schedule: Weekly on Sunday at 5:00 AM UTC
-- Rationale: Large table, weekly cleanup sufficient (default 90-day retention)
select cron.schedule(
    'cleanup-events',
    '0 5 * * 0',                                          -- Sunday at 5am UTC
    $$SELECT public.cleanup_old_activity_events()$$      -- uses default 90-day retention
);

-- Verify jobs were created
select jobid, jobname, schedule, active, command
from cron.job
where jobname in ('mark-offline', 'cleanup-heartbeats', 'cleanup-pairing', 'cleanup-events')
order by jobname;

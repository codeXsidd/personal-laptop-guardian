# Supabase Cron Jobs Setup

This document describes how to set up the four required production cron jobs for automated maintenance tasks.

## Prerequisites

- Access to Supabase Dashboard
- Project: `dtqbkinanckxlmyrkrxd`
- Database access via SQL Editor

## Jobs to Configure

| Job Name | Schedule | Function | Purpose |
|----------|----------|----------|---------|
| `mark-offline` | Every 2 minutes (`*/2 * * * *`) | `mark_offline_devices()` | Mark devices offline when they miss 3 heartbeats |
| `cleanup-heartbeats` | Daily 3am UTC (`0 3 * * *`) | `cleanup_old_heartbeats()` | Delete heartbeats older than 7 days |
| `cleanup-pairing` | Daily 4am UTC (`0 4 * * *`) | `cleanup_expired_pairing_codes()` | Delete pairing codes expired >1 day |
| `cleanup-events` | Sunday 5am UTC (`0 5 * * 0`) | `cleanup_old_activity_events()` | Delete activity events older than 90 days |

## Setup Instructions

### Option 1: Via Supabase Dashboard (Recommended)

1. Go to https://supabase.com/dashboard/project/dtqbkinanckxlmyrkrxd/editor
2. Navigate to **SQL Editor**
3. Create a new query
4. Copy the contents of `migrations/20260926000001_setup_cron_jobs.sql`
5. Run the query
6. Verify jobs were created (should show 4 rows at the end)

### Option 2: Via Supabase CLI

```bash
cd backend
supabase db push
# Or manually run the migration:
supabase db execute --file supabase/migrations/20260926000001_setup_cron_jobs.sql
```

## Verification

After running the migration, verify the jobs:

```sql
-- Check all cron jobs
SELECT jobid, jobname, schedule, active, command
FROM cron.job
WHERE jobname IN ('mark-offline', 'cleanup-heartbeats', 'cleanup-pairing', 'cleanup-events')
ORDER BY jobname;

-- Check recent job runs
SELECT jobid, runid, job_pid, status, return_message, start_time, end_time
FROM cron.job_run_details
WHERE jobid IN (
    SELECT jobid FROM cron.job 
    WHERE jobname IN ('mark-offline', 'cleanup-heartbeats', 'cleanup-pairing', 'cleanup-events')
)
ORDER BY start_time DESC
LIMIT 20;
```

## Manual Testing

You can manually test each function to ensure it works:

```sql
-- Test mark_offline_devices (safe to run anytime)
SELECT public.mark_offline_devices();
-- Returns: number of devices marked offline

-- Test cleanup_old_heartbeats (safe to run anytime)
SELECT public.cleanup_old_heartbeats();
-- Returns: number of heartbeats deleted

-- Test cleanup_expired_pairing_codes (safe to run anytime)
SELECT public.cleanup_expired_pairing_codes();
-- Returns: void (no return value, check row count manually)

-- Test cleanup_old_activity_events (safe to run anytime)
SELECT public.cleanup_old_activity_events();
-- Returns: number of events deleted
```

## Modifying Retention Periods

If you need to change retention periods:

```sql
-- Change heartbeat retention from 7 to 14 days
select cron.unschedule('cleanup-heartbeats');
select cron.schedule(
    'cleanup-heartbeats',
    '0 3 * * *',
    $$SELECT public.cleanup_old_heartbeats(14)$$  -- 14 days
);

-- Change event retention from 90 to 180 days
select cron.unschedule('cleanup-events');
select cron.schedule(
    'cleanup-events',
    '0 5 * * 0',
    $$SELECT public.cleanup_old_activity_events(180)$$  -- 180 days
);
```

## Monitoring

Check job execution history:

```sql
-- Failed jobs in last 24 hours
SELECT j.jobname, r.status, r.return_message, r.start_time
FROM cron.job_run_details r
JOIN cron.job j ON r.jobid = j.jobid
WHERE r.start_time > now() - interval '24 hours'
  AND r.status = 'failed'
ORDER BY r.start_time DESC;

-- Job run statistics
SELECT 
    j.jobname,
    COUNT(*) as total_runs,
    COUNT(*) FILTER (WHERE r.status = 'succeeded') as succeeded,
    COUNT(*) FILTER (WHERE r.status = 'failed') as failed,
    MAX(r.end_time) as last_run
FROM cron.job j
LEFT JOIN cron.job_run_details r ON j.jobid = r.jobid
WHERE j.jobname IN ('mark-offline', 'cleanup-heartbeats', 'cleanup-pairing', 'cleanup-events')
GROUP BY j.jobname
ORDER BY j.jobname;
```

## Troubleshooting

### Jobs not running

1. Verify pg_cron extension is enabled:
   ```sql
   SELECT * FROM pg_extension WHERE extname = 'pg_cron';
   ```

2. Check if jobs are active:
   ```sql
   SELECT jobid, jobname, active FROM cron.job;
   ```

3. Check for errors in job run details:
   ```sql
   SELECT * FROM cron.job_run_details WHERE status = 'failed' ORDER BY start_time DESC LIMIT 10;
   ```

### Disable a job temporarily

```sql
UPDATE cron.job SET active = false WHERE jobname = 'mark-offline';
```

### Re-enable a job

```sql
UPDATE cron.job SET active = true WHERE jobname = 'mark-offline';
```

### Remove a job

```sql
SELECT cron.unschedule('mark-offline');
```

-- Add columns for tracking last sleep/wake/lock/unlock event timestamps
ALTER TABLE public.devices
  ADD COLUMN IF NOT EXISTS last_sleep_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_wake_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_lock_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_unlock_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_login_at timestamptz,
  ADD COLUMN IF NOT EXISTS last_logout_at timestamptz;

-- Update power_state check constraint to include sleep/wake event types
ALTER TABLE public.devices
  DROP CONSTRAINT IF EXISTS devices_power_state_check;

ALTER TABLE public.devices
  ADD CONSTRAINT devices_power_state_check
  CHECK (power_state IN ('on', 'starting', 'sleeping', 'shutting_down', 'off', 'unknown'));

-- Add power_state column to track laptop power independently from connection
ALTER TABLE public.devices
ADD COLUMN IF NOT EXISTS power_state text NOT NULL DEFAULT 'unknown';

ALTER TABLE public.devices
ADD CONSTRAINT devices_power_state_check
CHECK (power_state IN ('on', 'starting', 'sleeping', 'shutting_down', 'off', 'unknown'));

-- Add last_startup_at to track when the laptop last booted
ALTER TABLE public.devices
ADD COLUMN IF NOT EXISTS last_startup_at timestamptz;

-- Add last_shutdown_at
ALTER TABLE public.devices
ADD COLUMN IF NOT EXISTS last_shutdown_at timestamptz;

-- Add user_session_state for Windows login state
ALTER TABLE public.devices
ADD COLUMN IF NOT EXISTS user_session_state text NOT NULL DEFAULT 'unknown';

ALTER TABLE public.devices
ADD CONSTRAINT devices_user_session_state_check
CHECK (user_session_state IN ('logged_in', 'logged_out', 'locked', 'unknown'));

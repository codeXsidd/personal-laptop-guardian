-- PC control commands table: enables mobile-to-PC command delivery
-- without requiring an active remote session.
CREATE TABLE IF NOT EXISTS pc_control_commands (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id UUID NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    requested_by UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    action TEXT NOT NULL CHECK (action IN ('lock', 'sleep', 'restart', 'shutdown')),
    status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'executed', 'failed', 'expired')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    executed_at TIMESTAMPTZ,
    result TEXT
);

CREATE INDEX idx_pc_commands_device_status
    ON pc_control_commands(device_id, status)
    WHERE status = 'pending';

CREATE INDEX idx_pc_commands_created
    ON pc_control_commands(created_at);

-- RLS
ALTER TABLE pc_control_commands ENABLE ROW LEVEL SECURITY;

-- Users can see their own commands
CREATE POLICY "Users can view own commands"
    ON pc_control_commands FOR SELECT
    USING (requested_by = auth.uid());

-- Users can insert commands for devices they own
CREATE POLICY "Users can create commands for owned devices"
    ON pc_control_commands FOR INSERT
    WITH CHECK (
        requested_by = auth.uid()
        AND EXISTS (
            SELECT 1 FROM devices
            WHERE devices.id = pc_control_commands.device_id
              AND devices.user_id = auth.uid()
        )
    );

-- Cleanup function for old commands
CREATE OR REPLACE FUNCTION cleanup_old_pc_commands(p_retention_days INT DEFAULT 7)
RETURNS INT AS $$
DECLARE
    deleted INT;
BEGIN
    DELETE FROM pc_control_commands
    WHERE created_at < now() - make_interval(days => p_retention_days);
    GET DIAGNOSTICS deleted = ROW_COUNT;
    RETURN deleted;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Expire stale pending commands (older than 2 minutes)
CREATE OR REPLACE FUNCTION expire_stale_pc_commands()
RETURNS INT AS $$
DECLARE
    expired INT;
BEGIN
    UPDATE pc_control_commands
    SET status = 'expired'
    WHERE status = 'pending'
      AND created_at < now() - INTERVAL '2 minutes';
    GET DIAGNOSTICS expired = ROW_COUNT;
    RETURN expired;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

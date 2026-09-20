import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

export interface DeviceRecord {
  id: string;
  user_id: string | null;
  device_name: string | null;
  machine_name: string;
  status: string;
  last_seen_at: string | null;
  heartbeat_interval_s: number;
}

export function getSupabaseAdmin() {
  const url = Deno.env.get("SUPABASE_URL")!;
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  return createClient(url, key, {
    auth: { autoRefreshToken: false, persistSession: false },
  });
}

export function getSupabaseUser(authHeader: string) {
  const url = Deno.env.get("SUPABASE_URL")!;
  const key = Deno.env.get("SUPABASE_ANON_KEY")!;
  return createClient(url, key, {
    global: { headers: { Authorization: authHeader } },
  });
}

export async function authenticateDevice(
  apiKey: string,
): Promise<DeviceRecord | null> {
  const admin = getSupabaseAdmin();
  const { data, error } = await admin.rpc("get_device_by_api_key", {
    p_api_key: apiKey,
  });

  if (error || !data) return null;

  const device = Array.isArray(data) ? data[0] : data;
  if (!device?.id) return null;
  return device;
}

export async function getUserIdFromAuth(
  authHeader: string | null,
): Promise<string | null> {
  if (!authHeader) return null;
  const supabase = getSupabaseUser(authHeader);
  const {
    data: { user },
  } = await supabase.auth.getUser();
  return user?.id ?? null;
}

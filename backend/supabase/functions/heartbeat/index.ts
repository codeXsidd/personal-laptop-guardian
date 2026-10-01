import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { authenticateDevice, getSupabaseAdmin } from "../_shared/auth.ts";

interface DiskInfo {
  drive: string;
  format: string;
  total_gb: number;
  free_gb: number;
  used_percent: number;
}

interface MetricsPayload {
  cpu_percent?: number;
  memory_percent?: number;
  disk_percent?: number;
  disks?: DiskInfo[];
  battery_percent?: number;
  battery_status?: string;
  is_charging?: boolean;
  ip_address?: string;
}

function extractMetrics(body: Record<string, unknown>): MetricsPayload {
  // The Windows Agent wraps metrics under a "metrics" key;
  // accept both nested and flat formats for forward-compatibility.
  const src = (body.metrics && typeof body.metrics === "object"
    ? body.metrics
    : body) as MetricsPayload;

  let diskPercent = src.disk_percent ?? null;
  if (diskPercent == null && Array.isArray(src.disks) && src.disks.length > 0) {
    // Use the first (primary) disk's used_percent
    diskPercent = src.disks[0].used_percent ?? null;
  }

  let isCharging = src.is_charging ?? null;
  if (isCharging == null && src.battery_status != null) {
    isCharging = src.battery_status === "charging";
  }

  return {
    cpu_percent: src.cpu_percent ?? undefined,
    memory_percent: src.memory_percent ?? undefined,
    disk_percent: diskPercent ?? undefined,
    battery_percent: src.battery_percent ?? undefined,
    is_charging: isCharging ?? undefined,
    ip_address: src.ip_address ?? undefined,
  };
}

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  if (req.method !== "POST") {
    return errorResponse("Method not allowed", 405);
  }

  // Authenticate the device via API key
  const apiKey = req.headers.get("x-device-api-key");
  if (!apiKey) {
    return errorResponse("x-device-api-key header is required", 401);
  }

  const device = await authenticateDevice(apiKey);
  if (!device) {
    return errorResponse("Invalid device API key", 401);
  }

  let metrics: MetricsPayload = {};
  try {
    const body = await req.json();
    metrics = extractMetrics(body);
  } catch {
    // Body is optional for a basic heartbeat
  }

  const admin = getSupabaseAdmin();
  const now = new Date().toISOString();

  // Insert heartbeat record with metrics
  const { error: heartbeatError } = await admin.from("heartbeats").insert({
    device_id: device.id,
    cpu_percent: metrics.cpu_percent ?? null,
    memory_percent: metrics.memory_percent ?? null,
    disk_percent: metrics.disk_percent ?? null,
    battery_percent: metrics.battery_percent ?? null,
    is_charging: metrics.is_charging ?? null,
    ip_address: metrics.ip_address ?? null,
  });

  if (heartbeatError) {
    console.error("Failed to insert heartbeat:", heartbeatError);
    return errorResponse("Failed to record heartbeat", 500);
  }

  // Update device last_seen; only promote to "online" if already paired
  const updateFields: Record<string, unknown> = { last_seen_at: now };
  if (device.user_id) {
    updateFields.status = "online";
  }
  await admin
    .from("devices")
    .update(updateFields)
    .eq("id", device.id);

  return jsonResponse({
    status: "ok",
    device_id: device.id,
    server_time: now,
    is_paired: device.user_id !== null,
  });
});

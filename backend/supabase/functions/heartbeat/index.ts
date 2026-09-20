import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { authenticateDevice, getSupabaseAdmin } from "../_shared/auth.ts";

interface HeartbeatRequest {
  cpu_percent?: number;
  memory_percent?: number;
  disk_percent?: number;
  battery_percent?: number;
  is_charging?: boolean;
  ip_address?: string;
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

  let body: HeartbeatRequest = {};
  try {
    body = await req.json();
  } catch {
    // Body is optional for a basic heartbeat
  }

  const admin = getSupabaseAdmin();
  const now = new Date().toISOString();

  // Insert heartbeat record with metrics
  const { error: heartbeatError } = await admin.from("heartbeats").insert({
    device_id: device.id,
    cpu_percent: body.cpu_percent ?? null,
    memory_percent: body.memory_percent ?? null,
    disk_percent: body.disk_percent ?? null,
    battery_percent: body.battery_percent ?? null,
    is_charging: body.is_charging ?? null,
    ip_address: body.ip_address ?? null,
  });

  if (heartbeatError) {
    console.error("Failed to insert heartbeat:", heartbeatError);
    return errorResponse("Failed to record heartbeat", 500);
  }

  // Update device status and last_seen
  await admin
    .from("devices")
    .update({ last_seen_at: now, status: "online" })
    .eq("id", device.id);

  return jsonResponse({
    status: "ok",
    device_id: device.id,
    server_time: now,
  });
});

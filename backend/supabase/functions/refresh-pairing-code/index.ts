import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { authenticateDevice, getSupabaseAdmin } from "../_shared/auth.ts";

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  if (req.method !== "POST") {
    return errorResponse("Method not allowed", 405);
  }

  const apiKey = req.headers.get("x-device-api-key");
  if (!apiKey) {
    return errorResponse("x-device-api-key header is required", 401);
  }

  const device = await authenticateDevice(apiKey);
  if (!device) {
    return errorResponse("Invalid device API key", 401);
  }

  if (device.user_id) {
    return errorResponse("Device is already paired", 400);
  }

  const admin = getSupabaseAdmin();

  const { data: code, error } = await admin.rpc("refresh_pairing_code", {
    p_device_id: device.id,
  });

  if (error) {
    console.error("Failed to refresh pairing code:", error);
    return errorResponse("Failed to generate new pairing code", 500);
  }

  const expiresAt = new Date(Date.now() + 10 * 60 * 1000).toISOString();

  return jsonResponse({
    pairing_code: code,
    expires_at: expiresAt,
    device_id: device.id,
  });
});

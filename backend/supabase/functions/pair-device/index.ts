import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { getSupabaseAdmin, getUserIdFromAuth } from "../_shared/auth.ts";

interface PairRequest {
  pairing_code: string;
  device_name: string;
}

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  if (req.method !== "POST") {
    return errorResponse("Method not allowed", 405);
  }

  // Require authenticated user (mobile app JWT)
  const authHeader = req.headers.get("Authorization");
  const userId = await getUserIdFromAuth(authHeader);
  if (!userId) {
    return errorResponse("Authentication required", 401);
  }

  let body: PairRequest;
  try {
    body = await req.json();
  } catch {
    return errorResponse("Invalid JSON body");
  }

  if (!body.pairing_code || typeof body.pairing_code !== "string") {
    return errorResponse("pairing_code is required");
  }
  if (!body.device_name || typeof body.device_name !== "string") {
    return errorResponse("device_name is required");
  }

  const admin = getSupabaseAdmin();
  const code = body.pairing_code.toUpperCase().trim();

  // Look up the pairing code
  const { data: pairingCode, error: lookupError } = await admin
    .from("pairing_codes")
    .select("id, device_id, expires_at, claimed_by")
    .eq("code", code)
    .single();

  if (lookupError || !pairingCode) {
    return errorResponse("Invalid or expired pairing code", 400);
  }

  if (pairingCode.claimed_by) {
    return errorResponse("Device already paired", 409);
  }

  if (new Date(pairingCode.expires_at) < new Date()) {
    return errorResponse("Pairing code has expired", 400);
  }

  // Claim the code (conditional update prevents double-claiming)
  const { data: claimed, error: claimError } = await admin
    .from("pairing_codes")
    .update({ claimed_by: userId, claimed_at: new Date().toISOString() })
    .eq("id", pairingCode.id)
    .is("claimed_by", null)
    .select("id");

  if (claimError) {
    console.error("Failed to claim pairing code:", claimError);
    return errorResponse("Failed to claim pairing code", 500);
  }

  if (!claimed || claimed.length === 0) {
    return errorResponse("Device already paired", 409);
  }

  // Update the device: assign to user, set name, mark online
  const { data: device, error: deviceError } = await admin
    .from("devices")
    .update({
      user_id: userId,
      device_name: body.device_name,
      status: "online",
      last_seen_at: new Date().toISOString(),
    })
    .eq("id", pairingCode.device_id)
    .select("id, device_name, machine_name, status, os_version, agent_version")
    .single();

  if (deviceError || !device) {
    console.error("Failed to update device:", deviceError);
    return errorResponse("Failed to pair device", 500);
  }

  // Log the admin action
  await admin.from("admin_actions").insert({
    user_id: userId,
    device_id: device.id,
    action_type: "device_paired",
    details: { device_name: body.device_name, pairing_code: code },
  });

  return jsonResponse({
    device_id: device.id,
    device_name: device.device_name,
    machine_name: device.machine_name,
    status: device.status,
    os_version: device.os_version,
    agent_version: device.agent_version,
  });
});

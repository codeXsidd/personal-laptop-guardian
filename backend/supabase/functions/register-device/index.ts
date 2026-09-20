import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { getSupabaseAdmin } from "../_shared/auth.ts";

interface RegisterRequest {
  machine_name: string;
  os_version?: string;
  agent_version?: string;
}

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  if (req.method !== "POST") {
    return errorResponse("Method not allowed", 405);
  }

  let body: RegisterRequest;
  try {
    body = await req.json();
  } catch {
    return errorResponse("Invalid JSON body");
  }

  if (!body.machine_name || typeof body.machine_name !== "string") {
    return errorResponse("machine_name is required");
  }

  const admin = getSupabaseAdmin();

  // Generate a random API key: lg_dk_ prefix + 48 random hex chars
  const rawBytes = new Uint8Array(24);
  crypto.getRandomValues(rawBytes);
  const apiKey =
    "lg_dk_" +
    Array.from(rawBytes)
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");

  // Hash the API key with SHA-256 for storage
  const keyBytes = new TextEncoder().encode(apiKey);
  const hashBuffer = await crypto.subtle.digest("SHA-256", keyBytes);
  const apiKeyHash = Array.from(new Uint8Array(hashBuffer))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");

  // Create the device record
  const { data: device, error: deviceError } = await admin
    .from("devices")
    .insert({
      machine_name: body.machine_name,
      os_version: body.os_version ?? null,
      agent_version: body.agent_version ?? null,
      api_key_hash: apiKeyHash,
      status: "pairing",
    })
    .select("id")
    .single();

  if (deviceError) {
    console.error("Failed to create device:", deviceError);
    return errorResponse("Failed to register device", 500);
  }

  // Generate a pairing code
  const { data: codeData, error: codeError } = await admin.rpc(
    "generate_pairing_code",
  );

  if (codeError || !codeData) {
    console.error("Failed to generate pairing code:", codeError);
    return errorResponse("Failed to generate pairing code", 500);
  }

  const pairingCode = codeData as string;
  const expiresAt = new Date(Date.now() + 10 * 60 * 1000).toISOString();

  // Store the pairing code
  const { error: pairingError } = await admin.from("pairing_codes").insert({
    code: pairingCode,
    device_id: device.id,
    expires_at: expiresAt,
  });

  if (pairingError) {
    console.error("Failed to store pairing code:", pairingError);
    return errorResponse("Failed to create pairing code", 500);
  }

  // Log the admin action
  await admin.from("admin_actions").insert({
    device_id: device.id,
    action_type: "device_registered",
    details: { machine_name: body.machine_name },
  });

  return jsonResponse({
    device_id: device.id,
    pairing_code: pairingCode,
    expires_at: expiresAt,
    api_key: apiKey,
  });
});

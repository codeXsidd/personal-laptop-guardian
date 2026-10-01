import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { getSupabaseAdmin, getUserIdFromAuth } from "../_shared/auth.ts";

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  if (req.method !== "POST") {
    return errorResponse("Method not allowed", 405);
  }

  const authHeader = req.headers.get("Authorization");
  const userId = await getUserIdFromAuth(authHeader);
  if (!userId) {
    return errorResponse("Authentication required", 401);
  }

  let body: { device_id: string };
  try {
    body = await req.json();
  } catch {
    return errorResponse("Invalid JSON body");
  }

  if (!body.device_id || typeof body.device_id !== "string") {
    return errorResponse("device_id is required");
  }

  const admin = getSupabaseAdmin();

  const { data: device, error: lookupError } = await admin
    .from("devices")
    .select("id, user_id, machine_name, device_name")
    .eq("id", body.device_id)
    .single();

  if (lookupError || !device) {
    return errorResponse("Device not found", 404);
  }

  if (device.user_id !== userId) {
    return errorResponse("You do not own this device", 403);
  }

  const { error: updateError } = await admin
    .from("devices")
    .update({
      user_id: null,
      device_name: null,
      status: "pairing",
    })
    .eq("id", body.device_id);

  if (updateError) {
    console.error("Failed to unpair device:", updateError);
    return errorResponse("Failed to unpair device", 500);
  }

  await admin.from("admin_actions").insert({
    user_id: userId,
    device_id: body.device_id,
    action_type: "device_unpaired",
    details: {
      previous_name: device.device_name,
      machine_name: device.machine_name,
    },
  });

  return jsonResponse({
    status: "unpaired",
    device_id: body.device_id,
    message: "Device has been unpaired. The agent will generate a new pairing code automatically.",
  });
});

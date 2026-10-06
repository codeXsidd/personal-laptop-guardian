import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { authenticateDevice, getSupabaseAdmin } from "../_shared/auth.ts";

const MAX_BATCH_SIZE = 100;

const VALID_SEVERITIES = new Set([
  "info",
  "low",
  "medium",
  "high",
  "critical",
]);

interface EventPayload {
  id: string;
  event_type: string;
  severity: string;
  timestamp: string;
  payload: Record<string, unknown>;
}

interface IngestRequest {
  events: EventPayload[];
}

function isValidUuid(s: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
    s,
  );
}

function isValidIsoTimestamp(s: string): boolean {
  const d = new Date(s);
  return !isNaN(d.getTime());
}

function validateEvent(evt: EventPayload, index: number): string | null {
  if (!evt.id || !isValidUuid(evt.id)) {
    return `events[${index}].id must be a valid UUID`;
  }
  if (!evt.event_type || typeof evt.event_type !== "string") {
    return `events[${index}].event_type is required`;
  }
  if (!VALID_SEVERITIES.has(evt.severity)) {
    return `events[${index}].severity must be one of: info, low, medium, high, critical`;
  }
  if (!evt.timestamp || !isValidIsoTimestamp(evt.timestamp)) {
    return `events[${index}].timestamp must be a valid ISO 8601 timestamp`;
  }
  return null;
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

  if (!device.user_id) {
    return errorResponse(
      "Device is not paired yet. Enter the pairing code in the mobile app first.",
      403,
    );
  }

  let body: IngestRequest;
  try {
    body = await req.json();
  } catch {
    return errorResponse("Invalid JSON body");
  }

  if (!Array.isArray(body.events)) {
    return errorResponse("events must be an array");
  }

  if (body.events.length === 0) {
    return jsonResponse({ inserted: 0, duplicates: 0, device_id: device.id });
  }

  if (body.events.length > MAX_BATCH_SIZE) {
    return errorResponse(
      `Batch size exceeds maximum of ${MAX_BATCH_SIZE}`,
      400,
    );
  }

  // Validate each event
  for (let i = 0; i < body.events.length; i++) {
    const validationError = validateEvent(body.events[i], i);
    if (validationError) {
      return errorResponse(validationError, 400);
    }
  }

  const admin = getSupabaseAdmin();

  // Prepare rows for insertion
  const rows = body.events.map((evt) => {
    const payload = evt.payload ?? {};
    if (evt.pc_timezone) payload.pc_timezone = evt.pc_timezone;
    if (evt.utc_offset) payload.utc_offset = evt.utc_offset;
    return {
      id: evt.id,
      device_id: device.id,
      event_type: evt.event_type,
      severity: evt.severity,
      timestamp: evt.timestamp,
      payload,
    };
  });

  // Upsert with ON CONFLICT DO NOTHING for deduplication
  const { data: inserted, error } = await admin
    .from("activity_events")
    .upsert(rows, { onConflict: "id", ignoreDuplicates: true })
    .select("id");

  if (error) {
    console.error("Failed to insert events:", error);
    return errorResponse("Failed to ingest events", 500);
  }

  const insertedCount = inserted?.length ?? 0;
  const duplicateCount = body.events.length - insertedCount;

  // Update device last_seen
  await admin
    .from("devices")
    .update({ last_seen_at: new Date().toISOString(), status: "online" })
    .eq("id", device.id);

  // Update device state based on event types — sort by timestamp so the
  // chronologically latest event wins when a batch contains multiple state changes.
  const sortedEvents = [...body.events].sort(
    (a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime(),
  );
  const stateUpdates: Record<string, unknown> = {};
  for (const evt of sortedEvents) {
    switch (evt.event_type) {
      case "system_startup":
      case "agent_started":
        stateUpdates.power_state = "on";
        stateUpdates.last_startup_at = evt.timestamp;
        break;
      case "system_shutdown":
        stateUpdates.power_state = "shutting_down";
        stateUpdates.last_shutdown_at = evt.timestamp;
        break;
      case "system_sleep":
        stateUpdates.power_state = "sleeping";
        stateUpdates.last_sleep_at = evt.timestamp;
        break;
      case "system_wake":
        stateUpdates.power_state = "on";
        stateUpdates.last_wake_at = evt.timestamp;
        break;
      case "session_login":
        stateUpdates.user_session_state = "logged_in";
        stateUpdates.last_login_at = evt.timestamp;
        break;
      case "session_unlock":
        stateUpdates.user_session_state = "logged_in";
        stateUpdates.last_unlock_at = evt.timestamp;
        break;
      case "session_logout":
        stateUpdates.user_session_state = "logged_out";
        stateUpdates.last_logout_at = evt.timestamp;
        break;
      case "session_lock":
        stateUpdates.user_session_state = "locked";
        stateUpdates.last_lock_at = evt.timestamp;
        break;
    }
  }
  if (Object.keys(stateUpdates).length > 0) {
    await admin.from("devices").update(stateUpdates).eq("id", device.id);
  }

  // Trigger notifications for newly inserted events (skip metrics)
  const SKIP_NOTIFY = new Set(["system_metrics"]);
  if (insertedCount > 0) {
    const insertedIds = new Set((inserted ?? []).map((r: { id: string }) => r.id));
    const notifyEvents = body.events.filter(
      (e) => insertedIds.has(e.id) && !SKIP_NOTIFY.has(e.event_type),
    );
    if (notifyEvents.length > 0) {
      const supabaseUrl = Deno.env.get("SUPABASE_URL")!;
      const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
      try {
        await fetch(`${supabaseUrl}/functions/v1/send-notification`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Authorization": `Bearer ${serviceKey}`,
          },
          body: JSON.stringify({
            device_id: device.id,
            events: notifyEvents,
          }),
        });
      } catch (e) {
        console.error("Failed to trigger notifications:", e);
      }
    }
  }

  return jsonResponse({
    inserted: insertedCount,
    duplicates: duplicateCount,
    device_id: device.id,
  });
});

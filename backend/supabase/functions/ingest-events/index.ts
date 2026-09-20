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
  const rows = body.events.map((evt) => ({
    id: evt.id,
    device_id: device.id,
    event_type: evt.event_type,
    severity: evt.severity,
    timestamp: evt.timestamp,
    payload: evt.payload ?? {},
  }));

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

  return jsonResponse({
    inserted: insertedCount,
    duplicates: duplicateCount,
    device_id: device.id,
  });
});

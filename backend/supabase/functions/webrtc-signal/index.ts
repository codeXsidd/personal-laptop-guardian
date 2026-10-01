import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import {
  authenticateDevice,
  getUserIdFromAuth,
  getSupabaseAdmin,
} from "../_shared/auth.ts";

async function resolveCallerIdentity(
  req: Request,
): Promise<{ kind: "device"; deviceId: string } | { kind: "user"; userId: string } | null> {
  const apiKey = req.headers.get("x-device-api-key");
  if (apiKey) {
    const device = await authenticateDevice(apiKey);
    if (device) return { kind: "device", deviceId: device.id };
  }
  const userId = await getUserIdFromAuth(req.headers.get("authorization"));
  if (userId) return { kind: "user", userId };
  return null;
}

async function verifySessionAccess(
  admin: ReturnType<typeof getSupabaseAdmin>,
  sessionId: string,
  caller: NonNullable<Awaited<ReturnType<typeof resolveCallerIdentity>>>,
): Promise<boolean> {
  const { data: session } = await admin
    .from("remote_sessions")
    .select("device_id, requested_by")
    .eq("id", sessionId)
    .single();

  if (!session) return false;

  if (caller.kind === "device") {
    return session.device_id === caller.deviceId;
  }
  return session.requested_by === caller.userId;
}

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  const admin = getSupabaseAdmin();

  const caller = await resolveCallerIdentity(req);
  if (!caller) return errorResponse("Authentication required", 401);

  // POST: send a signal (offer, answer, or ICE candidate)
  if (req.method === "POST") {
    let body: Record<string, unknown>;
    try {
      body = await req.json();
    } catch {
      return errorResponse("Invalid JSON body", 400);
    }

    const sessionId = body.session_id as string;
    const signalType = body.signal_type as string;
    const payload = body.payload;
    const sender = body.sender as string;

    if (!sessionId || !signalType || !payload || !sender) {
      return errorResponse(
        "session_id, signal_type, payload, and sender are required",
        400,
      );
    }

    if (!["offer", "answer", "ice_candidate"].includes(signalType)) {
      return errorResponse("signal_type must be offer, answer, or ice_candidate", 400);
    }
    if (!["mobile", "desktop"].includes(sender)) {
      return errorResponse("sender must be mobile or desktop", 400);
    }

    const hasAccess = await verifySessionAccess(admin, sessionId, caller);
    if (!hasAccess) return errorResponse("Not authorized for this session", 403);

    const { error } = await admin.from("webrtc_signals").insert({
      session_id: sessionId,
      sender,
      signal_type: signalType,
      payload,
    });

    if (error) {
      console.error("Failed to insert signal:", error);
      return errorResponse("Failed to store signal", 500);
    }

    return jsonResponse({ status: "ok" }, 201);
  }

  // GET: poll for signals from the other party
  if (req.method === "GET") {
    const url = new URL(req.url);
    const sessionId = url.searchParams.get("session_id");
    const after = url.searchParams.get("after");

    if (!sessionId) return errorResponse("session_id query param required", 400);

    const hasAccess = await verifySessionAccess(admin, sessionId, caller);
    if (!hasAccess) return errorResponse("Not authorized for this session", 403);

    // Return signals from the other party only
    const otherSender = caller.kind === "device" ? "mobile" : "desktop";

    let query = admin
      .from("webrtc_signals")
      .select("*")
      .eq("session_id", sessionId)
      .eq("sender", otherSender)
      .order("created_at", { ascending: true });

    if (after) {
      query = query.gt("created_at", after);
    }

    const { data, error } = await query;
    if (error) {
      console.error("Failed to fetch signals:", error);
      return errorResponse("Failed to fetch signals", 500);
    }

    return jsonResponse({ signals: data ?? [] });
  }

  return errorResponse("Method not allowed", 405);
});

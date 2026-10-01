import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import {
  authenticateDevice,
  getUserIdFromAuth,
  getSupabaseAdmin,
} from "../_shared/auth.ts";

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  const admin = getSupabaseAdmin();
  const now = new Date().toISOString();

  // GET: device polls for pending/active sessions
  if (req.method === "GET") {
    const apiKey = req.headers.get("x-device-api-key");
    if (!apiKey) return errorResponse("x-device-api-key required", 401);
    const device = await authenticateDevice(apiKey);
    if (!device) return errorResponse("Invalid device API key", 401);

    const { data, error } = await admin
      .from("remote_sessions")
      .select("*")
      .eq("device_id", device.id)
      .in("status", ["pending", "approved", "active"])
      .gt("expires_at", now)
      .order("created_at", { ascending: false });

    if (error) return errorResponse("Failed to fetch sessions", 500);

    // Expire stale sessions
    await admin
      .from("remote_sessions")
      .update({ status: "expired", ended_at: now, end_reason: "timeout" })
      .eq("device_id", device.id)
      .in("status", ["pending", "approved"])
      .lt("expires_at", now);

    return jsonResponse({ sessions: data ?? [] });
  }

  if (req.method !== "POST") return errorResponse("Method not allowed", 405);

  let body: Record<string, unknown>;
  try {
    body = await req.json();
  } catch {
    return errorResponse("Invalid JSON body", 400);
  }

  const action = body.action as string;

  // Mobile requests a remote session
  if (action === "request") {
    const userId = await getUserIdFromAuth(req.headers.get("authorization"));
    if (!userId) return errorResponse("Authentication required", 401);

    const deviceId = body.device_id as string;
    if (!deviceId) return errorResponse("device_id required", 400);

    // Verify device belongs to user
    const { data: device } = await admin
      .from("devices")
      .select("id, user_id, status")
      .eq("id", deviceId)
      .single();

    if (!device || device.user_id !== userId) {
      return errorResponse("Device not found or not owned", 403);
    }

    if (device.status !== "online") {
      return errorResponse("Device is not online", 400);
    }

    // Reject if an active session already exists
    const { data: existing } = await admin
      .from("remote_sessions")
      .select("id")
      .eq("device_id", deviceId)
      .in("status", ["pending", "approved", "active"])
      .gt("expires_at", now)
      .limit(1);

    if (existing && existing.length > 0) {
      return errorResponse("An active session already exists for this device", 409);
    }

    const expiresAt = new Date(Date.now() + 15 * 60 * 1000).toISOString();
    const { data: session, error } = await admin
      .from("remote_sessions")
      .insert({
        device_id: deviceId,
        requested_by: userId,
        status: "pending",
        expires_at: expiresAt,
      })
      .select()
      .single();

    if (error) {
      console.error("Failed to create remote session:", error);
      return errorResponse("Failed to create session", 500);
    }

    return jsonResponse({ session }, 201);
  }

  // Desktop approves or rejects
  if (action === "approve" || action === "reject") {
    const apiKey = req.headers.get("x-device-api-key");
    if (!apiKey) return errorResponse("x-device-api-key required", 401);
    const device = await authenticateDevice(apiKey);
    if (!device) return errorResponse("Invalid device API key", 401);

    const sessionId = body.session_id as string;
    if (!sessionId) return errorResponse("session_id required", 400);

    const updateData: Record<string, unknown> = {
      status: action === "approve" ? "approved" : "rejected",
    };
    if (action === "approve") {
      updateData.approved_at = now;
    } else {
      updateData.ended_at = now;
      updateData.end_reason = "rejected_by_user";
    }

    const { data: session, error } = await admin
      .from("remote_sessions")
      .update(updateData)
      .eq("id", sessionId)
      .eq("device_id", device.id)
      .eq("status", "pending")
      .select()
      .single();

    if (error || !session) {
      return errorResponse("Session not found or already processed", 404);
    }
    return jsonResponse({ session });
  }

  // Desktop marks session as active (WebRTC connected)
  if (action === "activate") {
    const apiKey = req.headers.get("x-device-api-key");
    if (!apiKey) return errorResponse("x-device-api-key required", 401);
    const device = await authenticateDevice(apiKey);
    if (!device) return errorResponse("Invalid device API key", 401);

    const sessionId = body.session_id as string;
    if (!sessionId) return errorResponse("session_id required", 400);

    const { data: session, error } = await admin
      .from("remote_sessions")
      .update({ status: "active", started_at: now })
      .eq("id", sessionId)
      .eq("device_id", device.id)
      .eq("status", "approved")
      .select()
      .single();

    if (error || !session) {
      return errorResponse("Session not found or not approved", 404);
    }
    return jsonResponse({ session });
  }

  // Either side ends or revokes
  if (action === "end" || action === "revoke") {
    const sessionId = body.session_id as string;
    if (!sessionId) return errorResponse("session_id required", 400);

    const endReason =
      action === "revoke" ? "revoked_by_user" : "ended_normally";
    const newStatus = action === "revoke" ? "revoked" : "ended";

    // Try device auth first, then user auth
    const apiKey = req.headers.get("x-device-api-key");
    const userId = await getUserIdFromAuth(req.headers.get("authorization"));

    if (apiKey) {
      const device = await authenticateDevice(apiKey);
      if (!device) return errorResponse("Invalid device API key", 401);

      const { data: session, error } = await admin
        .from("remote_sessions")
        .update({ status: newStatus, ended_at: now, end_reason: endReason })
        .eq("id", sessionId)
        .eq("device_id", device.id)
        .in("status", ["pending", "approved", "active"])
        .select()
        .single();

      if (error || !session) {
        return errorResponse("Session not found", 404);
      }
      return jsonResponse({ session });
    }

    if (userId) {
      const { data: session, error } = await admin
        .from("remote_sessions")
        .update({ status: newStatus, ended_at: now, end_reason: endReason })
        .eq("id", sessionId)
        .eq("requested_by", userId)
        .in("status", ["pending", "approved", "active"])
        .select()
        .single();

      if (error || !session) {
        return errorResponse("Session not found", 404);
      }
      return jsonResponse({ session });
    }

    return errorResponse("Authentication required", 401);
  }

  return errorResponse("Unknown action. Use: request, approve, reject, activate, end, revoke", 400);
});

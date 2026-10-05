import { authenticateDevice, getUserIdFromAuth, getSupabaseAdmin } from "../_shared/auth.ts";

interface SessionPeer {
  ws: WebSocket;
  role: "desktop" | "mobile";
  sessionId: string;
}

// In-memory session rooms. Each session has at most one desktop and one mobile peer.
const rooms = new Map<string, { desktop?: SessionPeer; mobile?: SessionPeer }>();

function cleanupRoom(sessionId: string, role: "desktop" | "mobile") {
  const room = rooms.get(sessionId);
  if (!room) return;
  if (role === "desktop") room.desktop = undefined;
  else room.mobile = undefined;
  if (!room.desktop && !room.mobile) rooms.delete(sessionId);
}

Deno.serve(async (req) => {
  // Only accept WebSocket upgrade requests
  const upgrade = req.headers.get("upgrade") || "";
  if (upgrade.toLowerCase() !== "websocket") {
    return new Response("WebSocket upgrade required", { status: 426 });
  }

  const url = new URL(req.url);
  const sessionId = url.searchParams.get("session_id");
  const role = url.searchParams.get("role") as "desktop" | "mobile" | null;

  if (!sessionId || !role || !["desktop", "mobile"].includes(role)) {
    return new Response("session_id and role (desktop|mobile) required", { status: 400 });
  }

  // Authenticate
  const admin = getSupabaseAdmin();
  let authorized = false;

  if (role === "desktop") {
    const apiKey = url.searchParams.get("api_key");
    if (apiKey) {
      const device = await authenticateDevice(apiKey);
      if (device) {
        const { data: session } = await admin
          .from("remote_sessions")
          .select("device_id, status")
          .eq("id", sessionId)
          .in("status", ["approved", "active"])
          .single();
        authorized = !!session && session.device_id === device.id;
      }
    }
  } else {
    const authHeader = url.searchParams.get("token");
    if (authHeader) {
      const userId = await getUserIdFromAuth(`Bearer ${authHeader}`);
      if (userId) {
        const { data: session } = await admin
          .from("remote_sessions")
          .select("requested_by, status")
          .eq("id", sessionId)
          .in("status", ["approved", "active"])
          .single();
        authorized = !!session && session.requested_by === userId;
      }
    }
  }

  if (!authorized) {
    return new Response("Unauthorized", { status: 401 });
  }

  const { socket, response } = Deno.upgradeWebSocket(req, {
    idleTimeout: 60,
  });

  const peer: SessionPeer = { ws: socket, role, sessionId };

  socket.onopen = () => {
    console.log(`[relay] ${role} connected to session ${sessionId}, rooms=${rooms.size}`);
    let room = rooms.get(sessionId);
    if (!room) {
      room = {};
      rooms.set(sessionId, room);
    }

    // Close existing peer in same role
    if (role === "desktop" && room.desktop) {
      console.log(`[relay] Replacing existing desktop in session ${sessionId}`);
      try { room.desktop.ws.close(1000, "replaced"); } catch { /* */ }
    }
    if (role === "mobile" && room.mobile) {
      console.log(`[relay] Replacing existing mobile in session ${sessionId}`);
      try { room.mobile.ws.close(1000, "replaced"); } catch { /* */ }
    }

    room[role] = peer;
    console.log(`[relay] Room ${sessionId}: desktop=${!!room.desktop} mobile=${!!room.mobile}`);

    // Send a test message to confirm the WebSocket is working
    try { socket.send(JSON.stringify({ type: "relay_connected", role, session_id: sessionId })); } catch { /* */ }

    // Notify the other peer
    const other = role === "desktop" ? room.mobile : room.desktop;
    if (other?.ws.readyState === WebSocket.OPEN) {
      console.log(`[relay] Notifying ${role === "desktop" ? "mobile" : "desktop"} of peer_joined`);
      other.ws.send(JSON.stringify({ type: "peer_joined", role }));
    }
  };

  let msgCount = 0;
  socket.onmessage = (event) => {
    const room = rooms.get(sessionId);
    if (!room) { console.log(`[relay] No room for ${sessionId}`); return; }

    const target = role === "desktop" ? room.mobile : room.desktop;
    if (!target || target.ws.readyState !== WebSocket.OPEN) {
      if (msgCount < 3) console.log(`[relay] No target for ${role} in ${sessionId}, target=${!!target}, state=${target?.ws.readyState}`);
      msgCount++;
      return;
    }

    // Relay the message directly (binary or text)
    if (event.data instanceof ArrayBuffer) {
      target.ws.send(event.data);
    } else if (event.data instanceof Blob) {
      event.data.arrayBuffer().then((buf) => {
        if (target.ws.readyState === WebSocket.OPEN) {
          target.ws.send(buf);
        }
      });
    } else {
      target.ws.send(event.data);
    }
  };

  socket.onclose = (event) => {
    console.log(`[relay] ${role} disconnected from ${sessionId}, code=${event.code} reason=${event.reason}`);
    cleanupRoom(sessionId, role);
    const room = rooms.get(sessionId);
    if (room) {
      const other = role === "desktop" ? room.mobile : room.desktop;
      if (other?.ws.readyState === WebSocket.OPEN) {
        other.ws.send(JSON.stringify({ type: "peer_left", role }));
      }
    }
  };

  socket.onerror = (event) => {
    console.log(`[relay] ${role} error in ${sessionId}: ${event}`);
    cleanupRoom(sessionId, role);
  };

  return response;
});

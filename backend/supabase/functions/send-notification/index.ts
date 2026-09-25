import { handleCors } from "../_shared/cors.ts";
import { jsonResponse, errorResponse } from "../_shared/response.ts";
import { getSupabaseAdmin } from "../_shared/auth.ts";

const SEVERITY_RANK: Record<string, number> = {
  info: 0,
  low: 1,
  medium: 2,
  high: 3,
  critical: 4,
};

interface NotificationRequest {
  device_id: string;
  events: Array<{
    id: string;
    event_type: string;
    severity: string;
    timestamp: string;
    payload: Record<string, unknown>;
  }>;
}

interface FCMResponse {
  name?: string;
  error?: { code: number; message: string; status: string };
}

async function getAccessToken(): Promise<string> {
  const serviceAccountJson = Deno.env.get("FIREBASE_SERVICE_ACCOUNT");
  if (!serviceAccountJson) {
    throw new Error("FIREBASE_SERVICE_ACCOUNT secret is not set");
  }

  const sa = JSON.parse(serviceAccountJson);
  const now = Math.floor(Date.now() / 1000);
  const header = btoa(JSON.stringify({ alg: "RS256", typ: "JWT" }));
  const claim = btoa(
    JSON.stringify({
      iss: sa.client_email,
      scope: "https://www.googleapis.com/auth/firebase.messaging",
      aud: "https://oauth2.googleapis.com/token",
      iat: now,
      exp: now + 3600,
    }),
  );

  const signingInput = `${header}.${claim}`;
  const key = await crypto.subtle.importKey(
    "pkcs8",
    pemToArrayBuffer(sa.private_key),
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5",
    key,
    new TextEncoder().encode(signingInput),
  );
  const jwt = `${signingInput}.${arrayBufferToBase64Url(signature)}`;

  const tokenResp = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer&assertion=${jwt}`,
  });

  const tokenData = await tokenResp.json();
  if (!tokenData.access_token) {
    throw new Error(`Failed to get access token: ${JSON.stringify(tokenData)}`);
  }
  return tokenData.access_token;
}

function pemToArrayBuffer(pem: string): ArrayBuffer {
  const b64 = pem
    .replace(/-----BEGIN PRIVATE KEY-----/, "")
    .replace(/-----END PRIVATE KEY-----/, "")
    .replace(/\n/g, "");
  const binary = atob(b64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

function arrayBufferToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function eventDisplayName(eventType: string): string {
  const names: Record<string, string> = {
    session_login: "Login",
    session_logout: "Logout",
    session_lock: "Screen Lock",
    session_unlock: "Screen Unlock",
    login_failed: "Login Failed",
    process_start: "App Started",
    process_stop: "App Stopped",
    usb_connected: "USB Connected",
    usb_disconnected: "USB Disconnected",
    network_connected: "Network Connected",
    network_disconnected: "Network Disconnected",
    network_changed: "Network Changed",
    eventlog_entry: "Event Log",
    file_access: "File Access",
    system_metrics: "System Metrics",
    agent_started: "Agent Started",
    system_startup: "System Startup",
    system_shutdown: "System Shutdown",
  };
  return names[eventType] ?? eventType;
}

Deno.serve(async (req) => {
  const corsResp = handleCors(req);
  if (corsResp) return corsResp;

  if (req.method !== "POST") {
    return errorResponse("Method not allowed", 405);
  }

  const authHeader = req.headers.get("authorization");
  if (!authHeader) {
    return errorResponse("Unauthorized", 401);
  }
  const token = authHeader.replace(/^Bearer\s+/i, "");
  try {
    const payload = JSON.parse(atob(token.split(".")[1]));
    if (payload.role !== "service_role") {
      return errorResponse("Unauthorized", 401);
    }
  } catch {
    return errorResponse("Unauthorized", 401);
  }

  let body: NotificationRequest;
  try {
    body = await req.json();
  } catch {
    return errorResponse("Invalid JSON body");
  }

  if (!body.device_id || !Array.isArray(body.events) || body.events.length === 0) {
    return errorResponse("device_id and non-empty events array required");
  }

  const admin = getSupabaseAdmin();

  const { data: device } = await admin
    .from("devices")
    .select("id, user_id, device_name, machine_name")
    .eq("id", body.device_id)
    .single();

  if (!device?.user_id) {
    return jsonResponse({ sent: 0, reason: "device not paired" });
  }

  const { data: settings } = await admin
    .from("notification_settings")
    .select("event_type, min_severity, enabled")
    .eq("user_id", device.user_id)
    .eq("device_id", device.id);

  const settingsMap = new Map<string, { min_severity: string; enabled: boolean }>();
  if (settings) {
    for (const s of settings) {
      settingsMap.set(s.event_type, {
        min_severity: s.min_severity,
        enabled: s.enabled,
      });
    }
  }

  const eventsToNotify = body.events.filter((evt) => {
    const setting = settingsMap.get(evt.event_type);
    if (!setting) {
      return (SEVERITY_RANK[evt.severity] ?? 0) >= SEVERITY_RANK["high"];
    }
    if (!setting.enabled) return false;
    return (SEVERITY_RANK[evt.severity] ?? 0) >= (SEVERITY_RANK[setting.min_severity] ?? 0);
  });

  if (eventsToNotify.length === 0) {
    return jsonResponse({ sent: 0, reason: "no events match notification settings" });
  }

  const { data: tokens } = await admin
    .from("notification_tokens")
    .select("id, fcm_token")
    .eq("user_id", device.user_id);

  if (!tokens || tokens.length === 0) {
    return jsonResponse({ sent: 0, reason: "no FCM tokens registered" });
  }

  let accessToken: string;
  try {
    accessToken = await getAccessToken();
  } catch (e) {
    console.error("Failed to get FCM access token:", e);
    return errorResponse("Failed to authenticate with FCM", 500);
  }

  const projectId = Deno.env.get("FIREBASE_PROJECT_ID") ?? "personal-laptop-guardian";
  const fcmUrl = `https://fcm.googleapis.com/v1/projects/${projectId}/messages:send`;

  let sentCount = 0;
  const invalidTokenIds: string[] = [];
  const sentEventTokenPairs = new Set<string>();

  for (const evt of eventsToNotify) {
    const isHighSeverity = evt.severity === "high" || evt.severity === "critical";
    const deviceName = device.device_name ?? device.machine_name ?? "Unknown";
    const title = isHighSeverity
      ? `[${evt.severity.toUpperCase()}] ${eventDisplayName(evt.event_type)}`
      : eventDisplayName(evt.event_type);
    const bodyText = `${deviceName}: ${eventDisplayName(evt.event_type)} at ${new Date(evt.timestamp).toLocaleTimeString()}`;

    for (const token of tokens) {
      const dedupeKey = `${evt.id}:${token.fcm_token}`;
      if (sentEventTokenPairs.has(dedupeKey)) continue;
      sentEventTokenPairs.add(dedupeKey);

      try {
        const resp = await fetch(fcmUrl, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${accessToken}`,
          },
          body: JSON.stringify({
            message: {
              token: token.fcm_token,
              notification: { title, body: bodyText },
              data: {
                event_id: evt.id,
                device_id: body.device_id,
                event_type: evt.event_type,
                severity: evt.severity,
                title,
                body: bodyText,
              },
              android: {
                priority: isHighSeverity ? "high" : "normal",
                notification: {
                  channel_id: isHighSeverity ? "security_alerts" : "general_activity",
                },
              },
            },
          }),
        });

        const result: FCMResponse = await resp.json();
        if (result.name) {
          sentCount++;
        } else if (
          result.error?.code === 404 ||
          result.error?.message?.includes("not a valid FCM registration token") ||
          result.error?.message?.includes("Requested entity was not found")
        ) {
          invalidTokenIds.push(token.id);
        } else {
          console.error(`FCM send failed for token ${token.id}:`, result.error);
        }
      } catch (e) {
        console.error(`FCM request failed for token ${token.id}:`, e);
      }
    }
  }

  if (invalidTokenIds.length > 0) {
    await admin
      .from("notification_tokens")
      .delete()
      .in_("id", invalidTokenIds);
    console.log(`Cleaned up ${invalidTokenIds.length} invalid FCM token(s)`);
  }

  return jsonResponse({
    sent: sentCount,
    events_checked: eventsToNotify.length,
    tokens_checked: tokens.length,
    invalid_tokens_removed: invalidTokenIds.length,
  });
});

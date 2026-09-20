import { corsHeaders } from "./cors.ts";

export function jsonResponse(
  body: Record<string, unknown>,
  status = 200,
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "Content-Type": "application/json" },
  });
}

export function errorResponse(
  error: string,
  status = 400,
  extra?: Record<string, unknown>,
): Response {
  return jsonResponse({ error, ...extra }, status);
}

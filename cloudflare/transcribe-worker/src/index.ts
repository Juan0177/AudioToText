/**
 * Optional edge STT endpoint using Workers AI binding.
 * Deploy when ASP.NET should not hold a Cloudflare API token.
 *
 * POST /transcribe  (multipart file field "file", or raw audio body)
 * Header: Authorization: Bearer <TRANSCRIBE_API_KEY> (if secret set)
 */

export interface Env {
  AI: Ai;
  TRANSCRIBE_API_KEY?: string;
  ALLOWED_ORIGIN?: string;
}

const MAX_BYTES = 25 * 1024 * 1024;
const MODEL = "@cf/openai/whisper";

function corsHeaders(env: Env): HeadersInit {
  return {
    "Access-Control-Allow-Origin": env.ALLOWED_ORIGIN || "*",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
    "Access-Control-Allow-Headers": "Authorization, Content-Type",
  };
}

function json(data: unknown, status: number, env: Env): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      ...corsHeaders(env),
    },
  });
}

function unauthorized(env: Env): Response {
  return json({ error: "Unauthorized" }, 401, env);
}

function authorize(request: Request, env: Env): boolean {
  if (!env.TRANSCRIBE_API_KEY) {
    return true;
  }
  const header = request.headers.get("Authorization") || "";
  const expected = `Bearer ${env.TRANSCRIBE_API_KEY}`;
  return header.length === expected.length && timingSafeEqual(header, expected);
}

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let out = 0;
  for (let i = 0; i < a.length; i++) {
    out |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }
  return out === 0;
}

async function readAudio(request: Request): Promise<Uint8Array | null> {
  const contentType = request.headers.get("Content-Type") || "";
  if (contentType.includes("multipart/form-data")) {
    const form = await request.formData();
    const file = form.get("file");
    if (!(file instanceof File)) {
      return null;
    }
    return new Uint8Array(await file.arrayBuffer());
  }

  const buf = await request.arrayBuffer();
  if (buf.byteLength === 0) {
    return null;
  }
  return new Uint8Array(buf);
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: corsHeaders(env) });
    }

    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/health") {
      return json({ ok: true }, 200, env);
    }

    if (request.method !== "POST" || url.pathname !== "/transcribe") {
      return json({ error: "Not found" }, 404, env);
    }

    if (!authorize(request, env)) {
      return unauthorized(env);
    }

    try {
      const audio = await readAudio(request);
      if (!audio || audio.byteLength === 0) {
        return json({ error: "Missing audio body" }, 400, env);
      }
      if (audio.byteLength > MAX_BYTES) {
        return json({ error: "File too large (max 25 MB)" }, 400, env);
      }

      const result = await env.AI.run(MODEL, {
        audio: [...audio],
      });

      const text =
        typeof result === "object" && result && "text" in result
          ? String((result as { text?: string }).text || "").trim()
          : "";

      return json(
        {
          text: text || "(Nessun parlato rilevato)",
          wordCount:
            typeof result === "object" && result && "word_count" in result
              ? (result as { word_count?: number }).word_count
              : undefined,
        },
        200,
        env,
      );
    } catch (err) {
      console.error("transcribe failed", err);
      return json({ error: "Transcription failed" }, 502, env);
    }
  },
} satisfies ExportedHandler<Env>;

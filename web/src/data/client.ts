import { ApiError, parseProblem } from "@/shared/lib/api-error";
import { API_KEY_HEADER, apiKeyStore } from "@/data/api-key-store";

const DEFAULT_API_BASE_URL = "http://localhost:5080";

export function apiBaseUrl(): string {
  return (process.env.NEXT_PUBLIC_API_BASE_URL || DEFAULT_API_BASE_URL).replace(/\/+$/, "");
}

// Every caller below wants the same request: the local API key attached, a network failure and a 401 turned into the
// shared ApiError shapes, and only an ok response handed back for the caller to read its own way.
async function request(path: `/${string}`, accept: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("Accept", accept);
  const key = apiKeyStore.current();
  if (key !== null && !headers.has(API_KEY_HEADER)) headers.set(API_KEY_HEADER, key);

  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl()}/api/v1${path}`, { ...init, headers });
  } catch {
    throw new ApiError({ kind: "network" });
  }

  if (response.status === 401) apiKeyStore.refused(headers.get(API_KEY_HEADER));
  if (response.ok) return response;

  const isProblem = response.headers.get("Content-Type")?.includes("application/problem+json") ?? false;
  const problem = isProblem ? parseProblem(await response.json().catch(() => null), response.status) : null;
  throw new ApiError(problem ? { kind: "problem", problem } : { kind: "http", status: response.status });
}

// The body is trusted to match the OpenAPI contract: the API is ours and the
// web types are generated from its document (`pnpm api:types`).
export async function apiFetch<T>(path: `/${string}`, init: RequestInit = {}): Promise<T> {
  const response = await request(path, "application/json, application/problem+json", init);
  return (response.status === 204 ? undefined : await response.json()) as T;
}

// For an answer that is not JSON, such as the payments calendar's RFC 5545 export (#70): the caller downloads or displays
// the blob itself, with the Content-Type the API sent it as.
export async function apiFetchBlob(path: `/${string}`, init: RequestInit = {}): Promise<Blob> {
  const response = await request(path, "*/*, application/problem+json", init);
  return response.blob();
}

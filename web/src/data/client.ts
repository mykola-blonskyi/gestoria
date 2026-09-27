import { ApiError, parseProblem } from "@/shared/lib/api-error";
import { API_KEY_HEADER, apiKeyStore } from "@/data/api-key-store";

const DEFAULT_API_BASE_URL = "http://localhost:5080";

export function apiBaseUrl(): string {
  return (process.env.NEXT_PUBLIC_API_BASE_URL || DEFAULT_API_BASE_URL).replace(/\/+$/, "");
}

// The body is trusted to match the OpenAPI contract: the API is ours and the
// web types are generated from its document (`pnpm api:types`).
export async function apiFetch<T>(path: `/${string}`, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json, application/problem+json");
  const key = apiKeyStore.current();
  if (key !== null && !headers.has(API_KEY_HEADER)) headers.set(API_KEY_HEADER, key);

  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl()}/api/v1${path}`, { ...init, headers });
  } catch {
    throw new ApiError({ kind: "network" });
  }

  if (response.status === 401) apiKeyStore.refused(headers.get(API_KEY_HEADER));

  if (response.ok) {
    return (response.status === 204 ? undefined : await response.json()) as T;
  }

  const isProblem = response.headers.get("Content-Type")?.includes("application/problem+json") ?? false;
  const problem = isProblem ? parseProblem(await response.json().catch(() => null), response.status) : null;
  throw new ApiError(problem ? { kind: "problem", problem } : { kind: "http", status: response.status });
}

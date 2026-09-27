import { afterEach, describe, expect, it, vi } from "vitest";

import { ApiError } from "@/data/api-error";
import { apiKeyStore } from "@/data/api-key-store";
import { apiFetch } from "@/data/client";
import { shouldRetry } from "@/data/query-provider";

function stubFetch(response: Response | Error) {
  const fetchStub = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

async function failureOf(promise: Promise<unknown>): Promise<ApiError> {
  const error = await promise.then(
    () => null,
    (reason: unknown) => reason,
  );
  if (!(error instanceof ApiError)) throw new Error("expected an ApiError");
  return error;
}

afterEach(() => {
  apiKeyStore.forget();
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe("apiFetch", () => {
  it("calls /api/v1 on the configured base URL and returns the JSON body", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "http://127.0.0.1:7000/");
    const fetchStub = stubFetch(Response.json({ year: 2025, amount: "1234.56" }));

    const body = await apiFetch<{ year: number; amount: string }>("/config/tax-years/2025");

    expect(body).toEqual({ year: 2025, amount: "1234.56" });
    expect(fetchStub).toHaveBeenCalledWith(
      "http://127.0.0.1:7000/api/v1/config/tax-years/2025",
      expect.objectContaining({ headers: expect.any(Headers) }),
    );
  });

  it("defaults to the local API when no base URL is configured", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "");
    const fetchStub = stubFetch(new Response(null, { status: 204 }));

    await expect(apiFetch("/health/live")).resolves.toBeUndefined();
    expect(fetchStub).toHaveBeenCalledWith("http://localhost:5080/api/v1/health/live", expect.anything());
  });

  it("parses a problem+json answer into a typed failure", async () => {
    stubFetch(
      new Response(
        JSON.stringify({
          type: "https://gestoria.local/problems/config-gap",
          title: "Configuration value not published",
          status: 422,
          detail: "tarifaPlana.2026 is declared incomplete in 2026.json",
          blockers: ["seguridadSocial.tarifaPlana"],
        }),
        { status: 422, headers: { "Content-Type": "application/problem+json" } },
      ),
    );

    const error = await failureOf(apiFetch("/set-aside/estimate", { method: "POST" }));

    expect(error.failure).toEqual({
      kind: "problem",
      problem: {
        type: "https://gestoria.local/problems/config-gap",
        title: "Configuration value not published",
        status: 422,
        detail: "tarifaPlana.2026 is declared incomplete in 2026.json",
        instance: undefined,
        extensions: { blockers: ["seguridadSocial.tarifaPlana"] },
      },
    });
  });

  it("keeps detail and extensions out of the error message, which can reach the console", async () => {
    stubFetch(
      new Response(JSON.stringify({ title: "Invalid input", status: 400, detail: "ingresos is 1234.56", amount: "99.99" }), {
        status: 400,
        headers: { "Content-Type": "application/problem+json; charset=utf-8" },
      }),
    );

    const error = await failureOf(apiFetch("/set-aside/estimate"));

    expect(error.message).toBe("API 400: Invalid input");
  });

  it("fills RFC 9457 defaults when the problem omits members", async () => {
    stubFetch(new Response("{}", { status: 404, headers: { "Content-Type": "application/problem+json" } }));

    const error = await failureOf(apiFetch("/profiles/1"));

    expect(error.failure).toEqual({
      kind: "problem",
      problem: { type: "about:blank", title: "", status: 404, detail: undefined, instance: undefined, extensions: {} },
    });
  });

  it("reports a non-problem error answer by its status", async () => {
    stubFetch(new Response("<html>Bad gateway</html>", { status: 502, headers: { "Content-Type": "text/html" } }));

    const error = await failureOf(apiFetch("/health/live"));

    expect(error.failure).toEqual({ kind: "http", status: 502 });
  });

  it("reports an unreachable API as a network failure", async () => {
    stubFetch(new TypeError("fetch failed"));

    const error = await failureOf(apiFetch("/health/live"));

    expect(error.failure).toEqual({ kind: "network" });
    expect(error.message).toBe("API unreachable");
  });
});

describe("shouldRetry", () => {
  const problem = (status: number) =>
    new ApiError({ kind: "problem", problem: { type: "about:blank", title: "", status, extensions: {} } });

  it.each([
    ["an unreachable API", new ApiError({ kind: "network" })],
    ["a 503 answer", new ApiError({ kind: "http", status: 503 })],
    ["a 500 problem", problem(500)],
  ])("retries %s", (_, error) => {
    expect(shouldRetry(0, error)).toBe(true);
    expect(shouldRetry(2, error)).toBe(false);
  });

  it.each([
    ["a 422 declared gap", problem(422)],
    ["a 400 validation problem", problem(400)],
    ["a plain 404", new ApiError({ kind: "http", status: 404 })],
    ["an error that did not come from the API", new Error("bug")],
  ])("does not retry %s", (_, error) => {
    expect(shouldRetry(0, error)).toBe(false);
  });
});

describe("apiFetch and the API key", () => {
  const unauthorized = () =>
    new Response(JSON.stringify({ type: "https://gestoria.local/problems/api-key-required", title: "Key", status: 401 }), {
      status: 401,
      headers: { "Content-Type": "application/problem+json" },
    });

  it("locks the app when the API refuses the key in use", async () => {
    apiKeyStore.remember("the-key");
    stubFetch(unauthorized());

    await failureOf(apiFetch("/config/tax-years"));

    expect(apiKeyStore.state()).toBe("refused");
    expect(apiKeyStore.current()).toBeNull();
  });

  // A request sent with an earlier key can answer after the app was unlocked with the new one.
  it("leaves the app unlocked when the refused key is not the one in use", async () => {
    apiKeyStore.remember("the-new-key");
    stubFetch(unauthorized());

    await failureOf(apiFetch("/config/tax-years", { headers: { "X-Api-Key": "an-earlier-key" } }));

    expect(apiKeyStore.state()).toBe("unlocked");
    expect(apiKeyStore.current()).toBe("the-new-key");
  });
});

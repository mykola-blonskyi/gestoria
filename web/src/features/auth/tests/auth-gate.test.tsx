import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useQuery } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { MESSAGES, renderInApp } from "@tests/render";

import { apiKeyStore } from "@/data/api-key-store";
import { taxYearsQuery, type TaxYear } from "@/data/tax-years";
import { AuthGate, AuthPage } from "@/features/auth";
import { LOCALES, type Locale } from "@/shared/constants/locales";

const KEY = "a-made-up-key-for-these-tests";
const taxYears = [
  { taxYear: 2025, configHash: "0".repeat(64), regions: [{ code: "VC", name: "Comunitat Valenciana" }], gaps: [] },
] satisfies TaxYear[];
const HEADER = "X-Api-Key";

type Answer = "ok" | "unauthorized" | "unreachable";

// The API: the list of tax years for the right key, the api-key-required problem otherwise, or no API at all.
function stubApi(answer: (key: string | null) => Answer = (key) => (key === KEY ? "ok" : "unauthorized")) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (_url, init) => {
    const outcome = answer(new Headers(init?.headers).get(HEADER));
    if (outcome === "unreachable") throw new TypeError("fetch failed");
    if (outcome === "ok") return Response.json(taxYears);
    const problem = { type: "https://gestoria.local/problems/api-key-required", title: "The request needs the local API key", status: 401 };
    return new Response(JSON.stringify(problem), { status: 401, headers: { "Content-Type": "application/problem+json" } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

const sentKeys = (fetchStub: ReturnType<typeof stubApi>) => fetchStub.mock.calls.map(([, init]) => new Headers(init?.headers).get(HEADER));

// A page behind the gate that calls the API through the data layer, as every feature does.
function TaxYearCount() {
  const years = useQuery(taxYearsQuery());
  return (
    <>
      <p>{years.isSuccess ? `${years.data.length} tax years` : "loading"}</p>
      <button onClick={() => void years.refetch()}>refresh</button>
    </>
  );
}

const gated = (page = <TaxYearCount />) => <AuthGate>{page}</AuthGate>;

async function unlockWith(key: string, locale: Locale = "en") {
  const user = userEvent.setup();
  const messages = MESSAGES[locale].Auth.unlock;
  await user.clear(screen.getByLabelText(messages.key));
  await user.type(screen.getByLabelText(messages.key), key);
  await user.click(screen.getByRole("button", { name: messages.submit }));
  return user;
}

beforeEach(() => {
  apiKeyStore.forget();
});

afterEach(() => {
  apiKeyStore.forget();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("AuthGate", () => {
  it.each(LOCALES)("in %s shows the unlock screen and nothing behind it until a key is entered", (locale) => {
    const fetchStub = stubApi();
    renderInApp(gated(), { locale });

    const messages = MESSAGES[locale].Auth.unlock;
    expect(screen.getByRole("heading", { level: 1, name: messages.title })).toBeInTheDocument();
    expect(screen.getByText(messages.lead)).toBeInTheDocument();
    expect(screen.getByLabelText(messages.key)).toHaveAttribute("type", "password");
    expect(screen.getByRole("button", { name: messages.submit })).toBeDisabled();
    expect(screen.queryByText(/tax years|loading/)).not.toBeInTheDocument();
    expect(fetchStub).not.toHaveBeenCalled();
  });

  it("unlocks with the right key and sends it on every request through the data layer", async () => {
    const fetchStub = stubApi();
    renderInApp(gated(), { locale: "en" });

    await unlockWith(KEY);

    expect(await screen.findByText(`${taxYears.length} tax years`)).toBeInTheDocument();
    expect(screen.queryByLabelText(MESSAGES.en.Auth.unlock.key)).not.toBeInTheDocument();
    expect(fetchStub.mock.calls.length).toBeGreaterThanOrEqual(2);
    expect(sentKeys(fetchStub)).toEqual(fetchStub.mock.calls.map(() => KEY));
  });

  it.each(LOCALES)("in %s says a wrong key was refused and stays locked", async (locale) => {
    stubApi();
    renderInApp(gated(), { locale });

    await unlockWith("not-the-key", locale);

    expect(await screen.findByRole("alert")).toHaveTextContent(MESSAGES[locale].Auth.unlock.wrongKey);
    expect(screen.getByLabelText(MESSAGES[locale].Auth.unlock.key)).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByText(/tax years/)).not.toBeInTheDocument();
  });

  it.each(LOCALES)("in %s says the API is not running, and how to start it, when it cannot be reached", async (locale) => {
    stubApi(() => "unreachable");
    renderInApp(gated(), { locale });

    await unlockWith(KEY, locale);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(MESSAGES[locale].Auth.unlock.unreachable);
    expect(alert).toHaveTextContent(MESSAGES[locale].Auth.unlock.unreachableNext);
    expect(alert).toHaveTextContent("dotnet run --project src/GestorIA.Api");
    expect(alert).not.toHaveTextContent(MESSAGES[locale].Auth.unlock.wrongKey);
    expect(screen.queryByText(/tax years/)).not.toBeInTheDocument();
  });

  it("unlocks once the API is started and the key is sent again", async () => {
    let running = false;
    stubApi((key) => (!running ? "unreachable" : key === KEY ? "ok" : "unauthorized"));
    renderInApp(gated(), { locale: "en" });

    const user = await unlockWith(KEY);
    await screen.findByRole("alert");
    running = true;
    await user.click(screen.getByRole("button", { name: MESSAGES.en.Auth.unlock.submit }));

    expect(await screen.findByText(`${taxYears.length} tax years`)).toBeInTheDocument();
  });

  it("returns to the unlock screen, saying why, when the API refuses the key later", async () => {
    let accepted = KEY;
    stubApi((key) => (key === accepted ? "ok" : "unauthorized"));
    renderInApp(gated(), { locale: "en" });
    const user = await unlockWith(KEY);
    await screen.findByText(`${taxYears.length} tax years`);

    accepted = "the-key-after-a-restart";
    await user.click(screen.getByRole("button", { name: "refresh" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(MESSAGES.en.Auth.unlock.refused);
    expect(screen.getByLabelText(MESSAGES.en.Auth.unlock.key)).toBeInTheDocument();
    expect(screen.queryByText(/tax years/)).not.toBeInTheDocument();
  });

  it("keeps the key out of storage, cookies, the address, the console and the form's data", async () => {
    const consoleSpies = (["log", "info", "warn", "error", "debug"] as const).map((method) => vi.spyOn(console, method));
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    stubApi();
    renderInApp(gated(), { locale: "en" });

    await unlockWith("not-the-key");
    await screen.findByRole("alert");
    const form = screen.getByLabelText(MESSAGES.en.Auth.unlock.key).closest("form");
    expect([...new FormData(form!).values()]).toEqual([]);
    await unlockWith(KEY);
    await screen.findByText(`${taxYears.length} tax years`);

    const everywhere = [
      JSON.stringify({ ...localStorage }),
      JSON.stringify({ ...sessionStorage }),
      document.cookie,
      window.location.href,
      JSON.stringify(consoleSpies.map((spy) => spy.mock.calls)),
    ].join("\n");
    expect(everywhere).not.toContain(KEY);
    expect(everywhere).not.toContain("not-the-key");
    expect(setItem).not.toHaveBeenCalled();
  });
});

describe("AuthPage", () => {
  it.each(LOCALES)("in %s says the app is unlocked for this tab only, and locks it on request", async (locale) => {
    stubApi();
    renderInApp(gated(<AuthPage />), { locale });
    const user = await unlockWith(KEY, locale);
    const messages = MESSAGES[locale].Auth;

    expect(await screen.findByRole("heading", { level: 1, name: messages.title })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(messages.unlocked);
    await user.click(screen.getByRole("button", { name: messages.lock }));

    await waitFor(() => expect(screen.getByLabelText(messages.unlock.key)).toHaveValue(""));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

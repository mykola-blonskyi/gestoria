import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import g12Estimate from "@tests/fixtures/g12-estimate.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import { MESSAGES, renderInApp } from "@tests/render";

import { DashboardPage } from "@/features/dashboard";
import { LOCALES, type Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney, formatMonth, formatShare } from "@/shared/lib/format";

// The API's own answers (tests/GestorIA.Api.Tests/WebFixtures.cs keeps them so): G12's taxpayer stored as the profile, and
// its estimate for Q1.
const ESTIMATE_URL = `http://localhost:5080/api/v1/profiles/${g12Profile.id}/set-aside/estimate`;

type Answer = { status: number; body: unknown } | "unreachable";

const problem = (status: number, body: Record<string, unknown>): Answer => ({ status, body: { status, ...body } });

function stubApi({
  profiles = { status: 200, body: [g12Profile] },
  estimate = { status: 200, body: g12Estimate },
}: { profiles?: Answer; estimate?: Answer } = {}) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    const answer = url.includes("/set-aside/estimate") ? estimate : profiles;
    if (answer === "unreachable") throw new TypeError("fetch failed");
    const contentType = answer.status >= 400 ? "application/problem+json" : "application/json";
    return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { "Content-Type": contentType } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

const estimateUrls = (fetchStub: ReturnType<typeof stubApi>) =>
  fetchStub.mock.calls.map(([url]) => url).filter((url) => url.includes("/set-aside/estimate"));

// Testing Library matches against text with its whitespace collapsed, and Intl puts no-break spaces in amounts.
const fill = (template: string, values: Record<string, string | number>) =>
  template.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key])).replace(/\s+/g, " ");

function renderDashboard(locale: Locale = "en") {
  const user = userEvent.setup();
  renderInApp(<DashboardPage />, { locale });
  return user;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  // Only Date: the overview opens on today's quarter, and the timers Testing Library waits with stay real.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2025-02-10T12:00:00Z"));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("DashboardPage", () => {
  it("estimates from the stored profile for today's quarter and shows the API's answer", async () => {
    const fetchStub = stubApi();
    renderDashboard();

    const estimate = MESSAGES.en.Dashboard.estimate;
    await screen.findByRole("heading", { name: estimate.heading });
    expect(estimateUrls(fetchStub)).toEqual([`${ESTIMATE_URL}?asOf=Q1`]);
    expect(fetchStub.mock.calls.every(([, init]) => (init?.method ?? "GET") === "GET")).toBe(true);

    expect(screen.getByText(fill(estimate.holdBack, { share: formatShare("0.1941", "en") }))).toBeInTheDocument();
    expect(screen.getByText(formatMoney("1228.99", "en"))).toBeInTheDocument();
    expect(
      screen.getByText(fill(estimate.due, { from: formatDate("2025-04-01", "en"), by: formatDate("2025-04-22", "en") }), { exact: false }),
    ).toBeInTheDocument();
    expect(screen.getByText(formatMoney("80.00", "en"))).toBeInTheDocument();
    expect(screen.getByText(fill(estimate.payableIn, { month: formatMonth("2026-06", "en") }))).toBeInTheDocument();
    expect(screen.getAllByText(formatMoney("0.00", "en"))).toHaveLength(2);
    expect(screen.getByText(fill(estimate.config, { year: 2025, hash: g12Estimate.configHash }))).toBeInTheDocument();
    expect(screen.getByText(fill(MESSAGES.en.Dashboard.basis, { year: 2025 }), { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: MESSAGES.en.Dashboard.profile.edit })).toHaveAttribute("href", "/settings");
  });

  it.each([
    ["2026-09-27T12:00:00Z", "2025-01-15", "Q4"],
    ["2024-11-02T12:00:00Z", "2025-01-15", "Q1"],
    ["2025-05-20T12:00:00Z", "2025-01-15", "Q2"],
    ["2025-02-10T12:00:00Z", "2025-08-01", "Q3"],
  ])("on %s with alta %s opens on %s: today's quarter of the tax year, never before the alta", async (today, alta, quarter) => {
    vi.setSystemTime(new Date(today));
    const fetchStub = stubApi({ profiles: { status: 200, body: [{ ...g12Profile, activity: { ...g12Profile.activity, alta } }] } });
    renderDashboard();

    await screen.findByRole("heading", { name: MESSAGES.en.Dashboard.estimate.heading });

    expect(screen.getByLabelText(MESSAGES.en.Dashboard.asOf)).toHaveValue(quarter);
    expect(estimateUrls(fetchStub)).toEqual([`${ESTIMATE_URL}?asOf=${quarter}`]);
  });

  it("asks for another quarter's estimate when the quarter changes", async () => {
    const fetchStub = stubApi();
    const user = renderDashboard();
    await screen.findByRole("heading", { name: MESSAGES.en.Dashboard.estimate.heading });

    await user.selectOptions(screen.getByLabelText(MESSAGES.en.Dashboard.asOf), "Q3");

    await waitFor(() => expect(estimateUrls(fetchStub)).toEqual([`${ESTIMATE_URL}?asOf=Q1`, `${ESTIMATE_URL}?asOf=Q3`]));
  });

  it("sends to settings when there is no profile yet, and asks for no estimate", async () => {
    const fetchStub = stubApi({ profiles: { status: 200, body: [] } });
    renderDashboard();

    const messages = MESSAGES.en.Dashboard.profile;
    expect(await screen.findByRole("heading", { name: messages.missing })).toBeInTheDocument();
    expect(screen.getByText(messages.missingLead)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: messages.enter })).toHaveAttribute("href", "/settings");
    expect(estimateUrls(fetchStub)).toEqual([]);
  });

  it("keeps nothing it was given in browser storage, cookies or the address, and logs nothing", async () => {
    stubApi();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const logged = (["log", "info", "warn", "error", "debug"] as const).map((method) => vi.spyOn(console, method));
    renderDashboard();

    await screen.findByRole("heading", { name: MESSAGES.en.Dashboard.estimate.heading });

    expect(setItem).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).toBe("");
    expect(window.location.search).toBe("");
    for (const spy of logged) expect(spy).not.toHaveBeenCalled();
  });

  it("shows every notice before the figures, warnings first, and the trace grouped by section", async () => {
    stubApi();
    renderDashboard();

    const trace = MESSAGES.en.Dashboard.trace;
    const heading = await screen.findByRole("heading", { name: MESSAGES.en.Dashboard.notices.heading });
    const items = within(heading.closest("[data-slot=card]") as HTMLElement).getAllByRole("listitem");
    expect(items).toHaveLength(g12Estimate.notices.length);
    expect(items[0]).toHaveTextContent(MESSAGES.en.Dashboard.notices.Warning);
    expect(items.at(-1)).toHaveTextContent(MESSAGES.en.Dashboard.notices.Info);
    expect(heading.compareDocumentPosition(screen.getByText(formatMoney("1228.99", "en"))) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    for (const section of new Set(g12Estimate.trace.map((step) => step.section as keyof typeof trace.sections))) {
      const count = g12Estimate.trace.filter((step) => step.section === section).length;
      expect(screen.getByText(fill(trace.section, { name: trace.sections[section], count }))).toBeInTheDocument();
    }
  });

  it.each(LOCALES)("shows the overview's labels in %s", async (locale) => {
    stubApi();
    const messages = MESSAGES[locale].Dashboard;
    renderDashboard(locale);

    expect(await screen.findByRole("heading", { name: messages.estimate.heading })).toBeInTheDocument();
    expect(screen.getByLabelText(messages.asOf)).toBeInTheDocument();
    expect(screen.getByText(fill(messages.basis, { year: 2025 }), { exact: false })).toBeInTheDocument();
    expect(screen.getByText(fill(messages.estimate.holdBack, { share: formatShare("0.1941", locale) }))).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.notices.heading })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.trace.heading })).toBeInTheDocument();
  });

  it.each(LOCALES)("says in %s that there is no profile yet", async (locale) => {
    stubApi({ profiles: { status: 200, body: [] } });
    renderDashboard(locale);

    expect(await screen.findByRole("heading", { name: MESSAGES[locale].Dashboard.profile.missing })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: MESSAGES[locale].Dashboard.profile.enter })).toBeInTheDocument();
  });

  it("says a declared gap is not published yet, with the engine's reason and the way to another tax year", async () => {
    const detail = "calendar.modelo130 Q4 of tax year 2026 is declared incomplete in this configuration.";
    stubApi({
      estimate: problem(422, { type: "https://gestoria.local/problems/config-gap", title: "The configuration does not cover this calculation", detail }),
    });
    renderDashboard();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(MESSAGES.en.Dashboard.failure.gap);
    expect(alert).toHaveTextContent(detail);
    expect(within(alert).getByRole("link", { name: MESSAGES.en.Dashboard.failure.gapSettings })).toHaveAttribute("href", "/settings");
    expect(screen.queryByRole("heading", { name: MESSAGES.en.Dashboard.estimate.heading })).not.toBeInTheDocument();
  });

  it("says the engine cannot estimate a profile it refuses, with its reason", async () => {
    const detail = "Quarter Q1 of 2025 ends in 2025-03, before the alta month 2025-08 (alta 2025-08-01).";
    stubApi({
      estimate: problem(422, { type: "https://gestoria.local/problems/estimate-refused", title: "The engine cannot estimate this input", detail }),
    });
    renderDashboard();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(MESSAGES.en.Dashboard.failure.refused);
    expect(alert).toHaveTextContent(detail);
  });

  // An unreachable API is retried twice, a second and then two apart (data/query-provider.tsx), before the page says so.
  it("says the API is not answering when it cannot be reached", { timeout: 10_000 }, async () => {
    stubApi({ profiles: "unreachable" });
    renderDashboard();

    expect(await screen.findByRole("alert", {}, { timeout: 8_000 })).toHaveTextContent(MESSAGES.en.Dashboard.failure.network);
  });
});

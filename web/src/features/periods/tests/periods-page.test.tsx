import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import g12AnnualTrueUp from "@tests/fixtures/g12-annual-true-up.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import g12Quarter from "@tests/fixtures/g12-quarter.json";
import g12QuarterNegative from "@tests/fixtures/g12-quarter-negative.json";
import taxYearsFixture from "@tests/fixtures/tax-years.json";
import { MESSAGES, renderInApp } from "@tests/render";

import type { QuarterResult } from "@/data/periods";
import { PeriodsPage } from "@/features/periods";
import { LOCALES, type Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney, formatMonth, formatShare } from "@/shared/lib/format";

// The API's own answers (tests/GestorIA.Api.Tests/WebFixtures.cs keeps them so): G12's taxpayer stored as the profile, its
// Q1 Modelo 130 and its annual true-up (#71).
const QUARTER_URL = `http://localhost:5080/api/v1/profiles/${g12Profile.id}/calculations/quarter`;
const ANNUAL_URL = `http://localhost:5080/api/v1/profiles/${g12Profile.id}/calculations/annual-true-up`;
const TAX_YEAR_2025 = taxYearsFixture.find((year) => year.taxYear === 2025)!;

type Answer = { status: number; body: unknown } | "unreachable";

const problem = (status: number, body: Record<string, unknown>): Answer => ({ status, body: { status, ...body } });

function stubApi({
  profiles = { status: 200, body: [g12Profile] },
  taxYears = { status: 200, body: taxYearsFixture },
  quarter = { status: 200, body: g12Quarter },
  annual = { status: 200, body: g12AnnualTrueUp },
}: { profiles?: Answer; taxYears?: Answer; quarter?: Answer; annual?: Answer } = {}) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    const answer = url.includes("/calculations/annual-true-up")
      ? annual
      : url.includes("/calculations/quarter")
        ? quarter
        : url.includes("/config/tax-years")
          ? taxYears
          : profiles;
    if (answer === "unreachable") throw new TypeError("fetch failed");
    const contentType = answer.status >= 400 ? "application/problem+json" : "application/json";
    return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { "Content-Type": contentType } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

const urlsMatching = (fetchStub: ReturnType<typeof stubApi>, pattern: string) =>
  fetchStub.mock.calls.map(([url]) => url).filter((url) => url.includes(pattern));

// Testing Library matches against text with its whitespace collapsed, and Intl puts no-break spaces in amounts.
const fill = (template: string, values: Record<string, string | number>) =>
  template.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key])).replace(/\s+/g, " ");

function renderPeriods(locale: Locale = "en") {
  const user = userEvent.setup();
  renderInApp(<PeriodsPage />, { locale });
  return user;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  // Only Date: the page opens on today's quarter, and the timers Testing Library waits with stay real.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2025-02-10T12:00:00Z"));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("PeriodsPage", () => {
  it("opens on quarter mode: the casilla sheet with real casilla numbers and the trace grouped by section", async () => {
    stubApi();
    renderPeriods();

    const quarter = MESSAGES.en.Periods.quarter;
    await screen.findByRole("heading", { name: fill(quarter.heading, { quarter: "Q1" }) });

    expect(screen.getByText(quarter.aIngresar).nextElementSibling).toHaveTextContent(formatMoney("1228.99", "en"));
    expect(
      screen.getByText(fill(quarter.due, { from: formatDate("2025-04-01", "en"), by: formatDate("2025-04-22", "en") }), { exact: false }),
    ).toBeInTheDocument();

    const casillas = MESSAGES.en.Periods.casillas;
    const line = TAX_YEAR_2025.modelo130Lines.rendimientoNeto;
    expect(screen.getByText(`${fill(casillas.line, { number: line })} · ${casillas.rendimientoNeto}`)).toBeInTheDocument();
    expect(screen.getByText(formatMoney("6644.94", "en"))).toBeInTheDocument();

    const trace = MESSAGES.en.Trace;
    expect(screen.getByText(fill(trace.section, { name: trace.sections.Modelo130, count: g12Quarter.trace.length }))).toBeInTheDocument();
  });

  it.each(LOCALES)("says in %s that a Q1 with nothing to pay is still filed, a deducir, in the same window", async (locale) => {
    stubApi({ quarter: { status: 200, body: g12QuarterNegative } });
    renderPeriods(locale);

    const quarter = MESSAGES[locale].Periods.quarter;
    await screen.findByRole("heading", { name: fill(quarter.heading, { quarter: "Q1" }) });

    expect(screen.getByText(quarter.aIngresar).nextElementSibling).toHaveTextContent(formatMoney("0.00", locale).replace(/\s+/g, " "));
    const window = { from: formatDate("2025-04-01", locale), by: formatDate("2025-04-22", locale) };
    expect(screen.getByText(fill(quarter.dueADeducir, window), { exact: false })).toBeInTheDocument();
    expect(screen.queryByText(fill(quarter.due, window), { exact: false })).not.toBeInTheDocument();
  });

  it("says a Q4 with filing negativa is filed as negativa, since nothing is left to deduct from", async () => {
    stubApi({ quarter: { status: 200, body: { ...g12QuarterNegative, quarter: "Q4", filing: "negativa" } } });
    renderPeriods();

    const quarter = MESSAGES.en.Periods.quarter;
    await screen.findByRole("heading", { name: fill(quarter.heading, { quarter: "Q4" }) });

    const window = { from: formatDate("2025-04-01", "en"), by: formatDate("2025-04-22", "en") };
    expect(screen.getByText(fill(quarter.dueNegativa, window), { exact: false })).toBeInTheDocument();
    expect(screen.queryByText(fill(quarter.dueADeducir, window), { exact: false })).not.toBeInTheDocument();
  });

  it("asks for another quarter's result when the quarter changes", async () => {
    const fetchStub = stubApi();
    const user = renderPeriods();
    await screen.findByRole("heading", { name: fill(MESSAGES.en.Periods.quarter.heading, { quarter: "Q1" }) });

    await user.selectOptions(screen.getByLabelText(MESSAGES.en.Periods.quarter.asOf), "Q3");

    await waitFor(() => expect(urlsMatching(fetchStub, "/calculations/quarter")).toEqual([`${QUARTER_URL}?quarter=Q1`, `${QUARTER_URL}?quarter=Q3`]));
    expect(fetchStub.mock.calls.filter(([url]) => url.includes("/calculations/quarter")).every(([, init]) => init?.method === "POST")).toBe(true);
  });

  it("fetches and shows the true-up gap, payableIn and the Modelo-100-not-yet note prominently when switching to year mode", async () => {
    const fetchStub = stubApi();
    const user = renderPeriods();
    await screen.findByRole("heading", { name: fill(MESSAGES.en.Periods.quarter.heading, { quarter: "Q1" }) });

    await user.click(screen.getByRole("button", { name: MESSAGES.en.Periods.toggle.year }));

    const year = MESSAGES.en.Periods.year;
    const banner = await screen.findByText(year.banner);
    expect(banner).toBeVisible();
    expect(screen.getByRole("heading", { name: year.heading })).toBeInTheDocument();
    expect(screen.getByText(year.gap).nextElementSibling).toHaveTextContent(formatMoney("0.00", "en"));
    const window = { from: formatDate("2026-04-08", "en"), by: formatDate("2026-06-30", "en") };
    expect(screen.getByText(fill(year.dueNothingToPay, window))).toBeInTheDocument();
    expect(screen.queryByText(fill(year.payableIn, { month: formatMonth("2026-06", "en") }), { exact: false })).not.toBeInTheDocument();
    expect(screen.getByText(formatMoney("3365.93", "en"))).toBeInTheDocument();
    expect(screen.getByText(formatShare("0.27", "en"))).toBeInTheDocument();

    expect(urlsMatching(fetchStub, "/calculations/annual-true-up")).toEqual([ANNUAL_URL]);
    expect(fetchStub.mock.calls.find(([url]) => url === ANNUAL_URL)?.[1]?.method).toBe("POST");
  });

  it("says when a gap beyond the advances is payable and in which window", async () => {
    stubApi({ annual: { status: 200, body: { ...g12AnnualTrueUp, gap: "512.40" } } });
    const user = renderPeriods();
    await screen.findByRole("heading", { name: fill(MESSAGES.en.Periods.quarter.heading, { quarter: "Q1" }) });

    await user.click(screen.getByRole("button", { name: MESSAGES.en.Periods.toggle.year }));

    const year = MESSAGES.en.Periods.year;
    expect(await screen.findByText(year.gap)).toBeInTheDocument();
    expect(screen.getByText(year.gap).nextElementSibling).toHaveTextContent(formatMoney("512.40", "en"));
    expect(screen.getByText(fill(year.payableIn, { month: formatMonth("2026-06", "en") }), { exact: false })).toBeInTheDocument();
    expect(
      screen.getByText(fill(year.due, { from: formatDate("2026-04-08", "en"), by: formatDate("2026-06-30", "en") }), { exact: false }),
    ).toBeInTheDocument();
  });

  it.each(LOCALES)("says in %s that a zero gap leaves nothing to pay but the Renta to file", async (locale) => {
    stubApi();
    const user = renderPeriods(locale);
    await screen.findByRole("heading", { name: fill(MESSAGES[locale].Periods.quarter.heading, { quarter: "Q1" }) });

    await user.click(screen.getByRole("button", { name: MESSAGES[locale].Periods.toggle.year }));

    const year = MESSAGES[locale].Periods.year;
    const window = { from: formatDate("2026-04-08", locale), by: formatDate("2026-06-30", locale) };
    expect(await screen.findByText(fill(year.dueNothingToPay, window))).toBeInTheDocument();
    expect(screen.queryByText(fill(year.due, window), { exact: false })).not.toBeInTheDocument();
  });

  it("sends to settings when there is no profile yet, and asks for no calculation", async () => {
    const fetchStub = stubApi({ profiles: { status: 200, body: [] } });
    renderPeriods();

    const messages = MESSAGES.en.Periods.profile;
    expect(await screen.findByRole("heading", { name: messages.missing })).toBeInTheDocument();
    expect(screen.getByText(messages.missingLead)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: messages.enter })).toHaveAttribute("href", "/settings");
    expect(urlsMatching(fetchStub, "/calculations/")).toEqual([]);
  });

  it("says a declared gap is not published yet, with the engine's reason and the way to another tax year", async () => {
    const detail = "calendar.modelo130 Q4 of tax year 2026 is declared incomplete in this configuration.";
    stubApi({
      quarter: problem(422, { type: "https://gestoria.local/problems/config-gap", title: "The configuration does not cover this calculation", detail }),
    });
    renderPeriods();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(MESSAGES.en.Failure.gap);
    expect(alert).toHaveTextContent(detail);
    expect(within(alert).getByRole("link", { name: MESSAGES.en.Failure.gapSettings })).toHaveAttribute("href", "/settings");
  });

  it("says the API is not answering when it cannot be reached", { timeout: 10_000 }, async () => {
    stubApi({ profiles: "unreachable" });
    renderPeriods();

    expect(await screen.findByRole("alert", {}, { timeout: 8_000 })).toHaveTextContent(MESSAGES.en.Failure.network);
  });

  it("keeps nothing it was given in browser storage, cookies or the address, and logs nothing", async () => {
    stubApi();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const logged = (["log", "info", "warn", "error", "debug"] as const).map((method) => vi.spyOn(console, method));
    renderPeriods();

    await screen.findByRole("heading", { name: fill(MESSAGES.en.Periods.quarter.heading, { quarter: "Q1" }) });

    expect(setItem).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).toBe("");
    expect(window.location.search).toBe("");
    for (const spy of logged) expect(spy).not.toHaveBeenCalled();
  });

  it.each(LOCALES)("shows the page's labels in %s", async (locale) => {
    stubApi();
    const messages = MESSAGES[locale].Periods;
    renderPeriods(locale);

    expect(await screen.findByRole("heading", { level: 1, name: messages.title })).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: messages.toggle.quarter })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: messages.toggle.year })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: messages.quarter.casillasHeading })).toBeInTheDocument();
  });

  it.each(LOCALES)("says in %s that there is no profile yet", async (locale) => {
    stubApi({ profiles: { status: 200, body: [] } });
    renderPeriods(locale);

    expect(await screen.findByRole("heading", { name: MESSAGES[locale].Periods.profile.missing })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: MESSAGES[locale].Periods.profile.enter })).toBeInTheDocument();
  });

  describe("the basis of a period", () => {
    const basis = MESSAGES.en.Periods.basis;
    const withLedger = (fixture: typeof g12Quarter | typeof g12AnnualTrueUp, ledger: Partial<QuarterResult["ledger"]>) => ({
      status: 200,
      body: { ...fixture, ledger: { ...fixture.ledger, ...ledger } },
    });

    it("says the quarter rests on the projection when no closed quarter's movements are reviewed", async () => {
      stubApi();
      renderPeriods();

      expect(await screen.findByText(basis.projection)).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: basis.review })).not.toBeInTheDocument();
    });

    it("says through which quarter the actuals run, and what awaits review or an invoice", async () => {
      stubApi({ quarter: withLedger(g12Quarter, { actualsThrough: "Q1", counted: 2, awaitingReview: 3, awaitingInvoice: 1 }) });
      renderPeriods();

      expect(
        await screen.findByText("Based on actuals through Q1, 2 classified movements; the projection covers the rest of the year."),
      ).toBeInTheDocument();
      expect(screen.queryByText(basis.projection)).not.toBeInTheDocument();
      expect(screen.getByText(/^3 movements of the closed quarters await review;/)).toBeInTheDocument();
      expect(screen.getByRole("link", { name: basis.review })).toHaveAttribute("href", "/transactions");
      expect(screen.getByText(/^1 expense awaits an invoice and is not counted/)).toBeInTheDocument();
    });

    it("says the year's actuals cover it all once they run through Q4", async () => {
      stubApi({ annual: withLedger(g12AnnualTrueUp, { actualsThrough: "Q4", counted: 5 }) });
      const user = renderPeriods();
      await screen.findByRole("heading", { name: fill(MESSAGES.en.Periods.quarter.heading, { quarter: "Q1" }) });

      await user.click(screen.getByRole("button", { name: MESSAGES.en.Periods.toggle.year }));

      expect(await screen.findByText("Based on actuals through Q4, 5 classified movements, which cover the whole year.")).toBeInTheDocument();
      expect(screen.queryByText(/the projection covers the rest of the year/)).not.toBeInTheDocument();
    });
  });
});

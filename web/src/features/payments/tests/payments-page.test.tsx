import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import databaseUnavailable from "@tests/fixtures/database-unavailable.json";
import g12Calendar from "@tests/fixtures/g12-calendar.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import { MESSAGES, renderInApp } from "@tests/render";

import { PaymentsPage } from "@/features/payments";
import { LOCALES, type Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney, formatMonth } from "@/shared/lib/format";

const CALENDAR_URL = `http://localhost:5080/api/v1/profiles/${g12Profile.id}/calendar`;

type Answer = { status: number; body: unknown } | "unreachable";

const problem = (status: number, body: Record<string, unknown>): Answer => ({ status, body: { status, ...body } });

function stubApi({
  profiles = { status: 200, body: [g12Profile] },
  calendar = { status: 200, body: g12Calendar },
  ics = { status: 200, body: "BEGIN:VCALENDAR" },
}: { profiles?: Answer; calendar?: Answer; ics?: Answer } = {}) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    const answer = url.includes("/calendar.ics") ? ics : url.includes("/calendar") ? calendar : profiles;
    if (answer === "unreachable") throw new TypeError("fetch failed");
    const contentType = answer.status >= 400 ? "application/problem+json" : "application/json";
    return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { "Content-Type": contentType } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

function renderPayments(locale: Locale = "en") {
  const user = userEvent.setup();
  renderInApp(<PaymentsPage />, { locale });
  return user;
}

// G12's tax year is 2025: a day inside it, so its later obligations are still to come. Only Date is faked, so React Query's
// own timers and retries run as usual.
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2025-03-15T10:00:00Z"));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const modelo130 = g12Calendar.obligations.find((o) => o.kind === "Modelo130" && o.period === "Q1")!;
const modelo303 = g12Calendar.obligations.find((o) => o.kind === "Modelo303" && o.period === "Q1")!;
const cuota = g12Calendar.obligations.find((o) => o.kind === "SeguridadSocial" && o.period === "2025-01")!;

describe("PaymentsPage", () => {
  it("lists every obligation of the stored profile's calendar, from the API in one call", async () => {
    const fetchStub = stubApi();
    renderPayments();

    await screen.findByRole("table");
    const rows = screen.getAllByRole("row").slice(1); // the header row is not an obligation
    expect(rows).toHaveLength(g12Calendar.obligations.length);
    expect(fetchStub.mock.calls.filter(([url]) => (url as string).includes("/calendar")).map(([url]) => url)).toEqual([CALENDAR_URL]);
  });

  it("shows a known amount formatted for the locale, never a raw API string", async () => {
    stubApi();
    renderPayments();

    await screen.findByRole("table");
    expect(screen.getByText(formatMoney(modelo130.amount.euros as string, "en"))).toBeInTheDocument();
    expect(screen.getAllByText(formatDate(modelo130.dueFrom, "en"), { exact: false }).length).toBeGreaterThan(0);
  });

  it("shows 'not known yet' rather than a guessed amount for a form with no calculator", async () => {
    stubApi();
    renderPayments();

    await screen.findByRole("table");
    expect(modelo303.amount.kind).toBe("notYetKnown");
    expect(screen.getAllByText(MESSAGES.en.Payments.amount.notYetKnown).length).toBeGreaterThan(0);
  });

  it("formats the TGSS cuota's monthly period and shows its known amount", async () => {
    stubApi();
    renderPayments();

    await screen.findByRole("table");
    expect(screen.getByText(formatMonth(cuota.period, "en"), { exact: false })).toBeInTheDocument();
    expect(screen.getAllByText(formatMoney(cuota.amount.euros as string, "en")).length).toBeGreaterThan(0);
  });

  it("shows a local-holidays note for both directions filing deadlines and the TGSS cuota move", async () => {
    stubApi();
    renderPayments();

    expect(await screen.findByText(MESSAGES.en.Payments.localHolidays.filing)).toBeInTheDocument();
    expect(screen.getByText(MESSAGES.en.Payments.localHolidays.cuota)).toBeInTheDocument();
  });

  it("labels a Modelo 349 row as owed only for a quarter with intra-EU operations", async () => {
    stubApi();
    renderPayments();

    await screen.findByRole("table");
    expect(screen.getAllByText(MESSAGES.en.Payments.notes.modelo349).length).toBeGreaterThan(0);
  });

  it("says above which amount Modelo 349 turns monthly, with the year's own figure from the API", async () => {
    stubApi();
    renderPayments("es");

    await screen.findByRole("table");
    const cap = formatMoney(g12Calendar.modelo349QuarterlyFilingCap, "es");
    const note = MESSAGES.es.Payments.notes.modelo349Monthly.replace("{cap}", cap);
    // A function matcher: getByText's default normalizer turns the amount's no-break space into a plain one on the page side only.
    expect(screen.getByText((_, element) => element?.tagName === "P" && element.textContent === note)).toBeInTheDocument();
  });

  it("offers to export the calendar, with amounts opt in rather than on by default", async () => {
    stubApi();
    renderPayments();

    await screen.findByRole("table");
    const checkbox = screen.getByLabelText(MESSAGES.en.Payments.export.includeAmounts);
    expect(checkbox).not.toBeChecked();
    expect(screen.getByRole("button", { name: MESSAGES.en.Payments.export.button })).toBeEnabled();
    expect(screen.queryByText(MESSAGES.en.Payments.export.nothingUpcoming)).not.toBeInTheDocument();
  });

  it("says nothing is still to come and offers no export once every obligation's due date has passed", async () => {
    vi.setSystemTime(new Date("2026-09-28T10:00:00Z"));
    stubApi();
    renderPayments("uk");

    await screen.findByRole("table");
    expect(screen.getByText(MESSAGES.uk.Payments.export.nothingUpcoming)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: MESSAGES.uk.Payments.export.button })).toBeDisabled();
  });

  // 22:30 UTC on the last due date is already the next day in Madrid, the day the API counts from.
  it("counts today by Madrid's date, not UTC's", async () => {
    const lastDue = g12Calendar.obligations.map((o) => o.dueBy).sort().at(-1)!;
    vi.setSystemTime(new Date(`${lastDue}T22:30:00Z`));
    stubApi();
    renderPayments();

    await screen.findByRole("table");
    expect(screen.getByRole("button", { name: MESSAGES.en.Payments.export.button })).toBeDisabled();
  });

  it("says nothing is still to come when the API answers so on export", async () => {
    stubApi({
      ics: problem(422, { type: "https://gestoria.local/problems/no-upcoming-obligations", title: "No obligation is still to come", detail: "…" }),
    });
    const user = renderPayments("es");

    await screen.findByRole("table");
    await user.click(screen.getByRole("button", { name: MESSAGES.es.Payments.export.button }));

    expect(await screen.findByText(MESSAGES.es.Payments.export.nothingUpcoming)).toBeInTheDocument();
    expect(screen.queryByText(MESSAGES.es.Payments.export.failed)).not.toBeInTheDocument();
  });

  it("sends to settings when there is no profile yet, and asks for no calendar", async () => {
    const fetchStub = stubApi({ profiles: { status: 200, body: [] } });
    renderPayments();

    const messages = MESSAGES.en.Payments.profile;
    expect(await screen.findByRole("heading", { name: messages.missing })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: messages.enter })).toHaveAttribute("href", "/settings");
    expect(fetchStub.mock.calls.some(([url]) => (url as string).includes("/calendar"))).toBe(false);
  });

  it("says a declared gap is not published yet, with the engine's reason and the way to another tax year", async () => {
    const detail = "calendar.modelo130 Q4 of tax year 2026 is declared incomplete in this configuration.";
    stubApi({
      calendar: problem(422, { type: "https://gestoria.local/problems/config-gap", title: "The configuration does not cover this calculation", detail }),
    });
    renderPayments();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(MESSAGES.en.Failure.gap);
    expect(alert).toHaveTextContent(detail);
    expect(within(alert).getByRole("link", { name: MESSAGES.en.Failure.gapSettings })).toHaveAttribute("href", "/settings");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("says the API is not answering when it cannot be reached", { timeout: 10_000 }, async () => {
    stubApi({ calendar: "unreachable" });
    renderPayments();

    expect(await screen.findByRole("alert", {}, { timeout: 8_000 })).toHaveTextContent(MESSAGES.en.Failure.network);
  });

  // A 503 is retried like an unreachable API before the page says so.
  it("says the database is not answering when the API cannot reach it", { timeout: 10_000 }, async () => {
    stubApi({ calendar: { status: 503, body: databaseUnavailable } });
    renderPayments();

    const alert = await screen.findByRole("alert", {}, { timeout: 8_000 });
    expect(alert).toHaveTextContent(MESSAGES.en.Failure.database);
    expect(alert).not.toHaveTextContent(MESSAGES.en.Failure.network);
  });

  it.each(LOCALES)("shows the calendar's labels in %s", async (locale) => {
    stubApi();
    const messages = MESSAGES[locale].Payments;
    renderPayments(locale);

    expect(screen.getByRole("heading", { name: messages.title })).toBeInTheDocument();
    await screen.findByRole("table");
    expect(screen.getByText(messages.table.obligation)).toBeInTheDocument();
    expect(screen.getByText(messages.localHolidays.filing)).toBeInTheDocument();
  });

  it.each(LOCALES)("says in %s that there is no profile yet", async (locale) => {
    stubApi({ profiles: { status: 200, body: [] } });
    renderPayments(locale);

    expect(await screen.findByRole("heading", { name: MESSAGES[locale].Payments.profile.missing })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: MESSAGES[locale].Payments.profile.enter })).toBeInTheDocument();
  });
});

import { readFileSync } from "node:fs";
import path from "node:path";

import { screen, waitFor, within } from "@testing-library/react";
import userEvent, { type UserEvent } from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { MESSAGES, renderInApp } from "@tests/render";

import { DashboardPage } from "@/features/dashboard";
import { LOCALES, type Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney, formatMonth, formatShare } from "@/shared/lib/format";

import g14Estimate from "./fixtures/g14-estimate.json";
import taxYears from "./fixtures/tax-years.json";

// The API's own answers (tests/GestorIA.Api.Tests/WebFixtures.cs keeps them so) and G14's input file from the goldens.
const G14_INPUT: unknown = JSON.parse(
  readFileSync(path.resolve(import.meta.dirname, "../../../../../tests/golden/2025/G14.json"), "utf8"),
).setAside.inputs;

type Answer = { status: number; body: unknown } | "unreachable";

const problem = (status: number, body: Record<string, unknown>): Answer => ({ status, body: { status, ...body } });

function stubApi({
  years = { status: 200, body: taxYears },
  estimate = { status: 200, body: g14Estimate },
}: { years?: Answer; estimate?: Answer } = {}) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    const answer = url.includes("/set-aside/estimate") ? estimate : years;
    if (answer === "unreachable") throw new TypeError("fetch failed");
    const contentType = answer.status >= 400 ? "application/problem+json" : "application/json";
    return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { "Content-Type": contentType } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

function postedRequest(fetchStub: ReturnType<typeof stubApi>) {
  const call = fetchStub.mock.calls.find(([url]) => url.includes("/set-aside/estimate"));
  if (call === undefined) throw new Error("no estimate was requested");
  const [url, init] = call;
  return { url, method: init?.method, body: JSON.parse(String(init?.body)) as unknown };
}

// Testing Library matches against text with its whitespace collapsed, and Intl puts no-break spaces in amounts.
const fill = (template: string, values: Record<string, string | number>) =>
  template.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key])).replace(/\s+/g, " ");

async function renderDashboard(locale: Locale = "en") {
  const user = userEvent.setup();
  renderInApp(<DashboardPage />, { locale });
  await screen.findByRole("button", { name: MESSAGES[locale].Dashboard.form.submit });
  return user;
}

async function typeG14(user: UserEvent) {
  const form = MESSAGES.en.Dashboard.form;
  const field = (label: string) => screen.getByLabelText(label);

  await user.selectOptions(field(form.taxYear), "2025");
  await user.selectOptions(field(form.asOf), "Q2");
  await user.selectOptions(field(form.region), "VC");
  await user.type(field(form.employmentIngresos), "0.00");
  await user.type(field(form.employmentSeguridadSocial), "0.00");
  await user.type(field(form.alta), "2025-01-15");
  await user.click(screen.getByRole("radio", { name: form.first }));
  await user.type(field(form.ingresosFromFormerEmployer), "0.00");
  await user.click(screen.getByRole("button", { name: form.addQuarter }));
  await user.type(field(form.ingresosYtd), "6000.00");
  await user.type(field(form.gastosYtd), "345.33");
  await user.type(field(form.cuotasSsYtd), "205.33");
  await user.type(field(form.projectionIngresos), "27000.00");
  await user.type(field(form.projectionGastos), "900.00");
  await user.type(field(form.baseCotizacion), "1356.21");
}

async function loadFile(user: UserEvent, content: string, locale: Locale = "en") {
  const file = new File([content], "input.json", { type: "application/json" });
  await user.upload(screen.getByLabelText(MESSAGES[locale].Dashboard.form.loadFile), file);
}

async function calculateG14FromFile(user: UserEvent, locale: Locale = "en") {
  await loadFile(user, JSON.stringify(G14_INPUT), locale);
  await user.click(screen.getByRole("button", { name: MESSAGES[locale].Dashboard.form.submit }));
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("DashboardPage", () => {
  it("posts exactly G14's input file when its figures are typed, and shows G14's estimate as the API answered it", async () => {
    const fetchStub = stubApi();
    const user = await renderDashboard();

    await typeG14(user);
    await user.click(screen.getByRole("button", { name: MESSAGES.en.Dashboard.form.submit }));

    const estimate = MESSAGES.en.Dashboard.estimate;
    await screen.findByRole("heading", { name: estimate.heading });
    const request = postedRequest(fetchStub);
    expect(request.url).toBe("http://localhost:5080/api/v1/set-aside/estimate?taxYear=2025");
    expect(request.method).toBe("POST");
    expect(request.body).toEqual(G14_INPUT);

    expect(screen.getByText(fill(estimate.holdBack, { share: formatShare("0.1947", "en") }))).toBeInTheDocument();
    expect(screen.getByText(formatMoney("1507.40", "en"))).toBeInTheDocument();
    expect(
      screen.getByText(fill(estimate.due, { from: formatDate("2025-07-01", "en"), by: formatDate("2025-07-21", "en") }), { exact: false }),
    ).toBeInTheDocument();
    expect(screen.getByText(formatMoney("80.00", "en"))).toBeInTheDocument();
    expect(screen.getByText(fill(estimate.payableIn, { month: formatMonth("2026-06", "en") }))).toBeInTheDocument();
    expect(screen.getAllByText(formatMoney("0.00", "en"))).toHaveLength(2);
    expect(screen.getByText(fill(estimate.config, { year: 2025, hash: g14Estimate.configHash }))).toBeInTheDocument();
  });

  it("posts the same request when the figures come from G14's input file", async () => {
    const fetchStub = stubApi();
    const user = await renderDashboard();

    await user.selectOptions(screen.getByLabelText(MESSAGES.en.Dashboard.form.taxYear), "2025");
    await calculateG14FromFile(user);

    await screen.findByRole("heading", { name: MESSAGES.en.Dashboard.estimate.heading });
    expect(postedRequest(fetchStub).body).toEqual(G14_INPUT);
  });

  it("keeps nothing it was given in browser storage, cookies or the address", async () => {
    stubApi();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const user = await renderDashboard();

    await calculateG14FromFile(user);
    await screen.findByRole("heading", { name: MESSAGES.en.Dashboard.estimate.heading });

    expect(setItem).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).toBe("");
    expect(window.location.search).toBe("");
  });

  it("shows every notice before the figures, warnings first, and the trace grouped by section", async () => {
    stubApi();
    const user = await renderDashboard();

    await calculateG14FromFile(user);

    const trace = MESSAGES.en.Dashboard.trace;
    const heading = await screen.findByRole("heading", { name: MESSAGES.en.Dashboard.notices.heading });
    const items = within(heading.closest("[data-slot=card]") as HTMLElement).getAllByRole("listitem");
    expect(items).toHaveLength(g14Estimate.notices.length);
    expect(items[0]).toHaveTextContent(MESSAGES.en.Dashboard.notices.Warning);
    expect(items.at(-1)).toHaveTextContent(MESSAGES.en.Dashboard.notices.Info);
    expect(heading.compareDocumentPosition(screen.getByText(formatMoney("1507.40", "en"))) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    for (const section of new Set(g14Estimate.trace.map((step) => step.section as keyof typeof trace.sections))) {
      const count = g14Estimate.trace.filter((step) => step.section === section).length;
      expect(screen.getByText(fill(trace.section, { name: trace.sections[section], count }))).toBeInTheDocument();
    }
  });

  it.each(LOCALES)("shows the estimate's labels in %s", async (locale) => {
    stubApi();
    const messages = MESSAGES[locale].Dashboard;
    const user = await renderDashboard(locale);

    await calculateG14FromFile(user, locale);

    expect(await screen.findByRole("heading", { name: messages.estimate.heading })).toBeInTheDocument();
    expect(screen.getByText(fill(messages.estimate.holdBack, { share: formatShare("0.1947", locale) }))).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.notices.heading })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.trace.heading })).toBeInTheDocument();
    const inSection = g14Estimate.trace.filter((step) => step.section === "SeguridadSocial").length;
    expect(screen.getByText(fill(messages.trace.section, { name: messages.trace.sections.SeguridadSocial, count: inSection }))).toBeInTheDocument();
  });

  it("refuses an incomplete or malformed form next to each field and asks the API nothing", async () => {
    const fetchStub = stubApi();
    const user = await renderDashboard();
    const form = MESSAGES.en.Dashboard.form;
    const errors = MESSAGES.en.Dashboard.errors;

    await user.type(screen.getByLabelText(form.employmentIngresos), "1.234,56");
    await user.click(screen.getByRole("radio", { name: form.previousYearNet }));
    await user.type(screen.getByLabelText(form.previousYearNetAmount), "-800.00");
    await user.click(screen.getByRole("button", { name: form.submit }));

    expect(screen.getByLabelText(form.employmentIngresos)).toHaveAccessibleDescription(errors.amount);
    expect(screen.getByLabelText(form.employmentIngresos)).toHaveFocus();
    expect(screen.getByLabelText(form.employmentSeguridadSocial)).toHaveAccessibleDescription(errors.required);
    expect(screen.getByLabelText(form.previousYearNetAmount)).toHaveAttribute("aria-invalid", "false");
    expect(screen.getByLabelText(form.baseCotizacion)).toHaveAccessibleDescription(errors.required);
    expect(fetchStub.mock.calls.some(([url]) => url.includes("/set-aside/estimate"))).toBe(false);
  });

  it("shows the API's refusal of a value next to its field", async () => {
    const reason = '$.activity.projection.baseCotizacion is "99.00"; it must be a base of the 2025 tables.';
    stubApi({
      estimate: problem(400, {
        type: "https://gestoria.local/problems/invalid-input",
        title: "The input is not valid",
        detail: reason,
        errors: { "$.activity.projection.baseCotizacion": [reason] },
      }),
    });
    const user = await renderDashboard();

    await calculateG14FromFile(user);

    await waitFor(() =>
      expect(screen.getByLabelText(MESSAGES.en.Dashboard.form.baseCotizacion)).toHaveAccessibleDescription(
        fill(MESSAGES.en.Dashboard.errors.fromApi, { reason }),
      ),
    );
    expect(screen.getByRole("alert")).toHaveTextContent(MESSAGES.en.Dashboard.failure.invalid);
  });

  it("says a declared gap is not published yet, with the engine's reason", async () => {
    const detail = "seguridadSocial.tarifaPlana.amount of 2026 is not in this configuration.";
    stubApi({
      estimate: problem(422, { type: "https://gestoria.local/problems/config-gap", title: "The configuration does not cover this calculation", detail }),
    });
    const user = await renderDashboard();

    await calculateG14FromFile(user);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(MESSAGES.en.Dashboard.failure.gap);
    expect(alert).toHaveTextContent(detail);
    expect(screen.queryByRole("heading", { name: MESSAGES.en.Dashboard.estimate.heading })).not.toBeInTheDocument();
  });

  it("says the engine cannot estimate figures it refuses, with its reason", async () => {
    const detail = "Actuals are the closed quarters in order from Q1, the first quarter of activity; got [Q2].";
    stubApi({
      estimate: problem(422, { type: "https://gestoria.local/problems/estimate-refused", title: "The engine cannot estimate this input", detail }),
    });
    const user = await renderDashboard();

    await calculateG14FromFile(user);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(MESSAGES.en.Dashboard.failure.refused);
    expect(alert).toHaveTextContent(detail);
  });

  // An unreachable API is retried twice, a second and then two apart (data/query-provider.tsx), before the page says so.
  it("says the API is not answering when it cannot be reached", { timeout: 10_000 }, async () => {
    stubApi({ years: "unreachable" });
    renderInApp(<DashboardPage />, { locale: "en" });

    expect(await screen.findByRole("alert", {}, { timeout: 8_000 })).toHaveTextContent(MESSAGES.en.Dashboard.failure.network);
  });

  it("says a file that is not a JSON object is not an input file, and keeps the form as it was", async () => {
    stubApi();
    const user = await renderDashboard();
    await user.type(screen.getByLabelText(MESSAGES.en.Dashboard.form.projectionIngresos), "100.00");

    await loadFile(user, "not json");

    expect(screen.getByRole("alert")).toHaveTextContent(MESSAGES.en.Dashboard.form.fileUnreadable);
    expect(screen.getByLabelText(MESSAGES.en.Dashboard.form.projectionIngresos)).toHaveValue("100.00");
  });
});

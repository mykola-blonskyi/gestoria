import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import databaseUnavailable from "@tests/fixtures/database-unavailable.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import g12Import from "@tests/fixtures/g12-statement-import.json";
import g12Q1 from "@tests/fixtures/g12-transactions-2025-q1.json";
import g12Year from "@tests/fixtures/g12-transactions-2025.json";
import { MESSAGES, renderInApp } from "@tests/render";

import type { Transaction } from "@/data/transactions";
import { TransactionsPage } from "@/features/transactions";
import type { Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney } from "@/shared/lib/format";

// The API's own answers (tests/GestorIA.Api.Tests/WebFixtures.cs keeps them so): G12's profile with the synthetic BBVA
// statement for 2025 imported into it.
const PROFILE = `http://localhost:5080/api/v1/profiles/${g12Profile.id}`;
const en = MESSAGES.en.Transactions;

type Answer = { status: number; body: unknown } | "unreachable";

const problem = (status: number, body: Record<string, unknown>): Answer => ({ status, body: { status, ...body } });

function stubApi({
  profiles = { status: 200, body: [g12Profile] },
  year = { status: 200, body: g12Year },
  q1 = { status: 200, body: g12Q1 },
  imported = { status: 200, body: g12Import },
}: { profiles?: Answer; year?: Answer; q1?: Answer; imported?: Answer } = {}) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    const answer = url.includes("/bank-statements")
      ? imported
      : url.includes("quarter=Q1")
        ? q1
        : url.includes("/transactions")
          ? year
          : profiles;
    if (answer === "unreachable") throw new TypeError("fetch failed");
    const contentType = answer.status >= 400 ? "application/problem+json" : "application/json";
    return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { "Content-Type": contentType } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

const urls = (fetchStub: ReturnType<typeof stubApi>, part: string) =>
  fetchStub.mock.calls.map(([url]) => url).filter((url) => url.includes(part));

// Testing Library matches against text with its whitespace collapsed, and Intl puts no-break spaces in amounts.
const fill = (template: string, values: Record<string, string | number>) =>
  template.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key])).replace(/\s+/g, " ");

const rows = () => within(screen.getByRole("list")).queryAllByRole("listitem");

function renderPage(locale: Locale = "en") {
  const user = userEvent.setup();
  renderInApp(<TransactionsPage />, { locale });
  return user;
}

const statement = (size = 64) => new File(["x".repeat(size)], "bbva.csv", { type: "text/csv" });

// jsdom does no layout: give every element a viewport's size so the virtualizer can measure.
beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue(DOMRect.fromRect({ width: 800, height: 400 }));
  vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockReturnValue(400);
  vi.spyOn(HTMLElement.prototype, "offsetWidth", "get").mockReturnValue(800);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("TransactionsPage", () => {
  it("lists the stored profile's movements of its tax year with dates, amounts and their kind", async () => {
    const fetchStub = stubApi();
    renderPage("en");

    await screen.findByText(fill(en.list.count, { count: g12Year.length }));
    expect(urls(fetchStub, "/transactions")).toEqual([`${PROFILE}/transactions?year=2025`]);
    expect(screen.getByRole("heading", { name: fill(en.list.heading, { year: 2025 }) })).toBeInTheDocument();
    const [first, second] = rows();
    expect(first).toHaveTextContent(formatDate("2025-01-02", "en"));
    expect(first).toHaveTextContent("TRANSFERENCIA RECIBIDA CLIENTE SINTETICO UNO");
    expect(first).toHaveTextContent(formatMoney("2345.67", "en"));
    expect(first).toHaveTextContent(en.kind.in);
    expect(second).toHaveTextContent(formatMoney("-87.61", "en"));
    expect(second).toHaveTextContent(en.kind.out);
  });

  it.each(["es", "uk", "ru"] as const)("formats dates and amounts for %s from the API's strings", async (locale) => {
    stubApi();
    renderPage(locale);

    await screen.findByText(fill(MESSAGES[locale].Transactions.list.count, { count: g12Year.length }));
    expect(rows()[0]).toHaveTextContent(formatMoney("2345.67", locale).replace(/\s+/g, " "));
    expect(rows()[0]).toHaveTextContent(formatDate("2025-01-02", locale));
  });

  it("asks the API for one quarter and shows its answer", async () => {
    const fetchStub = stubApi();
    const user = renderPage();
    await screen.findByText(fill(en.list.count, { count: g12Year.length }));

    await user.selectOptions(screen.getByLabelText(en.filters.quarter), "Q1");

    await screen.findByText(fill(en.list.count, { count: g12Q1.length }));
    expect(urls(fetchStub, "/transactions")).toContain(`${PROFILE}/transactions?year=2025&quarter=Q1`);
  });

  it("narrows to money in or money out without asking the API again", async () => {
    const fetchStub = stubApi();
    const user = renderPage();
    await screen.findByText(fill(en.list.count, { count: g12Year.length }));
    const calls = fetchStub.mock.calls.length;
    const incoming = g12Year.filter((transaction) => !transaction.amount.startsWith("-"));

    await user.selectOptions(screen.getByLabelText(en.filters.kind), "in");
    expect(screen.getByText(fill(en.list.count, { count: incoming.length }))).toBeInTheDocument();
    for (const row of rows()) expect(row).toHaveTextContent(en.kind.in);

    await user.selectOptions(screen.getByLabelText(en.filters.kind), "out");
    expect(screen.getByText(fill(en.list.count, { count: g12Year.length - incoming.length }))).toBeInTheDocument();
    for (const row of rows()) expect(row).toHaveTextContent(en.kind.out);
    expect(fetchStub.mock.calls.length).toBe(calls);
  });

  it("imports the file as the body, says what was stored and reads the list again", async () => {
    const fetchStub = stubApi();
    const user = renderPage();
    await screen.findByText(fill(en.list.count, { count: g12Year.length }));
    const file = statement();

    await user.upload(screen.getByLabelText(en.import.file), file);
    await user.click(screen.getByRole("button", { name: en.import.submit }));

    expect(await screen.findByText("18 movements read: 18 new, 0 already imported.")).toBeInTheDocument();
    const [url, init] = fetchStub.mock.calls.find(([called]) => called.includes("/bank-statements"))!;
    expect(url).toBe(`${PROFILE}/bank-statements?bank=bbva`);
    expect(init?.method).toBe("POST");
    expect(init?.body).toBe(file);
    expect(new Headers(init?.headers).get("Content-Type")).toBe("text/csv");
    await waitFor(() => expect(urls(fetchStub, "/transactions")).toHaveLength(2));
  });

  it.each([
    ["en", "1 movement read: 1 new, 0 already imported."],
    ["es", "1 movimiento leído: 1 nuevo, 0 ya importados."],
    ["uk", "Прочитано операцій: 1; нових: 1; уже імпортованих: 0."],
    ["ru", "Прочитано операций: 1; новых: 1; уже импортированных: 0."],
  ] as const)("says in %s how many movements one import read, stored and found stored, in the right number", async (locale, text) => {
    stubApi({ imported: { status: 200, body: { ...g12Import, lines: 1, imported: 1, alreadyImported: 0 } } });
    const user = renderPage(locale);
    const messages = MESSAGES[locale].Transactions;
    await screen.findByText(fill(messages.list.count, { count: g12Year.length }));

    await user.upload(screen.getByLabelText(messages.import.file), statement());
    await user.click(screen.getByRole("button", { name: messages.import.submit }));

    expect(await screen.findByText(text)).toBeInTheDocument();
  });

  it("shows every reason the API refused a statement for", async () => {
    stubApi({
      imported: problem(400, {
        type: "https://gestoria.local/problems/invalid-input",
        title: "The input is not valid",
        errors: { "line 12": ["Line 12: Importe must be an amount in euros with at most two decimals, written like -1.234,56."] },
      }),
    });
    const user = renderPage();
    await screen.findByText(fill(en.list.count, { count: g12Year.length }));

    await user.upload(screen.getByLabelText(en.import.file), statement());
    await user.click(screen.getByRole("button", { name: en.import.submit }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(en.failure.refused);
    expect(alert).toHaveTextContent("Line 12: Importe must be");
  });

  it("says a statement is too large when the API does, and before uploading one that is", async () => {
    const fetchStub = stubApi({
      imported: problem(413, { type: "https://gestoria.local/problems/statement-too-large", title: "The statement is too large" }),
    });
    const user = renderPage();
    await screen.findByText(fill(en.list.count, { count: g12Year.length }));

    await user.upload(screen.getByLabelText(en.import.file), statement());
    await user.click(screen.getByRole("button", { name: en.import.submit }));
    expect(await screen.findByRole("alert")).toHaveTextContent(en.failure.tooLarge);

    await user.upload(screen.getByLabelText(en.import.file), statement(2 * 1024 * 1024 + 1));
    await user.click(screen.getByRole("button", { name: en.import.submit }));
    expect(screen.getByRole("alert")).toHaveTextContent(en.failure.tooLarge);
    expect(urls(fetchStub, "/bank-statements")).toHaveLength(1);
  });

  it("says the database is not answering when the API cannot reach it", async () => {
    stubApi({ imported: { status: 503, body: databaseUnavailable } });
    const user = renderPage();
    await screen.findByText(fill(en.list.count, { count: g12Year.length }));

    await user.upload(screen.getByLabelText(en.import.file), statement());
    await user.click(screen.getByRole("button", { name: en.import.submit }));

    expect(await screen.findByRole("alert")).toHaveTextContent(en.failure.database);
  });

  it("sends to settings when there is no profile yet", async () => {
    stubApi({ profiles: { status: 200, body: [] } });
    renderPage();

    expect(await screen.findByRole("link", { name: en.profile.enter })).toHaveAttribute("href", "/settings");
    expect(screen.queryByLabelText(en.import.file)).not.toBeInTheDocument();
  });

  it("renders ten thousand movements as a window of rows and filters them by kind", async () => {
    const many: Transaction[] = Array.from({ length: 10_000 }, (_, index) => ({
      id: `00000000-0000-0000-0000-${String(index).padStart(12, "0")}`,
      bookingDate: "2025-06-15",
      valueDate: "2025-06-15",
      description: `SYNTHETIC MOVEMENT ${index}`,
      amount: index % 4 === 0 ? `${index}.25` : `-${index % 97}.10`,
      balance: null,
    }));
    stubApi({ year: { status: 200, body: many } });
    const user = renderPage();

    await screen.findByText(fill(en.list.count, { count: 10_000 }));
    expect(rows().length).toBeGreaterThan(0);
    expect(rows().length).toBeLessThan(30);
    expect(rows()[0]).toHaveAttribute("aria-setsize", "10000");
    expect(screen.queryByText("SYNTHETIC MOVEMENT 9999")).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText(en.filters.kind), "in");
    expect(screen.getByText(fill(en.list.count, { count: 2_500 }))).toBeInTheDocument();
    expect(rows()[0]).toHaveAttribute("aria-setsize", "2500");
  });

  it("keeps nothing it was given in browser storage, cookies or the address, and logs nothing", async () => {
    stubApi();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const logged = (["log", "info", "warn", "error", "debug"] as const).map((method) => vi.spyOn(console, method));
    const user = renderPage();

    await screen.findByText(fill(en.list.count, { count: g12Year.length }));
    await user.upload(screen.getByLabelText(en.import.file), statement());
    await user.click(screen.getByRole("button", { name: en.import.submit }));
    await screen.findByText("18 movements read: 18 new, 0 already imported.");

    expect(setItem).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).toBe("");
    expect(window.location.search).toBe("");
    for (const spy of logged) expect(spy).not.toHaveBeenCalled();
  });
});

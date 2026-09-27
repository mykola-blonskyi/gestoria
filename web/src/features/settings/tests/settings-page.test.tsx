import { screen, waitFor, within } from "@testing-library/react";
import userEvent, { type UserEvent } from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import databaseUnavailable from "@tests/fixtures/database-unavailable.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import taxYears from "@tests/fixtures/tax-years.json";
import { MESSAGES, renderInApp } from "@tests/render";

import { SettingsPage } from "@/features/settings";
import { LOCALES, type Locale } from "@/shared/constants/locales";

vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

// The API's own answers (tests/GestorIA.Api.Tests/WebFixtures.cs keeps them so): the tax years, and G12's taxpayer as the
// stored profile. What a save sends is compared with that profile, so the form writes what the API reads back.
const { id: G12_ID, ...G12_INPUT } = g12Profile;
const API = "http://localhost:5080/api/v1";

type Answer = { status: number; body: unknown } | "unreachable";

function stubApi({
  profiles = { status: 200, body: [] },
  save = { status: 201, body: g12Profile },
  remove = { status: 204, body: null },
}: { profiles?: Answer; save?: Answer; remove?: Answer } = {}) {
  let deleted = false;
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url, init) => {
    const method = init?.method ?? "GET";
    const answer = url.endsWith("/config/tax-years")
      ? { status: 200, body: taxYears }
      : method === "DELETE"
        ? remove
        : method === "GET"
          ? deleted
            ? { status: 200, body: [] }
            : profiles
          : save;
    if (answer === "unreachable") throw new TypeError("fetch failed");
    if (method === "DELETE" && answer.status === 204) deleted = true;
    if (answer.status === 204) return new Response(null, { status: 204 });
    const contentType = answer.status >= 400 ? "application/problem+json" : "application/json";
    return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { "Content-Type": contentType } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

function saves(fetchStub: ReturnType<typeof stubApi>) {
  return fetchStub.mock.calls
    .filter(([, init]) => init?.method === "POST" || init?.method === "PUT")
    .map(([url, init]) => ({ url, method: init?.method, body: JSON.parse(String(init?.body)) as unknown }));
}

const form = MESSAGES.en.Settings.profile;

async function renderSettings(locale: Locale = "en") {
  const user = userEvent.setup();
  renderInApp(<SettingsPage />, { locale });
  await screen.findByRole("button", { name: MESSAGES[locale].Settings.profile.submit });
  return user;
}

async function typeG12(user: UserEvent) {
  const field = (label: string) => screen.getByLabelText(label);
  await user.selectOptions(field(form.region), "VC");
  await user.type(field(form.employmentIngresos), "0.00");
  await user.type(field(form.employmentSeguridadSocial), "0.00");
  await user.type(field(form.alta), "2025-01-15");
  await user.click(screen.getByRole("radio", { name: form.first }));
  await user.type(field(form.ingresosFromFormerEmployer), "0.00");
  await user.type(field(form.projectionIngresos), "30000.00");
  await user.type(field(form.projectionGastos), "1200.00");
  await user.type(field(form.baseCotizacion), "1274.51");
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("SettingsPage", () => {
  it.each(LOCALES)("shows the preferences, the profile and the data section in %s", async (locale) => {
    stubApi();
    const messages = MESSAGES[locale].Settings;
    await renderSettings(locale);

    expect(screen.getByRole("heading", { level: 1, name: messages.title })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.profile.heading })).toBeInTheDocument();
    expect(screen.getByLabelText(messages.profile.taxYear)).toBeInTheDocument();
    expect(screen.getByLabelText(messages.profile.baseCotizacion)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.data.heading })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: messages.data.backup })).toHaveAttribute("href", "/backup");
  });

  it("lets the user change the theme and the language here as well as in the header", async () => {
    stubApi();
    await renderSettings();

    expect(screen.getByRole("combobox", { name: "Theme" })).toHaveValue("light");
    expect(screen.getByRole("combobox", { name: "Language" })).toHaveValue("en");
  });

  // 2026.json declares its tarifa plana and its calendar unpublished, so every 2026 estimate is refused today (#69).
  it("starts a new profile on the newest tax year that declares no gap, and shows a chosen year's gaps", async () => {
    stubApi();
    const user = await renderSettings();

    expect(screen.getByLabelText(form.taxYear)).toHaveValue("2025");
    expect(screen.queryByText(form.gaps.replace("{year}", "2026"))).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText(form.taxYear), "2026");

    const gaps = screen.getByText(form.gaps.replace("{year}", "2026")).parentElement!;
    for (const gap of taxYears[1]!.gaps) {
      expect(within(gaps).getByText(gap.entry)).toBeInTheDocument();
      expect(within(gaps).getByText(gap.note)).toBeInTheDocument();
    }
  });

  it("creates the profile with exactly what was typed, and says the overview estimates from it", async () => {
    const fetchStub = stubApi();
    const user = await renderSettings();

    await typeG12(user);
    await user.click(screen.getByRole("button", { name: form.submit }));

    expect(await screen.findByText(form.saved)).toBeInTheDocument();
    expect(saves(fetchStub)).toEqual([{ url: `${API}/profiles`, method: "POST", body: G12_INPUT }]);
  });

  it("fills the form from the stored profile and replaces it on save", async () => {
    const fetchStub = stubApi({ profiles: { status: 200, body: [g12Profile] }, save: { status: 200, body: g12Profile } });
    const user = await renderSettings();

    expect(screen.getByLabelText(form.region)).toHaveValue("VC");
    expect(screen.getByLabelText(form.alta)).toHaveValue("2025-01-15");
    expect(screen.getByRole("radio", { name: form.first })).toBeChecked();
    expect(screen.getByLabelText(form.baseCotizacion)).toHaveValue("1274.51");

    await user.clear(screen.getByLabelText(form.projectionIngresos));
    await user.type(screen.getByLabelText(form.projectionIngresos), "32000.00");
    await user.click(screen.getByRole("radio", { name: form.previousYearNet }));
    await user.type(screen.getByLabelText(form.previousYearNetAmount), "-500.25");
    await user.click(screen.getByRole("button", { name: form.submit }));

    await screen.findByText(form.saved);
    expect(saves(fetchStub)).toEqual([
      {
        url: `${API}/profiles/${G12_ID}`,
        method: "PUT",
        body: {
          ...G12_INPUT,
          activity: { ...G12_INPUT.activity, previousYear: { kind: "rendimientoNeto", rendimientoNeto: "-500.25" } },
          projection: { ...G12_INPUT.projection, ingresos: "32000.00" },
        },
      },
    ]);
  });

  it("refuses an incomplete or malformed profile next to each field and sends nothing", async () => {
    const fetchStub = stubApi();
    const user = await renderSettings();
    const errors = form.errors;

    await user.type(screen.getByLabelText(form.employmentIngresos), "1.234,56");
    await user.type(screen.getByLabelText(form.projectionGastos), "12.345");
    await user.click(screen.getByRole("button", { name: form.submit }));

    expect(screen.getByLabelText(form.employmentIngresos)).toHaveAccessibleDescription(errors.amount);
    expect(screen.getByLabelText(form.employmentIngresos)).toHaveFocus();
    expect(screen.getByLabelText(form.projectionGastos)).toHaveAccessibleDescription(errors.amount);
    expect(screen.getByLabelText(form.employmentSeguridadSocial)).toHaveAccessibleDescription(errors.required);
    expect(screen.getByLabelText(form.alta)).toHaveAccessibleDescription(errors.required);
    expect(saves(fetchStub)).toEqual([]);
  });

  it("shows each of the API's refusals next to its field", async () => {
    const base = '$.projection.baseCotizacion is "1274.51"; it must be a base of the 2026 tables, from 653.59 to 5101.20 (LGSS art. 308.1.a 3.ª).';
    const region = '$.region is "VC"; 2026 covers MD only.';
    stubApi({
      save: {
        status: 400,
        body: {
          type: "https://gestoria.local/problems/invalid-input",
          title: "The input is not valid",
          status: 400,
          detail: `${region} ${base}`,
          errors: { "$.region": [region], "$.projection.baseCotizacion": [base] },
        },
      },
    });
    const user = await renderSettings();

    await typeG12(user);
    await user.click(screen.getByRole("button", { name: form.submit }));

    await waitFor(() =>
      expect(screen.getByLabelText(form.baseCotizacion)).toHaveAccessibleDescription(form.errors.fromApi.replace("{reason}", base)),
    );
    expect(screen.getByLabelText(form.region)).toHaveAccessibleDescription(form.errors.fromApi.replace("{reason}", region));
    expect(screen.getByRole("alert")).toHaveTextContent(form.failure.invalid);
    expect(screen.queryByText(form.saved)).not.toBeInTheDocument();
  });

  it("keeps nothing typed in browser storage, cookies or the address, and logs nothing", async () => {
    stubApi();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const logged = (["log", "info", "warn", "error", "debug"] as const).map((method) => vi.spyOn(console, method));
    const user = await renderSettings();

    await typeG12(user);
    await user.click(screen.getByRole("button", { name: form.submit }));
    await screen.findByText(form.saved);

    expect(setItem).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).toBe("");
    expect(window.location.search).toBe("");
    for (const spy of logged) expect(spy).not.toHaveBeenCalled();
  });

  // An unreachable API is retried twice, a second and then two apart (data/query-provider.tsx), before the page says so.
  it("says the API is not answering when the profile cannot be loaded", { timeout: 10_000 }, async () => {
    stubApi({ profiles: "unreachable" });
    renderInApp(<SettingsPage />, { locale: "en" });

    expect(await screen.findByRole("alert", {}, { timeout: 8_000 })).toHaveTextContent(form.failure.network);
  });

  it("says the database is not answering, in the app's words, when the API cannot reach it", { timeout: 10_000 }, async () => {
    stubApi({ profiles: { status: 503, body: databaseUnavailable } });
    renderInApp(<SettingsPage />, { locale: "en" });

    const alert = await screen.findByRole("alert", {}, { timeout: 8_000 });
    expect(alert).toHaveTextContent(form.failure.database);
    expect(alert).not.toHaveTextContent(databaseUnavailable.detail);
  });

  describe("deleting all data", () => {
    const data = MESSAGES.en.Settings.data;

    it.each(LOCALES)("lists in %s what will be deleted and deletes only once the word is typed", async (locale) => {
      const fetchStub = stubApi({ profiles: { status: 200, body: [g12Profile] } });
      const messages = MESSAGES[locale].Settings.data;
      const user = await renderSettings(locale);
      const form = screen.getByRole("form", { name: messages.delete.heading });
      const submit = within(form).getByRole("button", { name: messages.delete.submit });
      const confirm = within(form).getByLabelText(messages.delete.confirm.replace("{word}", messages.delete.word));

      expect(within(form).getByRole("listitem")).toHaveTextContent(
        messages.delete.profile.replace("{year}", "2025").replace("{region}", "VC"),
      );
      expect(confirm).toHaveAccessibleDescription(messages.delete.what);
      expect(within(form).getByText(messages.delete.retention)).toBeInTheDocument();
      expect(submit).toBeDisabled();

      await user.type(confirm, messages.delete.word.toLowerCase());
      expect(submit).toBeDisabled();
      await user.keyboard("{Enter}");

      await user.clear(confirm);
      await user.type(confirm, messages.delete.word);
      expect(submit).toBeEnabled();
      expect(fetchStub.mock.calls.filter(([, init]) => init?.method === "DELETE")).toEqual([]);

      await user.click(submit);

      expect(await screen.findByText(messages.deleted)).toBeInTheDocument();
      expect(fetchStub.mock.calls.filter(([, init]) => init?.method === "DELETE").map(([url]) => url)).toEqual([`${API}/profiles/${G12_ID}`]);
    });

    it("starts the profile form over once everything is deleted", async () => {
      stubApi({ profiles: { status: 200, body: [g12Profile] } });
      const user = await renderSettings();
      expect(screen.getByLabelText(form.region)).toHaveValue("VC");

      await user.type(screen.getByLabelText(data.delete.confirm.replace("{word}", data.delete.word)), data.delete.word);
      await user.click(screen.getByRole("button", { name: data.delete.submit }));

      await screen.findByText(data.deleted);
      await waitFor(() => expect(screen.getByLabelText(form.alta)).toHaveValue(""));
      expect(screen.getByLabelText(form.baseCotizacion)).toHaveValue("");
    });

    it("offers no delete before anything is stored", async () => {
      stubApi();
      await renderSettings();

      expect(await screen.findByText(data.nothing)).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: data.delete.submit })).not.toBeInTheDocument();
    });

    it("says nothing was deleted when the API is not answering, and keeps the profile", async () => {
      stubApi({ profiles: { status: 200, body: [g12Profile] }, remove: "unreachable" });
      const user = await renderSettings();

      await user.type(screen.getByLabelText(data.delete.confirm.replace("{word}", data.delete.word)), data.delete.word);
      await user.click(screen.getByRole("button", { name: data.delete.submit }));

      expect(await screen.findByRole("alert")).toHaveTextContent(data.failure.network);
      expect(screen.getByLabelText(form.region)).toHaveValue("VC");
      expect(screen.queryByText(data.deleted)).not.toBeInTheDocument();
    });
  });
});

import { readFileSync } from "node:fs";
import path from "node:path";

import { screen, within } from "@testing-library/react";
import { MutationCache } from "@tanstack/react-query";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import databaseUnavailable from "@tests/fixtures/database-unavailable.json";
import g12Export from "@tests/fixtures/g12-export.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import g12RestoreConflict from "@tests/fixtures/g12-restore-conflict.json";
import g12Restore from "@tests/fixtures/g12-restore.json";
import { MESSAGES, renderInApp } from "@tests/render";

import { EXPORT_MAX_BYTES } from "@/data/profiles";
import { BackupPage } from "@/features/backup";
import { LOCALES, type Locale } from "@/shared/constants/locales";

// The stored profile and its export are the API's own answers (tests/GestorIA.Api.Tests/WebFixtures.cs keeps them so).
const API = "http://localhost:5080/api/v1";
const text = MESSAGES.en.Backup.export;
const restoreText = MESSAGES.en.Backup.restore;
// The file exactly as the API wrote it, so the body sent can be compared byte for byte.
const g12ExportFile = readFileSync(path.resolve(import.meta.dirname, "../../../../tests/fixtures/g12-export.json"), "utf8");

type Answer = { status: number; body: unknown } | "unreachable";

function stubApi({
  profiles = { status: 200, body: [g12Profile] },
  exported = { status: 200, body: g12Export },
  restored = { status: 201, body: g12Restore },
}: { profiles?: Answer; exported?: Answer; restored?: Answer } = {}) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    const answer = url.endsWith("/export") ? exported : url.endsWith("/restore") ? restored : profiles;
    if (answer === "unreachable") throw new TypeError("fetch failed");
    const contentType = answer.status >= 400 ? "application/problem+json" : "application/json";
    return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { "Content-Type": contentType } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

// jsdom has no object URLs and no downloads: the file handed to the browser is caught at the link's click.
function catchDownloads() {
  const files: { name: string; blob: Blob }[] = [];
  const blobs = new Map<string, Blob>();
  const revoked: string[] = [];
  vi.spyOn(URL, "createObjectURL").mockImplementation((blob) => {
    const url = `blob:test/${blobs.size}`;
    blobs.set(url, blob as Blob);
    return url;
  });
  vi.spyOn(URL, "revokeObjectURL").mockImplementation((url) => void revoked.push(url));
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    files.push({ name: this.download, blob: blobs.get(this.href)! });
  });
  return { files, revoked };
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("BackupPage", () => {
  it.each(LOCALES)("offers the download in %s, labelled as personal financial data, and a restore below it", async (locale) => {
    stubApi();
    const messages = MESSAGES[locale].Backup;
    renderInApp(<BackupPage />, { locale });

    expect(screen.getByRole("heading", { level: 1, name: messages.title })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.export.heading })).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent(messages.export.label);
    expect(await screen.findByRole("button", { name: messages.export.download })).toHaveTextContent(messages.export.label.toLowerCase());
    expect(screen.getByText(messages.export.format)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.restore.heading })).toBeInTheDocument();
    expect(screen.getByLabelText(messages.restore.file)).toHaveAttribute("accept", ".json,application/json");
  });

  it("downloads the API's export as it came, under a name that carries only the date", async () => {
    const fetchStub = stubApi();
    const downloads = catchDownloads();
    const user = userEvent.setup();
    renderInApp(<BackupPage />, { locale: "en" });

    await user.click(await screen.findByRole("button", { name: text.download }));

    expect(await screen.findByText(text.done)).toBeInTheDocument();
    expect(fetchStub).toHaveBeenCalledWith(`${API}/profiles/${g12Profile.id}/export`, expect.objectContaining({ cache: "no-store" }));
    expect(downloads.files.map((file) => file.name)).toEqual(["gestoria-export-2026-09-28.json"]);
    const file = downloads.files[0]!.blob;
    expect(file.type).toBe("application/json");
    expect(JSON.parse(await file.text())).toEqual(g12Export);
    await vi.waitFor(() => expect(downloads.revoked).toEqual(["blob:test/0"]));
  });

  // The file is dated by the day in Madrid, as the API names it: late evening in UTC is already tomorrow there.
  it("dates the file by the day in Madrid", async () => {
    stubApi({ exported: { status: 200, body: { ...g12Export, exportedAt: "2026-09-27T22:30:00+00:00" } } });
    const downloads = catchDownloads();
    const user = userEvent.setup();
    renderInApp(<BackupPage />, { locale: "en" });

    await user.click(await screen.findByRole("button", { name: text.download }));

    await screen.findByText(text.done);
    expect(downloads.files.map((file) => file.name)).toEqual(["gestoria-export-2026-09-28.json"]);
  });

  // The whole of the user's data must not linger in the mutation cache once the file is handed over.
  it("lets go of the export once it is downloaded", async () => {
    stubApi();
    catchDownloads();
    const removed = vi.spyOn(MutationCache.prototype, "remove");
    const user = userEvent.setup();
    renderInApp(<BackupPage />, { locale: "en" });

    await user.click(await screen.findByRole("button", { name: text.download }));

    await screen.findByText(text.done);
    await vi.waitFor(() => expect(removed).toHaveBeenCalled());
  });

  it("says there is nothing to download before a profile is stored, and points to settings", async () => {
    stubApi({ profiles: { status: 200, body: [] } });
    renderInApp(<BackupPage />, { locale: "en" });

    expect(await screen.findByText(text.nothing, { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: text.enter })).toHaveAttribute("href", "/settings");
    expect(screen.queryByRole("button", { name: text.download })).not.toBeInTheDocument();
  });

  it("says so when the profile is gone by the time of the download", async () => {
    stubApi({
      exported: {
        status: 404,
        body: { type: "https://gestoria.local/problems/profile-not-found", title: "No such profile", status: 404, detail: "There is no profile." },
      },
    });
    const downloads = catchDownloads();
    const user = userEvent.setup();
    renderInApp(<BackupPage />, { locale: "en" });

    await user.click(await screen.findByRole("button", { name: text.download }));

    expect(await screen.findByRole("alert")).toHaveTextContent(text.failure.gone);
    expect(downloads.files).toEqual([]);
  });

  it("says the database is not answering when the export cannot be read", async () => {
    stubApi({ exported: { status: 503, body: databaseUnavailable } });
    const downloads = catchDownloads();
    const user = userEvent.setup();
    renderInApp(<BackupPage />, { locale: "en" });

    await user.click(await screen.findByRole("button", { name: text.download }));

    expect(await screen.findByRole("alert")).toHaveTextContent(text.failure.database);
    expect(downloads.files).toEqual([]);
  });

  it("keeps the export out of browser storage, cookies, the address and the console", async () => {
    stubApi();
    catchDownloads();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const logged = (["log", "info", "warn", "error", "debug"] as const).map((method) => vi.spyOn(console, method));
    const user = userEvent.setup();
    renderInApp(<BackupPage />, { locale: "en" });

    await user.click(await screen.findByRole("button", { name: text.download }));
    await screen.findByText(text.done);

    expect(setItem).not.toHaveBeenCalled();
    expect(document.cookie).toBe("");
    expect(window.location.search).toBe("");
    for (const spy of logged) expect(spy).not.toHaveBeenCalled();
  });
});

describe("restoring an export", () => {
  const exportFile = (content = g12ExportFile) => new File([content], "gestoria-export-2026-09-28.json", { type: "application/json" });
  const restoreCalls = (fetchStub: ReturnType<typeof stubApi>) => fetchStub.mock.calls.filter(([url]) => url.endsWith("/restore"));
  const profileReads = (fetchStub: ReturnType<typeof stubApi>) => fetchStub.mock.calls.filter(([url]) => url === `${API}/profiles`);
  const translator = (locale: Locale) => createTranslator({ locale, messages: MESSAGES[locale], namespace: "Backup.restore" });

  async function choose(file: File, locale: Locale = "en") {
    const user = userEvent.setup();
    renderInApp(<BackupPage />, { locale });
    await user.upload(screen.getByLabelText(MESSAGES[locale].Backup.restore.file), file);
    return user;
  }

  it.each([
    ["uk", "2 січня 2025 р.", "31 грудня 2025 р.", "28 вересня 2026 р."],
    ["es", "2 de enero de 2025", "31 de diciembre de 2025", "28 de septiembre de 2026"],
    ["en", "January 2, 2025", "December 31, 2025", "September 28, 2026"],
    ["ru", "2 января 2025 г.", "31 декабря 2025 г.", "28 сентября 2026 г."],
  ] as const)("shows in %s what the file holds before anything is sent", async (locale, first, last, exported) => {
    const fetchStub = stubApi();
    const t = translator(locale);

    await choose(exportFile(), locale);

    const items = (await screen.findByText(t("holds"))).parentElement!;
    expect(within(items).getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      t("profile", { year: 2025, region: "VC" }),
      t("movements", { count: 18, first, last }),
    ]);
    expect(screen.getByText(t("exported", { date: exported }))).toBeInTheDocument();
    expect(screen.getByText(t("where"))).toBeInTheDocument();
    expect(screen.getByRole("button", { name: t("confirm") })).toBeEnabled();
    expect(restoreCalls(fetchStub)).toEqual([]);
  });

  it("sends the file as read, once, says what was stored and reads the profile again", async () => {
    const fetchStub = stubApi();
    const user = await choose(exportFile());
    const reads = profileReads(fetchStub).length;

    await user.click(await screen.findByRole("button", { name: restoreText.confirm }));

    expect(await screen.findByText(translator("en")("done", { year: 2025, count: 18 }), { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: restoreText.overview })).toHaveAttribute("href", "/");
    const calls = restoreCalls(fetchStub);
    expect(calls).toHaveLength(1);
    const [url, init] = calls[0]!;
    expect(url).toBe(`${API}/profiles/restore`);
    expect(init?.method).toBe("POST");
    expect(new Headers(init?.headers).get("Content-Type")).toBe("application/json");
    expect(init?.body).toBe(g12ExportFile);
    await vi.waitFor(() => expect(profileReads(fetchStub).length).toBeGreaterThan(reads));
    expect(screen.queryByRole("button", { name: restoreText.confirm })).not.toBeInTheDocument();
  });

  it("says the installation already holds data, what it holds, and points to settings", async () => {
    stubApi({ restored: { status: 409, body: g12RestoreConflict } });
    const user = await choose(exportFile());

    await user.click(await screen.findByRole("button", { name: restoreText.confirm }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(translator("en")("failure.notEmpty", { year: 2025, count: 18 }));
    expect(within(alert).getByRole("link", { name: restoreText.failure.settings })).toHaveAttribute("href", "/settings");
  });

  it("lists the API's reasons when it refuses the file, and says nothing was stored", async () => {
    stubApi({
      restored: {
        status: 400,
        body: {
          type: "https://gestoria.local/problems/invalid-input",
          title: "One or more validation errors occurred.",
          status: 400,
          errors: {
            "$.entities.bankTransactions[3].lineKey": ["The line key does not match the movement."],
            "$.entities.profiles[0].region": ["Unknown region."],
          },
        },
      },
    });
    const user = await choose(exportFile());

    await user.click(await screen.findByRole("button", { name: restoreText.confirm }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(restoreText.failure.refused);
    expect(within(alert).getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "The line key does not match the movement.",
      "Unknown region.",
    ]);
  });

  it.each([
    ["not JSON", "{ not json", restoreText.notJson],
    ["not an export", JSON.stringify({ format: "x" }), restoreText.notExport],
    ["a newer version", JSON.stringify({ ...g12Export, formatVersion: 2 }), translator("en")("unsupportedVersion", { version: "2" })],
  ])("refuses a file that is %s without sending it", async (_, content, message) => {
    const fetchStub = stubApi();

    await choose(exportFile(content));

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(screen.queryByRole("button", { name: restoreText.confirm })).not.toBeInTheDocument();
    expect(restoreCalls(fetchStub)).toEqual([]);
  });

  it("refuses a file over the limit without reading or sending it", async () => {
    const fetchStub = stubApi();
    const file = exportFile();
    Object.defineProperty(file, "size", { value: EXPORT_MAX_BYTES + 1 });
    const read = vi.spyOn(file, "text");

    await choose(file);

    expect(await screen.findByRole("alert")).toHaveTextContent(restoreText.failure.tooLarge);
    expect(read).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: restoreText.confirm })).not.toBeInTheDocument();
    expect(restoreCalls(fetchStub)).toEqual([]);
  });

  it("forgets the file on cancel and sends nothing", async () => {
    const fetchStub = stubApi();
    const user = await choose(exportFile());

    await user.click(await screen.findByRole("button", { name: restoreText.cancel }));

    expect(screen.queryByText(restoreText.holds)).not.toBeInTheDocument();
    expect((screen.getByLabelText(restoreText.file) as HTMLInputElement).files).toHaveLength(0);
    expect(restoreCalls(fetchStub)).toEqual([]);
  });

  it.each([
    ["the database is not answering", { status: 503, body: databaseUnavailable }, restoreText.failure.database],
    ["the API is not answering", "unreachable", restoreText.failure.network],
  ] as const)("says nothing was stored when %s", async (_, restored, message) => {
    stubApi({ restored });
    const user = await choose(exportFile());

    await user.click(await screen.findByRole("button", { name: restoreText.confirm }));

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
  });

  // The mutation's variables are the whole file: it must not linger in the mutation cache once the restore is done.
  it("lets go of the file once the restore is done", async () => {
    stubApi();
    const removed = vi.spyOn(MutationCache.prototype, "remove");
    const user = await choose(exportFile());

    await user.click(await screen.findByRole("button", { name: restoreText.confirm }));

    await screen.findByText(translator("en")("done", { year: 2025, count: 18 }), { exact: false });
    await vi.waitFor(() => expect(removed).toHaveBeenCalled());
  });

  it("keeps the file out of browser storage, cookies, the address and the console", async () => {
    stubApi();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const logged = (["log", "info", "warn", "error", "debug"] as const).map((method) => vi.spyOn(console, method));
    const user = await choose(exportFile());

    await user.click(await screen.findByRole("button", { name: restoreText.confirm }));
    await screen.findByText(translator("en")("done", { year: 2025, count: 18 }), { exact: false });

    expect(setItem).not.toHaveBeenCalled();
    expect(document.cookie).toBe("");
    expect(window.location.search).toBe("");
    for (const spy of logged) expect(spy).not.toHaveBeenCalled();
  });
});

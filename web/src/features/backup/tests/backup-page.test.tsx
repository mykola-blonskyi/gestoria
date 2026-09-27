import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import g12Export from "@tests/fixtures/g12-export.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import { MESSAGES, renderInApp } from "@tests/render";

import { BackupPage } from "@/features/backup";
import { LOCALES } from "@/shared/constants/locales";

// The stored profile and its export are the API's own answers (tests/GestorIA.Api.Tests/WebFixtures.cs keeps them so).
const API = "http://localhost:5080/api/v1";
const text = MESSAGES.en.Backup.export;

type Answer = { status: number; body: unknown } | "unreachable";

function stubApi({
  profiles = { status: 200, body: [g12Profile] },
  exported = { status: 200, body: g12Export },
}: { profiles?: Answer; exported?: Answer } = {}) {
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    const answer = url.endsWith("/export") ? exported : profiles;
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
  it.each(LOCALES)("offers the download in %s, labelled as personal financial data, and says restore is still to come", async (locale) => {
    stubApi();
    const messages = MESSAGES[locale].Backup;
    renderInApp(<BackupPage />, { locale });

    expect(screen.getByRole("heading", { level: 1, name: messages.title })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: messages.export.heading })).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent(messages.export.label);
    expect(await screen.findByRole("button", { name: messages.export.download })).toHaveTextContent(messages.export.label.toLowerCase());
    expect(screen.getByText(messages.points.restore)).toBeInTheDocument();
    expect(screen.getByText(messages.pending)).toBeInTheDocument();
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

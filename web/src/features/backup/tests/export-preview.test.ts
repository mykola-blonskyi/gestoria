import { describe, expect, it } from "vitest";

import g12Export from "@tests/fixtures/g12-export.json";

import { previewExport } from "../components/export-preview";

const withEntities = (entities: Record<string, unknown>) => JSON.stringify({ ...g12Export, entities });
const movement = (bookingDate: unknown) => ({ ...g12Export.entities.bankTransactions[0], bookingDate });

describe("previewExport", () => {
  it("describes the API's export", () => {
    expect(previewExport(JSON.stringify(g12Export))).toEqual({
      kind: "preview",
      profile: { taxYear: 2025, region: "VC" },
      bankTransactions: 18,
      firstBooking: "2025-01-02",
      lastBooking: "2025-12-31",
      exportedOn: "2026-09-28",
    });
  });

  // SPEC-009 §2.1: a file exported before #72 has no bankTransactions and is still a version 1 export.
  it("reads a missing kind as zero rows", () => {
    expect(previewExport(withEntities({ profiles: g12Export.entities.profiles }))).toMatchObject({
      kind: "preview",
      bankTransactions: 0,
      firstBooking: null,
      lastBooking: null,
    });
  });

  it("takes the range from the earliest and latest booking dates, whatever their order, and skips what is not a date", () => {
    const rows = [movement("2025-06-30"), movement("2025-02-31"), movement("2025-03-01"), movement(20250101), movement("2025-11-15")];
    expect(previewExport(withEntities({ profiles: g12Export.entities.profiles, bankTransactions: rows }))).toMatchObject({
      bankTransactions: 5,
      firstBooking: "2025-03-01",
      lastBooking: "2025-11-15",
    });
  });

  it("dates the export by its day in Madrid", () => {
    expect(previewExport(JSON.stringify({ ...g12Export, exportedAt: "2026-09-27T22:30:00+00:00" }))).toMatchObject({ exportedOn: "2026-09-28" });
    expect(previewExport(JSON.stringify({ ...g12Export, exportedAt: "yesterday" }))).toMatchObject({ exportedOn: null });
  });

  it.each([
    ["not JSON", "{", { kind: "not-json" }],
    ["JSON that is not an object", "[]", { kind: "not-export" }],
    ["another format", JSON.stringify({ format: "x" }), { kind: "not-export" }],
    ["a newer version", JSON.stringify({ ...g12Export, formatVersion: 2 }), { kind: "unsupported-version", version: "2" }],
    ["no profile", withEntities({ bankTransactions: [] }), { kind: "no-profile" }],
    ["two profiles", withEntities({ profiles: [...g12Export.entities.profiles, ...g12Export.entities.profiles] }), { kind: "no-profile" }],
  ])("refuses %s", (_, text, expected) => {
    expect(previewExport(text)).toEqual(expected);
  });
});

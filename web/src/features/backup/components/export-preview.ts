import { madridDay } from "@/shared/lib/format";

export type ExportPreview =
  | { kind: "not-json" }
  | { kind: "not-export" }
  | { kind: "unsupported-version"; version: string }
  | { kind: "no-profile" }
  | {
      kind: "preview";
      profile: { taxYear: number; region: string };
      bankTransactions: number;
      // The earliest and latest booking dates, yyyy-MM-dd; null when no movement carries one.
      firstBooking: string | null;
      lastBooking: string | null;
      // The day of the export in Madrid, as the file is named; null when the file does not say.
      exportedOn: string | null;
    };

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

// What an export file holds, read in the browser to show before anything is sent. Only enough to describe it: the API
// validates the whole file when the user confirms (SPEC-009 §2.2).
export function previewExport(text: string): ExportPreview {
  let document: unknown;
  try {
    document = JSON.parse(text);
  } catch {
    return { kind: "not-json" };
  }
  if (!isRecord(document) || document.format !== "gestoria.export") return { kind: "not-export" };
  if (document.formatVersion !== 1) return { kind: "unsupported-version", version: String(document.formatVersion) };

  const entities = isRecord(document.entities) ? document.entities : {};
  // A known kind missing from a version 1 file reads as zero rows (SPEC-009 §2.1).
  const profiles = Array.isArray(entities.profiles) ? entities.profiles : [];
  const bankTransactions = Array.isArray(entities.bankTransactions) ? entities.bankTransactions : [];
  const profile: unknown = profiles[0];
  // Without a profile there is nothing to restore into: every stored row belongs to one.
  if (profiles.length !== 1 || !isRecord(profile) || typeof profile.taxYear !== "number" || typeof profile.region !== "string") {
    return { kind: "no-profile" };
  }
  const bookingDates = bankTransactions
    .map((row: unknown) => (isRecord(row) ? row.bookingDate : undefined))
    .filter(isIsoDate)
    .sort();

  return {
    kind: "preview",
    profile: { taxYear: profile.taxYear, region: profile.region },
    bankTransactions: bankTransactions.length,
    firstBooking: bookingDates[0] ?? null,
    lastBooking: bookingDates.at(-1) ?? null,
    exportedOn: typeof document.exportedAt === "string" && !Number.isNaN(Date.parse(document.exportedAt)) ? madridDay(new Date(document.exportedAt)) : null,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

// A real calendar day, so formatting it cannot throw.
function isIsoDate(value: unknown): value is string {
  if (typeof value !== "string" || !ISO_DATE.test(value)) return false;
  const [year, month, day] = value.split("-").map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month - 1, day)).toISOString().slice(0, 10) === value;
}

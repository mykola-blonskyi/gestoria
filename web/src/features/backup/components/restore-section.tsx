"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { useId, useState, type ChangeEvent, type ReactNode } from "react";

import { ApiError, PROBLEM_TYPES, isDatabaseUnavailable, type ProblemDetails } from "@/shared/lib/api-error";
import { EXPORT_MAX_BYTES } from "@/data/profiles";
import type { Locale } from "@/shared/constants/locales";
import { formatDate } from "@/shared/lib/format";
import { Button } from "@/shared/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/card";

import { useRestoreProfile } from "../hooks/use-restore";
import { previewExport, type ExportPreview } from "./export-preview";

const OVERVIEW = "/";
const SETTINGS = "/settings";

// What the chosen file is, in this component's state only: never in storage, the address or the query cache (SPEC-013).
type Chosen = { kind: "too-large" } | { kind: "read"; text: string; preview: ExportPreview };

type Restored = { year: number; bankTransactions: number };

// Reads an export file in the browser, shows what it holds, and sends it only once the user confirms (#75).
export function RestoreSection() {
  const t = useTranslations("Backup.restore");
  const id = useId();
  const [chosen, setChosen] = useState<Chosen | null>(null);
  // A new key empties the file input, which cannot be cleared from React.
  const [inputKey, setInputKey] = useState(0);
  const [restored, setRestored] = useState<Restored | null>(null);
  const restore = useRestoreProfile();

  async function choose(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    restore.reset();
    setRestored(null);
    if (file === undefined) return setChosen(null);
    if (file.size > EXPORT_MAX_BYTES) return setChosen({ kind: "too-large" });
    const text = await file.text();
    setChosen({ kind: "read", text, preview: previewExport(text) });
  }

  function clear() {
    restore.reset();
    setChosen(null);
    setInputKey((key) => key + 1);
  }

  function confirm(text: string, year: number) {
    restore.mutate(text, {
      onSuccess: (answer) => {
        setRestored({ year, bankTransactions: answer.entities.bankTransactions });
        clear();
      },
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>{t("heading")}</h2>
        </CardTitle>
        <CardDescription>{t("lead")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        <div className="grid justify-items-start gap-1.5">
          <label htmlFor={`${id}-file`} className="text-sm font-medium">
            {t("file")}
          </label>
          <input
            key={inputKey}
            id={`${id}-file`}
            type="file"
            accept=".json,application/json"
            className="text-sm"
            disabled={restore.isPending}
            onChange={choose}
          />
        </div>
        {chosen?.kind === "too-large" && <Alert title={t("failure.tooLarge")} />}
        {chosen?.kind === "read" && chosen.preview.kind !== "preview" && <Unreadable preview={chosen.preview} />}
        {chosen?.kind === "read" && chosen.preview.kind === "preview" && (
          <Preview
            preview={chosen.preview}
            pending={restore.isPending}
            onConfirm={(year) => confirm(chosen.text, year)}
            onCancel={clear}
          />
        )}
        {restore.isError && <Failure error={restore.error} />}
        {restored !== null && (
          <p role="status" className="text-sm font-medium">
            {t("done", { year: restored.year, count: restored.bankTransactions })}{" "}
            <Link href={OVERVIEW} className="underline underline-offset-4">
              {t("overview")}
            </Link>
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function Unreadable({ preview }: { preview: Exclude<ExportPreview, { kind: "preview" }> }) {
  const t = useTranslations("Backup.restore");
  switch (preview.kind) {
    case "not-json":
      return <Alert title={t("notJson")} />;
    case "not-export":
      return <Alert title={t("notExport")} />;
    case "unsupported-version":
      return <Alert title={t("unsupportedVersion", { version: preview.version })} />;
    case "no-profile":
      return <Alert title={t("noProfile")} />;
  }
}

function Preview({
  preview,
  pending,
  onConfirm,
  onCancel,
}: {
  preview: Extract<ExportPreview, { kind: "preview" }>;
  pending: boolean;
  onConfirm: (year: number) => void;
  onCancel: () => void;
}) {
  const t = useTranslations("Backup.restore");
  const locale = useLocale() as Locale;
  const { profile, bankTransactions, firstBooking, lastBooking, exportedOn } = preview;

  return (
    <div className="grid gap-3 rounded-xl border border-border p-4">
      <div>
        <p className="text-sm">{t("holds")}</p>
        <ul className="mt-1 list-disc pl-5 text-sm">
          <li>{t("profile", { year: profile.taxYear, region: profile.region })}</li>
          <li>
            {bankTransactions === 0
              ? t("noMovements")
              : firstBooking !== null && lastBooking !== null
                ? t("movements", { count: bankTransactions, first: formatDate(firstBooking, locale), last: formatDate(lastBooking, locale) })
                : t("movementsUndated", { count: bankTransactions })}
          </li>
        </ul>
        {exportedOn !== null && <p className="mt-1 text-sm">{t("exported", { date: formatDate(exportedOn, locale) })}</p>}
      </div>
      <p className="text-sm">{t("where")}</p>
      <div className="flex flex-wrap gap-2">
        <Button disabled={pending} onClick={() => onConfirm(profile.taxYear)}>
          {pending ? t("restoring") : t("confirm")}
        </Button>
        <Button variant="outline" disabled={pending} onClick={onCancel}>
          {t("cancel")}
        </Button>
      </div>
    </div>
  );
}

// What went wrong, in words, each saying nothing was stored. Never rethrown or logged (SPEC-013).
function Failure({ error }: { error: Error }) {
  const t = useTranslations("Backup.restore.failure");
  const failure = error instanceof ApiError ? error.failure : null;

  if (failure?.kind === "network") return <Alert title={t("network")} />;
  if (isDatabaseUnavailable(error)) return <Alert title={t("database")} />;
  if (failure?.kind !== "problem") return <Alert title={t("other", { status: failure?.status ?? 0 })} />;

  const { problem } = failure;
  switch (problem.type) {
    case PROBLEM_TYPES.installationNotEmpty:
      return <NotEmpty problem={problem} />;
    case PROBLEM_TYPES.invalidInput:
      return <Alert title={t("refused")} reasons={reasonsOf(problem.extensions.errors)} />;
    case PROBLEM_TYPES.exportTooLarge:
      return <Alert title={t("tooLarge")} />;
    default:
      return <Alert title={t("other", { status: problem.status })} />;
  }
}

function NotEmpty({ problem }: { problem: ProblemDetails }) {
  const t = useTranslations("Backup.restore.failure");
  const { taxYear, entities } = problem.extensions;
  const movements = typeof entities === "object" && entities !== null ? (entities as Record<string, unknown>).bankTransactions : undefined;

  return (
    <Alert
      title={
        typeof taxYear === "number" && typeof movements === "number"
          ? t("notEmpty", { year: taxYear, count: movements })
          : t("notEmptyUnknown")
      }
    >
      <p className="mt-1 text-sm">
        {t("replace")}{" "}
        <Link href={SETTINGS} className="font-medium underline underline-offset-4">
          {t("settings")}
        </Link>
      </p>
    </Alert>
  );
}

function Alert({ title, reasons = [], children }: { title: string; reasons?: string[]; children?: ReactNode }) {
  return (
    <div role="alert" className="rounded-xl border border-destructive bg-background p-4">
      <p className="font-medium text-destructive">{title}</p>
      {reasons.length > 0 && (
        <ul className="mt-1 list-disc pl-5 text-sm">
          {reasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      )}
      {children}
    </div>
  );
}

// The API's reasons, keyed by the JSON path of each refused value; none quotes a movement's description (SPEC-009 §2.2).
function reasonsOf(errors: unknown): string[] {
  if (typeof errors !== "object" || errors === null) return [];
  return Object.values(errors).flatMap((reasons) => (Array.isArray(reasons) ? reasons.filter((r) => typeof r === "string") : []));
}

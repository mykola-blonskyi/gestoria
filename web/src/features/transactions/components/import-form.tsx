"use client";

import { useLocale, useTranslations } from "next-intl";
import { useId, useRef, useState, type ChangeEvent, type FormEvent } from "react";

import { BANKS, STATEMENT_MAX_BYTES, type Bank } from "@/data/transactions";
import type { Locale } from "@/shared/constants/locales";
import { formatDate } from "@/shared/lib/format";
import { Button } from "@/shared/ui/button";
import { Input } from "@/shared/ui/input";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";

import { useImportStatement } from "../hooks/use-transactions";
import { Alert, Failure } from "./failure";
import { statementPeriod } from "./statement-period";

const BANK_NAMES: Record<Bank, string> = { bbva: "BBVA" };

export function ImportForm({ profileId }: { profileId: string }) {
  const t = useTranslations("Transactions.import");
  const tFailure = useTranslations("Transactions.failure");
  const locale = useLocale() as Locale;
  const id = useId();
  const chosen = useRef<File | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [bank, setBank] = useState<Bank>("bbva");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [tooLarge, setTooLarge] = useState(false);
  const importing = useImportStatement(profileId);

  async function choose(event: ChangeEvent<HTMLInputElement>) {
    const next = event.target.files?.[0] ?? null;
    chosen.current = next;
    setFile(next);
    const period = next === null || next.size > STATEMENT_MAX_BYTES ? null : statementPeriod(await next.text());
    if (chosen.current !== next) return;
    setFrom(period?.from ?? "");
    setTo(period?.to ?? "");
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (file === null) return;
    importing.reset();
    setTooLarge(file.size > STATEMENT_MAX_BYTES);
    if (file.size <= STATEMENT_MAX_BYTES) importing.mutate({ profileId, bank, file, from, to });
  }

  return (
    <form onSubmit={submit} className="grid gap-3 rounded-xl border border-border p-4">
      <h2 className="font-medium">{t("heading")}</h2>
      <p className="text-sm text-muted-foreground">{t("lead")}</p>
      <div className="grid gap-3 sm:grid-cols-[1fr_auto_auto] sm:items-end">
        <div className="grid gap-1.5">
          <label htmlFor={`${id}-file`} className="text-sm font-medium">
            {t("file")}
          </label>
          <input
            id={`${id}-file`}
            type="file"
            accept=".csv,text/csv"
            className="text-sm"
            onChange={choose}
          />
        </div>
        <div className="grid gap-1.5">
          <label htmlFor={`${id}-bank`} className="text-sm font-medium">
            {t("bank")}
          </label>
          <NativeSelect id={`${id}-bank`} value={bank} onChange={(event) => setBank(event.target.value as Bank)}>
            {BANKS.map((option) => (
              <NativeSelectOption key={option} value={option}>
                {BANK_NAMES[option]}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <Button type="submit" disabled={file === null || importing.isPending}>
          {importing.isPending ? t("importing") : t("submit")}
        </Button>
      </div>
      <p className="text-sm text-muted-foreground">{t("periodHint")}</p>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="grid gap-1.5">
          <label htmlFor={`${id}-from`} className="text-sm font-medium">
            {t("from")}
          </label>
          <Input id={`${id}-from`} type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
        </div>
        <div className="grid gap-1.5">
          <label htmlFor={`${id}-to`} className="text-sm font-medium">
            {t("to")}
          </label>
          <Input id={`${id}-to`} type="date" value={to} onChange={(event) => setTo(event.target.value)} />
        </div>
      </div>
      {tooLarge && <Alert title={tFailure("tooLarge")} />}
      {importing.isError && <Failure error={importing.error} />}
      {importing.isSuccess && (
        <p role="status" className="text-sm font-medium">
          <span>
            {t("done", {
              lines: importing.data.lines,
              imported: importing.data.imported,
              already: importing.data.alreadyImported,
            })}
          </span>{" "}
          <span>
            {t("donePeriod", { from: formatDate(importing.data.from, locale), to: formatDate(importing.data.to, locale) })}
          </span>
        </p>
      )}
    </form>
  );
}

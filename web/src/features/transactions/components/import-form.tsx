"use client";

import { useTranslations } from "next-intl";
import { useId, useState, type FormEvent } from "react";

import { BANKS, STATEMENT_MAX_BYTES, type Bank } from "@/data/transactions";
import { Button } from "@/shared/ui/button";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";

import { useImportStatement } from "../hooks/use-transactions";
import { Alert, Failure } from "./failure";

const BANK_NAMES: Record<Bank, string> = { bbva: "BBVA" };

export function ImportForm({ profileId }: { profileId: string }) {
  const t = useTranslations("Transactions.import");
  const tFailure = useTranslations("Transactions.failure");
  const id = useId();
  const [file, setFile] = useState<File | null>(null);
  const [bank, setBank] = useState<Bank>("bbva");
  const [tooLarge, setTooLarge] = useState(false);
  const importing = useImportStatement(profileId);

  function submit(event: FormEvent) {
    event.preventDefault();
    if (file === null) return;
    importing.reset();
    setTooLarge(file.size > STATEMENT_MAX_BYTES);
    if (file.size <= STATEMENT_MAX_BYTES) importing.mutate({ profileId, bank, file });
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
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
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
      {tooLarge && <Alert title={tFailure("tooLarge")} />}
      {importing.isError && <Failure error={importing.error} />}
      {importing.isSuccess && (
        <p role="status" className="text-sm font-medium">
          {t("done", {
            lines: importing.data.lines,
            imported: importing.data.imported,
            already: importing.data.alreadyImported,
          })}
        </p>
      )}
    </form>
  );
}

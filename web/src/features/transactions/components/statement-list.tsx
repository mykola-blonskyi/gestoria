"use client";

import { useLocale, useTranslations } from "next-intl";
import { useId } from "react";

import type { Locale } from "@/shared/constants/locales";
import { formatDate } from "@/shared/lib/format";

import { useStatements } from "../hooks/use-transactions";
import { Failure } from "./failure";

export function StatementList({ profileId }: { profileId: string }) {
  const t = useTranslations("Transactions.statements");
  const locale = useLocale() as Locale;
  const id = useId();
  const statements = useStatements(profileId);

  return (
    <section className="grid gap-3" aria-labelledby={`${id}-heading`}>
      <h2 id={`${id}-heading`} className="font-medium">
        {t("heading")}
      </h2>
      {statements.isPending ? (
        <p role="status">{t("loading")}</p>
      ) : statements.isError ? (
        <Failure error={statements.error} />
      ) : statements.data.length === 0 ? (
        <p className="text-sm">{t("empty")}</p>
      ) : (
        <ul className="grid gap-1 text-sm">
          {statements.data.map((statement) => (
            <li key={statement.sequence}>
              {t("item", {
                sequence: statement.sequence,
                from: formatDate(statement.from, locale),
                to: formatDate(statement.to, locale),
              })}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

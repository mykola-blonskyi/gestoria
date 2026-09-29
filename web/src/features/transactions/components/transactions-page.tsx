"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useId, useState } from "react";

import type { Profile } from "@/data/profiles";
import type { Quarter } from "@/data/set-aside";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";
import { PageHeader } from "@/shared/ui/page-header";

import { useProfile } from "../hooks/use-profile";
import { useTransactions } from "../hooks/use-transactions";
import { Failure } from "./failure";
import { ImportForm } from "./import-form";
import { KINDS, kindOf, type Kind } from "./kind";
import { ReviewQueue } from "./review-queue";
import { StatementList } from "./statement-list";
import { TransactionList } from "./transaction-list";

const QUARTERS = ["Q1", "Q2", "Q3", "Q4"] as const satisfies readonly Quarter[];
const WHOLE_YEAR = "year";

export function TransactionsPage() {
  const t = useTranslations("Transactions");
  const profile = useProfile();

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="grid gap-6">
        {profile.isPending ? (
          <p role="status">{t("profile.loading")}</p>
        ) : profile.isError ? (
          <Failure error={profile.error} />
        ) : profile.data === null ? (
          <div className="grid gap-2 rounded-xl border border-border p-4">
            <h2 className="font-medium">{t("profile.missing")}</h2>
            <p className="text-sm">{t("profile.missingLead")}</p>
            <Link href="/settings" className="text-sm font-medium underline underline-offset-4">
              {t("profile.enter")}
            </Link>
          </div>
        ) : (
          <>
            <ImportForm profileId={profile.data.id} />
            <StatementList profileId={profile.data.id} />
            <ReviewQueue profileId={profile.data.id} />
            <Movements profile={profile.data} />
          </>
        )}
      </div>
    </>
  );
}

function Movements({ profile }: { profile: Profile }) {
  const t = useTranslations("Transactions");
  const id = useId();
  const [quarter, setQuarter] = useState<Quarter | null>(null);
  const [kind, setKind] = useState<Kind>("all");
  const transactions = useTransactions(profile.id, profile.taxYear, quarter);
  const shown = transactions.data?.filter((transaction) => kind === "all" || kindOf(transaction) === kind) ?? [];

  return (
    <section className="grid gap-3" aria-labelledby={`${id}-heading`}>
      <h2 id={`${id}-heading`} className="font-medium">
        {t("list.heading", { year: profile.taxYear })}
      </h2>
      <div className="flex flex-wrap gap-3">
        <div className="grid gap-1.5">
          <label htmlFor={`${id}-quarter`} className="text-sm font-medium">
            {t("filters.quarter")}
          </label>
          <NativeSelect
            id={`${id}-quarter`}
            value={quarter ?? WHOLE_YEAR}
            onChange={(event) => setQuarter(event.target.value === WHOLE_YEAR ? null : (event.target.value as Quarter))}
          >
            <NativeSelectOption value={WHOLE_YEAR}>{t("filters.wholeYear", { year: profile.taxYear })}</NativeSelectOption>
            {QUARTERS.map((option) => (
              <NativeSelectOption key={option} value={option}>
                {option}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <div className="grid gap-1.5">
          <label htmlFor={`${id}-kind`} className="text-sm font-medium">
            {t("filters.kind")}
          </label>
          <NativeSelect id={`${id}-kind`} value={kind} onChange={(event) => setKind(event.target.value as Kind)}>
            {KINDS.map((option) => (
              <NativeSelectOption key={option} value={option}>
                {t(`filters.kinds.${option}`)}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
      </div>
      {transactions.isPending ? (
        <p role="status">{t("list.loading")}</p>
      ) : transactions.isError ? (
        <Failure error={transactions.error} />
      ) : (
        <>
          <p className="text-sm text-muted-foreground">{t("list.count", { count: shown.length })}</p>
          {shown.length === 0 ? <p className="text-sm">{t("list.empty")}</p> : <TransactionList transactions={shown} />}
        </>
      )}
    </section>
  );
}

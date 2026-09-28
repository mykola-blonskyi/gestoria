"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { useState } from "react";

import { annualTrueUpQuery, quarterResultQuery, type AnnualTrueUp, type QuarterResult } from "@/data/periods";
import type { Profile } from "@/data/profiles";
import type { Quarter } from "@/data/set-aside";
import { taxYearsQuery } from "@/data/tax-years";
import { formatDate, formatMoney, formatMonth, formatShare } from "@/shared/lib/format";
import { QUARTERS, defaultQuarter } from "@/shared/lib/quarters";
import { ApiFailure } from "@/shared/ui/api-failure";
import { Button } from "@/shared/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { CasillaSheet } from "@/shared/ui/casilla-sheet";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";
import { Notices } from "@/shared/ui/notices-view";
import { PageHeader } from "@/shared/ui/page-header";
import { Trace } from "@/shared/ui/trace-view";

import { useProfile } from "../hooks/use-profile";

const SETTINGS = "/settings";
type Mode = "quarter" | "year";

export function PeriodsPage() {
  const t = useTranslations("Periods");
  const profile = useProfile();

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="grid gap-6">
        {profile.isPending ? (
          <p role="status">{t("profile.loading")}</p>
        ) : profile.isError ? (
          <ApiFailure error={profile.error} />
        ) : profile.data === null ? (
          <div className="grid gap-2 rounded-xl border border-border p-4">
            <h2 className="font-medium">{t("profile.missing")}</h2>
            <p className="text-sm">{t("profile.missingLead")}</p>
            <Link href={SETTINGS} className="text-sm font-medium underline underline-offset-4">
              {t("profile.enter")}
            </Link>
          </div>
        ) : (
          <Periods profile={profile.data} />
        )}
      </div>
    </>
  );
}

function Periods({ profile }: { profile: Profile }) {
  const t = useTranslations("Periods.toggle");
  const [mode, setMode] = useState<Mode>("quarter");
  const taxYears = useQuery(taxYearsQuery());
  const modelo130Lines = taxYears.data?.find((year) => year.taxYear === profile.taxYear)?.modelo130Lines ?? {};

  return (
    <>
      <div role="group" aria-label={t("label")} className="inline-flex gap-2">
        <Button variant={mode === "quarter" ? "primary" : "outline"} aria-pressed={mode === "quarter"} onClick={() => setMode("quarter")}>
          {t("quarter")}
        </Button>
        <Button variant={mode === "year" ? "primary" : "outline"} aria-pressed={mode === "year"} onClick={() => setMode("year")}>
          {t("year")}
        </Button>
      </div>
      {mode === "quarter" ? <QuarterMode profile={profile} modelo130Lines={modelo130Lines} /> : <YearMode profile={profile} />}
    </>
  );
}

function QuarterMode({ profile, modelo130Lines }: { profile: Profile; modelo130Lines: Record<string, string> }) {
  const t = useTranslations("Periods.quarter");
  const [quarter, setQuarter] = useState<Quarter>(() => defaultQuarter(profile.taxYear, profile.activity.alta, new Date()));
  const result = useQuery(quarterResultQuery(profile.id, quarter));

  return (
    <>
      <div className="grid gap-1.5">
        <label htmlFor="periods-quarter" className="text-sm font-medium">
          {t("asOf")}
        </label>
        <NativeSelect id="periods-quarter" value={quarter} onChange={(event) => setQuarter(event.target.value as Quarter)}>
          {QUARTERS.map((option) => (
            <NativeSelectOption key={option} value={option}>
              {option}
            </NativeSelectOption>
          ))}
        </NativeSelect>
      </div>
      {result.isPending ? (
        <p role="status">{t("calculating")}</p>
      ) : result.isError ? (
        <ApiFailure error={result.error} settings={SETTINGS} />
      ) : (
        <QuarterResultCard result={result.data} modelo130Lines={modelo130Lines} />
      )}
    </>
  );
}

function QuarterResultCard({ result, modelo130Lines }: { result: QuarterResult; modelo130Lines: Record<string, string> }) {
  const t = useTranslations("Periods.quarter");
  const locale = useLocale();

  return (
    <section aria-labelledby="quarter-heading" className="grid gap-6">
      <h2 id="quarter-heading" className="text-xl font-semibold">
        {t("heading", { quarter: result.quarter })}
      </h2>
      <Card>
        <CardContent className="grid gap-2">
          <dl>
            <Figure label={t("aIngresar")} value={formatMoney(result.aIngresar, locale)} />
          </dl>
          <p className="text-sm text-muted-foreground">
            {t(dueMessage(result), { from: formatDate(result.dueFrom, locale), by: formatDate(result.dueBy, locale) })} {t("localHolidays")}
          </p>
          <p className="text-sm text-muted-foreground">{t("basis")}</p>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>
            <h3>{t("casillasHeading")}</h3>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <CasillaSheet casillas={result.casillas} modelo130Lines={modelo130Lines} />
        </CardContent>
      </Card>
      <Trace steps={result.trace} locale={locale} />
    </section>
  );
}

// A return with nothing to pay is still filed in the same window. AEAT's instructions for casilla 19: a negative result in
// Q1–Q3 is filed "a deducir" and carried to the year's later quarters; a zero result, or a negative one in Q4, "negativa".
function dueMessage(result: QuarterResult) {
  if (!/^0+(\.0+)?$/.test(result.aIngresar)) return "due";
  const resultado = result.casillas.find((casilla) => casilla.key === "resultado")?.amount ?? "0.00";
  return resultado.startsWith("-") && result.quarter !== "Q4" ? "dueADeducir" : "dueNegativa";
}

function YearMode({ profile }: { profile: Profile }) {
  const t = useTranslations("Periods.year");
  const result = useQuery(annualTrueUpQuery(profile.id));

  return result.isPending ? (
    <p role="status">{t("calculating")}</p>
  ) : result.isError ? (
    <ApiFailure error={result.error} settings={SETTINGS} />
  ) : (
    <AnnualTrueUpCard result={result.data} />
  );
}

function AnnualTrueUpCard({ result }: { result: AnnualTrueUp }) {
  const t = useTranslations("Periods.year");
  const locale = useLocale();

  return (
    <section aria-labelledby="year-heading" className="grid gap-6">
      <div className="rounded-xl border-2 border-destructive bg-background p-4">
        <p className="font-medium">{t("banner")}</p>
      </div>
      <h2 id="year-heading" className="text-xl font-semibold">
        {t("heading")}
      </h2>
      <Card>
        <CardContent className="grid gap-4">
          <dl>
            <Figure label={t("gap")} value={formatMoney(result.gap, locale)}>
              {t("payableIn", { month: formatMonth(result.payableIn, locale) })}{" "}
              {t("due", { from: formatDate(result.dueFrom, locale), by: formatDate(result.dueBy, locale) })}
            </Figure>
          </dl>
          <dl className="grid gap-4 sm:grid-cols-3">
            <Figure label={t("liabilityOnActivity")} value={formatMoney(result.liabilityOnActivity, locale)} />
            <Figure label={t("marginalRate")} value={formatShare(result.marginalRate, locale)} />
            <Figure label={t("reduccionTrabajoLost")} value={formatMoney(result.reduccionTrabajoLost, locale)} />
          </dl>
        </CardContent>
      </Card>
      <Notices notices={result.notices} />
      <Trace steps={result.trace} locale={locale} />
    </section>
  );
}

function Figure({ label, value, children }: { label: string; value: string; children?: ReactNode }) {
  return (
    <div>
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold">{value}</dd>
      {children && <dd className="text-sm text-muted-foreground">{children}</dd>}
    </div>
  );
}

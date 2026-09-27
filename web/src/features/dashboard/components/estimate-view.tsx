"use client";

import { useLocale, useTranslations } from "next-intl";
import type { ReactNode } from "react";

import type { Notice, SetAsideEstimate, TraceSection, TraceStep } from "@/data/set-aside";
import type { Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney, formatMonth, formatShare } from "@/shared/lib/format";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";

// Every figure here is the engine's, formatted and never recomputed (SPEC-012 §2).
export function EstimateView({ estimate }: { estimate: SetAsideEstimate }) {
  const t = useTranslations("Dashboard.estimate");
  const locale = useLocale();
  const next = estimate.nextPayment;

  return (
    <section aria-labelledby="estimate-heading" className="grid gap-6">
      <h2 id="estimate-heading" className="text-xl font-semibold">
        {t("heading")}
      </h2>
      <Notices notices={estimate.notices} />
      <Card>
        <CardContent className="grid gap-4">
          <p className="text-lg font-medium">{t("holdBack", { share: formatShare(estimate.holdBackShare, locale) })}</p>
          <dl className="grid gap-4 sm:grid-cols-2">
            <Figure label={t("nextPayment", { quarter: next.quarter })} value={formatMoney(next.aIngresar, locale)}>
              {t("due", { from: formatDate(next.dueFrom, locale), by: formatDate(next.dueBy, locale) })} {t("localHolidays")}
            </Figure>
            <Figure label={t("cuota")} value={formatMoney(estimate.monthlyCuotaSs, locale)} />
            <Figure label={t("renta")} value={formatMoney(estimate.annualTrueUpGap, locale)}>
              {t("payableIn", { month: formatMonth(estimate.annualTrueUpPayableIn, locale) })}
            </Figure>
            <Figure label={t("iva")} value={formatMoney(estimate.ivaToSetAside, locale)} />
          </dl>
          <p className="text-sm break-all text-muted-foreground">{t("config", { year: estimate.taxYear, hash: estimate.configHash })}</p>
        </CardContent>
      </Card>
      <Trace steps={estimate.trace} locale={locale} />
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

const SEVERITY_RANK = { Error: 2, Warning: 1, Info: 0 } as const;

// Warnings first, as the console prints them; Array.prototype.sort is stable, so the engine's order holds within a severity.
function Notices({ notices }: { notices: readonly Notice[] }) {
  const t = useTranslations("Dashboard.notices");
  const ordered = [...notices].sort((a, b) => SEVERITY_RANK[b.severity] - SEVERITY_RANK[a.severity]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h3>{t("heading")}</h3>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <ul className="grid gap-3">
          {ordered.map((notice, index) => (
            <li key={index} className={notice.severity === "Info" ? "" : "border-s-4 border-destructive ps-3"}>
              <p className="text-sm font-medium">
                {t(notice.severity)} · <code>{notice.code}</code>
              </p>
              <p lang="en" className="text-sm">
                {notice.text}
              </p>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}

function Trace({ steps, locale }: { steps: readonly TraceStep[]; locale: Locale }) {
  const t = useTranslations("Dashboard.trace");
  const sections = Map.groupBy(steps, (step) => step.section);

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h3>{t("heading")}</h3>
        </CardTitle>
        <p className="text-sm text-muted-foreground">{t("lead", { count: steps.length })}</p>
      </CardHeader>
      <CardContent className="grid gap-2">
        {[...sections].map(([section, inSection]) => (
          <details key={section} className="rounded-lg border border-border p-3">
            <summary className="cursor-pointer font-medium">
              {t("section", { name: t(`sections.${section satisfies TraceSection}`), count: inSection.length })}
            </summary>
            <ol className="mt-3 grid gap-4">
              {inSection.map((step) => (
                <li key={step.id} className="grid gap-1 text-sm">
                  <p className="font-medium">
                    <span lang="es">{step.title}</span> <code className="text-muted-foreground">{step.id}</code>
                  </p>
                  {step.inputs.length > 0 && (
                    <p>
                      {t("inputs")}: {step.inputs.map((input) => `${input.name} = ${input.value}`).join(", ")}
                    </p>
                  )}
                  <p>
                    {t("formula")}: <code className="break-words">{step.formula}</code>
                  </p>
                  <p className="font-medium">
                    {t("result")}: {display(step.output, locale)}
                  </p>
                  <p lang="en" className="text-muted-foreground">
                    {t("source")}: {step.reference}
                  </p>
                </li>
              ))}
            </ol>
          </details>
        ))}
      </CardContent>
    </Card>
  );
}

function display(output: TraceStep["output"], locale: Locale): string {
  switch (output.kind) {
    case "money":
      return formatMoney(output.value, locale);
    case "rate":
      return formatShare(output.value, locale);
    case "date":
      return formatDate(output.value, locale);
    case "count":
      return output.value;
  }
}

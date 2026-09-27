"use client";

import { useLocale, useTranslations } from "next-intl";
import type { ReactNode } from "react";

import type { SetAsideEstimate } from "@/data/set-aside";
import { formatDate, formatMoney, formatMonth, formatShare } from "@/shared/lib/format";
import { Card, CardContent } from "@/shared/ui/card";
import { Notices } from "@/shared/ui/notices-view";
import { Trace } from "@/shared/ui/trace-view";

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

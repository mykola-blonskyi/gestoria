"use client";

import { useLocale, useTranslations } from "next-intl";

import type { Obligation } from "@/data/payments";
import { formatDate, formatMoney } from "@/shared/lib/format";

import { usePaymentsCalendar } from "../hooks/use-payments-calendar";
import { ApiFailure } from "./api-failure";
import { ExportCalendar } from "./export-calendar";
import { formatPeriod } from "./format-period";

const SETTINGS = "/settings";

export function CalendarView({ profileId }: { profileId: string }) {
  const t = useTranslations("Payments");
  const locale = useLocale();
  const calendar = usePaymentsCalendar(profileId);

  if (calendar.isPending) {
    return <p role="status">{t("loading")}</p>;
  }

  if (calendar.isError) {
    return <ApiFailure error={calendar.error} settings={SETTINGS} />;
  }

  return (
    <div className="grid gap-6">
      <ObligationsTable obligations={calendar.data.obligations} />
      <div className="grid gap-1">
        <p className="text-sm text-muted-foreground">{t("notes.modelo349Monthly", { cap: formatMoney(calendar.data.modelo349QuarterlyFilingCap, locale) })}</p>
        <p className="text-sm text-muted-foreground">{t("localHolidays.filing")}</p>
        <p className="text-sm text-muted-foreground">{t("localHolidays.cuota")}</p>
      </div>
      <p className="text-sm break-all text-muted-foreground">{t("config", { year: calendar.data.taxYear, hash: calendar.data.configHash })}</p>
      <ExportCalendar profileId={profileId} />
    </div>
  );
}

function ObligationsTable({ obligations }: { obligations: readonly Obligation[] }) {
  const t = useTranslations("Payments");
  const locale = useLocale();

  return (
    <table className="w-full border-collapse text-sm">
      <caption className="sr-only">{t("title")}</caption>
      <thead>
        <tr className="border-b border-border text-left text-muted-foreground">
          <th scope="col" className="py-2 pr-3 font-medium">
            {t("table.obligation")}
          </th>
          <th scope="col" className="py-2 pr-3 font-medium">
            {t("table.due")}
          </th>
          <th scope="col" className="py-2 text-right font-medium">
            {t("table.amount")}
          </th>
        </tr>
      </thead>
      <tbody>
        {obligations.map((obligation, index) => (
          <tr key={`${obligation.kind}-${obligation.period}-${index}`} className="border-b border-border last:border-0">
            <td className="py-2 pr-3">
              {t(`kinds.${obligation.kind}`)} · {formatPeriod(obligation.kind, obligation.period, locale, t)}
              {obligation.kind === "Modelo349" && <span className="block text-xs text-muted-foreground">{t("notes.modelo349")}</span>}
            </td>
            <td className="py-2 pr-3">{t("dueWindow", { from: formatDate(obligation.dueFrom, locale), by: formatDate(obligation.dueBy, locale) })}</td>
            <td className="py-2 text-right">
              {obligation.amount.kind === "known" ? formatMoney(obligation.amount.euros, locale) : t("amount.notYetKnown")}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

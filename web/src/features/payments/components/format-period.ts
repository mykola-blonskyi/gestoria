import type { useTranslations } from "next-intl";

import type { ObligationKind } from "@/data/payments";
import type { Locale } from "@/shared/constants/locales";
import { formatMonth } from "@/shared/lib/format";

// Period is the engine's own label for the obligation (GestorIA.Engine.PaymentObligation): "Q1".."Q4" for a quarterly
// filing, a "yyyy-MM" YearMonth for a TGSS cuota, the tax year itself for the Renta true-up. Each reads differently.
export function formatPeriod(kind: ObligationKind, period: string, locale: Locale, t: ReturnType<typeof useTranslations>): string {
  switch (kind) {
    case "SeguridadSocial":
      return formatMonth(period, locale);
    case "RentaTrueUp":
      return t("period.taxYear", { year: period });
    default:
      return period;
  }
}

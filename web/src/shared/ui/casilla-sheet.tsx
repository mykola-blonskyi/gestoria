"use client";

import { useLocale, useTranslations } from "next-intl";

import type { Locale } from "@/shared/constants/locales";
import { formatMoney } from "@/shared/lib/format";

// The shape of a CasillaView (GestorIA.Api), kept structural here so this shared component never imports the data layer
// (web/README.md's layer rules): a feature's own CasillaView value satisfies this without conversion.
export type Casilla = {
  key: string;
  amount: string;
};

// A Modelo 130 casilla, in the config's own key order (never re-sorted by casilla number): "Casilla {n}" from
// modelo130Lines, a translated human label for what it means, and the amount. modelo130Lines and the casillas array
// are independently versioned, so a key with no line number still shows its amount rather than crashing.
export function CasillaSheet({ casillas, modelo130Lines }: { casillas: readonly Casilla[]; modelo130Lines: Record<string, string> }) {
  const t = useTranslations("Periods.casillas");
  const locale: Locale = useLocale();

  return (
    <dl className="grid gap-3 sm:grid-cols-2">
      {casillas.map((casilla) => {
        const line = modelo130Lines[casilla.key];
        return (
          <div key={casilla.key}>
            <dt className="text-sm text-muted-foreground">
              {line !== undefined && <>{t("line", { number: line })} · </>}
              {/* casilla.key is an API-supplied string, not a literal known at compile time; the translation catalogue
                  is the source of truth for the finite set that actually exists (messages.test.ts key-parity check). */}
              {t(casilla.key as never)}
            </dt>
            <dd className="text-base font-medium">{formatMoney(casilla.amount, locale)}</dd>
          </div>
        );
      })}
    </dl>
  );
}

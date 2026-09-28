import Link from "next/link";
import { useTranslations } from "next-intl";

import type { QuarterResult } from "@/data/periods";

const TRANSACTIONS = "/transactions";

// What the period's figures rest on, from the answer's own ledger counts (#73): the projection, or the actuals of the closed,
// reviewed quarters, and what the classified movements leave out.
export function LedgerBasis({ ledger }: { ledger: QuarterResult["ledger"] }) {
  const t = useTranslations("Periods.basis");

  return (
    <div className="grid gap-1 text-sm text-muted-foreground">
      <p>
        {ledger.actualsThrough === null
          ? t("projection")
          : t("actuals", { quarter: ledger.actualsThrough, counted: ledger.counted })}
      </p>
      {ledger.awaitingReview > 0 && (
        <p>
          {t("awaitingReview", { count: ledger.awaitingReview })}{" "}
          <Link href={TRANSACTIONS} className="font-medium underline underline-offset-4">
            {t("review")}
          </Link>
        </p>
      )}
      {ledger.awaitingInvoice > 0 && <p>{t("awaitingInvoice", { count: ledger.awaitingInvoice })}</p>}
    </div>
  );
}

"use client";

import { useLocale, useTranslations } from "next-intl";

import type { Transaction } from "@/data/transactions";
import type { Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney } from "@/shared/lib/format";
import { VirtualList } from "@/shared/ui/virtual-list";

import { kindOf } from "./kind";

const ROW_HEIGHT = 56;

export function TransactionList({ transactions }: { transactions: readonly Transaction[] }) {
  const t = useTranslations("Transactions");
  const locale = useLocale() as Locale;

  return (
    <VirtualList
      items={transactions}
      label={t("list.label")}
      rowHeight={ROW_HEIGHT}
      getKey={(transaction) => transaction.id}
      renderRow={(transaction) => {
        const kind = kindOf(transaction);
        return (
          <div className="grid h-full grid-cols-[7rem_1fr_auto] items-center gap-3 border-b border-border px-3 text-sm sm:grid-cols-[10rem_1fr_auto]">
            <span className="text-muted-foreground">{formatDate(transaction.bookingDate, locale)}</span>
            <span className="truncate" title={transaction.description}>
              {transaction.description}
            </span>
            <span className="text-right">
              <span className="block font-medium tabular-nums">{formatMoney(transaction.amount, locale)}</span>
              <span className="block text-xs text-muted-foreground">{t(`kind.${kind}`)}</span>
            </span>
          </div>
        );
      }}
    />
  );
}

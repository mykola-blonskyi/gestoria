import type { Locale } from "@/shared/constants/locales";

const MONEY_PATTERN = /^-?\d+\.\d{2}$/;
const DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

function isMoneyString(amount: string): amount is `${number}` {
  return MONEY_PATTERN.test(amount);
}

// Intl.NumberFormat reads a numeric string as an exact decimal (ECMA-402, NumberFormat v3),
// so the amount never passes through a binary float on its way to the screen.
// Errors never echo the value: the console must not show financial data (SPEC-013).
export function formatMoney(amount: string, locale: Locale): string {
  if (!isMoneyString(amount)) {
    throw new RangeError("Not a money string with two decimals");
  }
  return new Intl.NumberFormat(locale, {
    style: "currency",
    currency: "EUR",
    currencyDisplay: "narrowSymbol",
    signDisplay: "negative",
  }).format(amount);
}

export function formatDate(isoDate: string, locale: Locale): string {
  const match = DATE_PATTERN.exec(isoDate);
  const date = match ? new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))) : null;
  if (date === null || date.toISOString().slice(0, 10) !== isoDate) {
    throw new RangeError("Not an ISO-8601 calendar date");
  }
  return new Intl.DateTimeFormat(locale, { dateStyle: "long", timeZone: "UTC" }).format(date);
}

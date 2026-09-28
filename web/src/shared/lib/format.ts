import type { Locale } from "@/shared/constants/locales";

const MONEY_PATTERN = /^-?\d+\.\d{2}$/;
const DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
const SHARE_PATTERN = /^\d+(\.\d+)?$/;
const MONTH_PATTERN = /^(\d{4})-(\d{2})$/;

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

// A rate arrives as its exact decimal fraction ("0.1947" is 19.47 %). Like formatMoney, the
// string goes to Intl as a decimal and never through a binary float.
export function formatShare(fraction: string, locale: Locale): string {
  if (!SHARE_PATTERN.test(fraction)) {
    throw new RangeError("Not a decimal fraction");
  }
  return new Intl.NumberFormat(locale, {
    style: "percent",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(fraction as `${number}`);
}

export function formatMonth(yearMonth: string, locale: Locale): string {
  const match = MONTH_PATTERN.exec(yearMonth);
  const month = match ? Number(match[2]) : 0;
  if (match === null || month < 1 || month > 12) {
    throw new RangeError("Not an ISO-8601 year and month");
  }
  return new Intl.DateTimeFormat(locale, { year: "numeric", month: "long", timeZone: "UTC" }).format(
    new Date(Date.UTC(Number(match[1]), month - 1, 1)),
  );
}

// The day an instant falls on where the user lives, as yyyy-MM-dd: every region GestorIA covers is on Madrid time (ADR-0016),
// the same day the API calls today. The en-CA locale writes a date as yyyy-MM-dd.
const MADRID_DAY = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Madrid", year: "numeric", month: "2-digit", day: "2-digit" });

export function madridDay(instant: Date): string {
  return MADRID_DAY.format(instant);
}

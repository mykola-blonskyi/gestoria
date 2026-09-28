import { describe, expect, it } from "vitest";

import { formatDate, formatMoney, formatMonth, formatShare, isZeroMoney } from "@/shared/lib/format";

const NBSP = " ";

describe("formatMoney", () => {
  it.each([
    ["uk", "1234.56", `1${NBSP}234,56${NBSP}€`],
    ["es", "1234.56", `1234,56${NBSP}€`],
    ["es", "12345.67", `12.345,67${NBSP}€`],
    ["en", "1234.56", "€1,234.56"],
    ["ru", "1234.56", `1${NBSP}234,56${NBSP}€`],
  ] as const)("formats %s %s as %s", (locale, amount, expected) => {
    expect(formatMoney(amount, locale)).toBe(expected);
  });

  it.each([
    ["uk", `-1${NBSP}234,56${NBSP}€`, `0,00${NBSP}€`],
    ["es", `-1234,56${NBSP}€`, `0,00${NBSP}€`],
    ["en", "-€1,234.56", "€0.00"],
    ["ru", `-1${NBSP}234,56${NBSP}€`, `0,00${NBSP}€`],
  ] as const)("formats negatives and zero in %s", (locale, negative, zero) => {
    expect(formatMoney("-1234.56", locale)).toBe(negative);
    expect(formatMoney("0.00", locale)).toBe(zero);
  });

  // 12345678901234567.89 has no exact binary double: Number() turns it into 12345678901234568.
  it.each([
    ["uk", `12${NBSP}345${NBSP}678${NBSP}901${NBSP}234${NBSP}567,89${NBSP}€`],
    ["es", `12.345.678.901.234.567,89${NBSP}€`],
    ["en", "€12,345,678,901,234,567.89"],
    ["ru", `12${NBSP}345${NBSP}678${NBSP}901${NBSP}234${NBSP}567,89${NBSP}€`],
  ] as const)("keeps every digit of a value beyond float precision in %s", (locale, expected) => {
    expect(formatMoney("12345678901234567.89", locale)).toBe(expected);
  });

  it("shows a negative zero without its sign", () => {
    expect(formatMoney("-0.00", "uk")).toBe(`0,00${NBSP}€`);
  });

  it("shows the cents it was given without rounding", () => {
    expect(formatMoney("0.10", "en")).toBe("€0.10");
    expect(formatMoney("0.01", "en")).toBe("€0.01");
  });

  it.each(["1234.5", "1234", "1,234.56", "1234.567", "abc", "", "1e3", " 1.00", "+1.00", "NaN"])(
    "rejects %j, which is not a SPEC-009 money string, without echoing it",
    (amount) => {
      expect(() => formatMoney(amount, "uk")).toThrowError(
        new RangeError("Not a money string with two decimals"),
      );
    },
  );
});

describe("isZeroMoney", () => {
  it.each(["0.00", "-0.00", "0", "000.0"])("reads %s as zero", (amount) => {
    expect(isZeroMoney(amount)).toBe(true);
  });

  it.each(["0.01", "-0.01", "100.00", "10.00", "", "abc"])("reads %s as not zero", (amount) => {
    expect(isZeroMoney(amount)).toBe(false);
  });
});

describe("formatDate", () => {
  it.each([
    ["uk", "20 квітня 2027 р."],
    ["es", "20 de abril de 2027"],
    ["en", "April 20, 2027"],
    ["ru", "20 апреля 2027 г."],
  ] as const)("formats an ISO date in %s", (locale, expected) => {
    expect(formatDate("2027-04-20", locale)).toBe(expected);
  });

  it("keeps the calendar day whatever the machine's time zone", () => {
    expect(formatDate("2027-01-01", "en")).toBe("January 1, 2027");
  });

  it.each(["2027-02-30", "2027-4-20", "20-04-2027", "2027-04-20T10:00:00Z", ""])("rejects %j", (isoDate) => {
    expect(() => formatDate(isoDate, "uk")).toThrowError(new RangeError("Not an ISO-8601 calendar date"));
  });
});

describe("formatShare", () => {
  it.each([
    ["uk", "19,47%"],
    ["es", `19,47${NBSP}%`],
    ["en", "19.47%"],
    ["ru", `19,47${NBSP}%`],
  ] as const)("formats a fraction as a percentage with two decimals in %s", (locale, expected) => {
    expect(formatShare("0.1947", locale)).toBe(expected);
  });

  // 0.1 + 0.2 in binary floating point is 0.30000000000000004.
  it("scales the decimal string, not a float", () => {
    expect(formatShare("0.3", "en")).toBe("30.00%");
    expect(formatShare("1", "en")).toBe("100.00%");
  });

  it.each(["", "19.47%", "-0.1", "1e-2", "abc"])("rejects %j without echoing it", (fraction) => {
    expect(() => formatShare(fraction, "uk")).toThrowError(new RangeError("Not a decimal fraction"));
  });
});

describe("formatMonth", () => {
  it.each([
    ["uk", "червень 2026 р."],
    ["es", "junio de 2026"],
    ["en", "June 2026"],
    ["ru", "июнь 2026 г."],
  ] as const)("formats a year and month in %s", (locale, expected) => {
    expect(formatMonth("2026-06", locale)).toBe(expected);
  });

  it.each(["2026-13", "2026-6", "2026-06-01", ""])("rejects %j", (yearMonth) => {
    expect(() => formatMonth(yearMonth, "uk")).toThrowError(new RangeError("Not an ISO-8601 year and month"));
  });
});

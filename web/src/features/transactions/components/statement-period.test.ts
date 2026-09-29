import { describe, expect, it } from "vitest";

import { statementPeriod } from "./statement-period";

const HEADER = "Fecha;Fecha Valor;Concepto;Importe;Saldo";
const line = (date: string) => `${date};${date};X;-1,00;`;

describe("statementPeriod", () => {
  it("skips the header and reads the first and last date", () => {
    expect(statementPeriod([HEADER, line("02/01/2025"), line("31/12/2025")].join("\n"))).toEqual({ from: "2025-01-02", to: "2025-12-31" });
  });

  it("reads CRLF and blank lines", () => {
    expect(statementPeriod(`\r\n${HEADER}\r\n${line("05/02/2025")}\r\n\r\n${line("06/02/2025")}\r\n`)).toEqual({
      from: "2025-02-05",
      to: "2025-02-06",
    });
  });

  it("ignores lines it cannot read", () => {
    expect(statementPeriod([HEADER, "garbage", line("2025-01-02"), line("1/2/2025"), line("10/03/2025")].join("\n"))).toEqual({
      from: "2025-03-10",
      to: "2025-03-10",
    });
  });

  it("takes the smallest and the largest date, not the first and the last line", () => {
    expect(statementPeriod([HEADER, line("15/03/2025"), line("02/01/2025"), line("30/06/2024"), line("01/12/2025"), line("20/05/2025")].join("\n"))).toEqual({
      from: "2024-06-30",
      to: "2025-12-01",
    });
  });

  it("answers null when no date was read", () => {
    expect(statementPeriod("")).toBeNull();
    expect(statementPeriod(HEADER)).toBeNull();
    expect(statementPeriod(`${HEADER}\nnope;x`)).toBeNull();
  });

  it("treats the first non-blank line as the header even when it is a date line", () => {
    expect(statementPeriod([line("01/01/2025"), line("02/01/2025")].join("\n"))).toEqual({ from: "2025-01-02", to: "2025-01-02" });
  });
});

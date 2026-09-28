// The API's Quarter enum ("Q1"–"Q4"), kept as a local literal union — not imported from data/set-aside — so shared never
// imports the data layer (web/README.md's layer rules). data/set-aside.ts's own Quarter alias is the same literal set.
export type Quarter = "Q1" | "Q2" | "Q3" | "Q4";

export const QUARTERS = ["Q1", "Q2", "Q3", "Q4"] as const satisfies readonly Quarter[];

// The quarter a period picker opens on: today's, in the profile's own tax year; Q4 for a year that is over and Q1 for one
// to come. Never before the quarter of the alta, which the engine would refuse. A calendar lookup, not tax arithmetic.
export function defaultQuarter(taxYear: number, alta: string, today: Date): Quarter {
  const quarterOf = (month: number) => Math.floor((month - 1) / 3) + 1;
  const year = today.getFullYear();
  const current = year === taxYear ? quarterOf(today.getMonth() + 1) : year > taxYear ? 4 : 1;
  const altaYear = Number(alta.slice(0, 4));
  const first = altaYear === taxYear ? quarterOf(Number(alta.slice(5, 7))) : altaYear < taxYear ? 1 : 4;
  return QUARTERS[Math.max(current, first) - 1] ?? "Q4";
}

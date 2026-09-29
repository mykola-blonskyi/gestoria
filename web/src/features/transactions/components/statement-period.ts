const BBVA_DATE = /^(\d{2})\/(\d{2})\/(\d{4})$/;

// The first and last booking date of a BBVA CSV, read the way the API does: the header line is skipped and the date
// leads each line. A line that cannot be read is left to the API to report.
export function statementPeriod(text: string): { from: string; to: string } | null {
  let from: string | null = null;
  let to: string | null = null;
  const lines = text.split(/\r?\n/).filter((line) => line.trim() !== "");
  for (const line of lines.slice(1)) {
    const match = BBVA_DATE.exec(line.split(";", 1)[0]!.trim());
    if (match === null) continue;
    const date = `${match[3]}-${match[2]}-${match[1]}`;
    if (from === null || date < from) from = date;
    if (to === null || date > to) to = date;
  }
  return from === null || to === null ? null : { from, to };
}

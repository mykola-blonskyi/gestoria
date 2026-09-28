import type { ProfileExport } from "@/data/profiles";

// The same name the API gives the file (Content-Disposition): what it is and the day it was made in Madrid time, nothing about
// whose it is. The en-CA locale writes a date as yyyy-MM-dd.
const MADRID_DAY = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Madrid", year: "numeric", month: "2-digit", day: "2-digit" });

export function exportFileName(data: ProfileExport): string {
  return `gestoria-export-${MADRID_DAY.format(new Date(data.exportedAt))}.json`;
}

// Hands the export to the browser as a download. The API needs the key in a header, so a plain link cannot fetch it: the
// document is fetched, then offered as a file from memory.
export function saveExport(data: ProfileExport): void {
  const url = URL.createObjectURL(new Blob([`${JSON.stringify(data, null, 2)}\n`], { type: "application/json" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = exportFileName(data);
  link.click();
  // Revoked on the next task, once the click has started the download, so the file is not kept in memory.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

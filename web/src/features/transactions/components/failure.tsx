import { useTranslations } from "next-intl";

import { ApiError, PROBLEM_TYPES } from "@/shared/lib/api-error";

// What went wrong, on the page and never rethrown or logged (SPEC-013). A refused statement lists each reason the API
// gave, keyed "file" or "line N"; those never quote the file.
export function Failure({ error }: { error: Error }) {
  const t = useTranslations("Transactions.failure");
  const failure = error instanceof ApiError ? error.failure : null;

  if (failure?.kind === "network") return <Alert title={t("network")} />;
  if (failure?.kind !== "problem") return <Alert title={t("other", { status: failure?.status ?? 0 })} />;

  const { problem } = failure;
  if (problem.type === PROBLEM_TYPES.databaseUnavailable) return <Alert title={t("database")} />;
  if (problem.type === PROBLEM_TYPES.statementTooLarge) return <Alert title={t("tooLarge")} />;
  if (problem.type === PROBLEM_TYPES.invalidInput) return <Alert title={t("refused")} reasons={reasonsOf(problem.extensions.errors)} />;
  return <Alert title={t("other", { status: problem.status })} />;
}

export function Alert({ title, reasons = [] }: { title: string; reasons?: string[] }) {
  return (
    <div role="alert" className="rounded-xl border border-destructive bg-background p-4">
      <p className="font-medium text-destructive">{title}</p>
      {reasons.length > 0 && (
        <ul className="mt-1 list-disc pl-5 text-sm">
          {reasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

function reasonsOf(errors: unknown): string[] {
  if (typeof errors !== "object" || errors === null) return [];
  return Object.values(errors).flatMap((reasons) => (Array.isArray(reasons) ? reasons.filter((r) => typeof r === "string") : []));
}

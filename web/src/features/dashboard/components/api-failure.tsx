import { useTranslations } from "next-intl";

import { ApiError, PROBLEM_TYPES } from "@/data/api-error";

// What went wrong, in words, on the page. Never rethrown or logged: detail can quote an amount (SPEC-013).
export function ApiFailure({ error }: { error: Error }) {
  const t = useTranslations("Dashboard.failure");
  const failure = error instanceof ApiError ? error.failure : null;

  if (failure?.kind === "network") return <Alert title={t("network")} />;
  if (failure?.kind !== "problem") return <Alert title={t("other", { status: failure?.status ?? 0 })} />;

  const { problem } = failure;
  switch (problem.type) {
    case PROBLEM_TYPES.configGap:
      return <Alert title={t("gap")} lead={t("gapLead")} detail={problem.detail} />;
    case PROBLEM_TYPES.estimateRefused:
      return <Alert title={t("refused")} detail={problem.detail} />;
    case PROBLEM_TYPES.invalidInput:
      return <Alert title={t("invalid")} detail={problem.detail} />;
    default:
      return <Alert title={t("other", { status: problem.status })} />;
  }
}

function Alert({ title, lead, detail }: { title: string; lead?: string; detail?: string }) {
  return (
    <div role="alert" className="rounded-xl border border-destructive bg-background p-4">
      <p className="font-medium text-destructive">{title}</p>
      {lead && <p className="mt-1 text-sm">{lead}</p>}
      {detail && <p className="mt-1 text-sm">{detail}</p>}
    </div>
  );
}

import Link from "next/link";
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";

import { ApiError, PROBLEM_TYPES } from "@/shared/lib/api-error";

// What went wrong, in words, on the page. Never rethrown or logged: detail can quote an amount (SPEC-013). A gap is the one
// failure the profile can route around, by choosing another tax year, so it links to settings when given where they are.
export function ApiFailure({ error, settings }: { error: Error; settings?: string }) {
  const t = useTranslations("Failure");
  const failure = error instanceof ApiError ? error.failure : null;

  if (failure?.kind === "network") return <Alert title={t("network")} />;
  if (failure?.kind !== "problem") return <Alert title={t("other", { status: failure?.status ?? 0 })} />;

  const { problem } = failure;
  switch (problem.type) {
    case PROBLEM_TYPES.configGap:
      return (
        <Alert title={t("gap")} lead={t("gapLead")} detail={problem.detail}>
          {settings && (
            <Link href={settings} className="mt-2 inline-block text-sm font-medium underline underline-offset-4">
              {t("gapSettings")}
            </Link>
          )}
        </Alert>
      );
    case PROBLEM_TYPES.estimateRefused:
      return <Alert title={t("refused")} detail={problem.detail} />;
    case PROBLEM_TYPES.databaseUnavailable:
      return <Alert title={t("database")} />;
    default:
      return <Alert title={t("other", { status: problem.status })} />;
  }
}

function Alert({ title, lead, detail, children }: { title: string; lead?: string; detail?: string; children?: ReactNode }) {
  return (
    <div role="alert" className="rounded-xl border border-destructive bg-background p-4">
      <p className="font-medium text-destructive">{title}</p>
      {lead && <p className="mt-1 text-sm">{lead}</p>}
      {detail && <p className="mt-1 text-sm">{detail}</p>}
      {children}
    </div>
  );
}

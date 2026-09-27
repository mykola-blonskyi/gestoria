"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/shared/ui/page-header";

// The error is not logged or shown: its message may carry what the page was displaying (SPEC-013).
export default function ErrorPage({ reset }: { error: Error; reset: () => void }) {
  const t = useTranslations("Error");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <button
        type="button"
        onClick={reset}
        className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:opacity-90"
      >
        {t("retry")}
      </button>
    </>
  );
}

import { useTranslations } from "next-intl";

import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

export function PeriodsPage() {
  const t = useTranslations("Periods");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <EmptyState points={[t("points.quarters"), t("points.years"), t("points.trace")]} pending={t("pending")} />
    </>
  );
}

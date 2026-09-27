import { useTranslations } from "next-intl";

import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

export function DashboardPage() {
  const t = useTranslations("Dashboard");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <EmptyState points={[t("points.holdBack"), t("points.nextPayment"), t("points.cuota"), t("points.renta"), t("points.warnings")]} pending={t("pending")} />
    </>
  );
}

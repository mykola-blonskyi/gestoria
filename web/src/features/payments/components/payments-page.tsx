import { useTranslations } from "next-intl";

import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

export function PaymentsPage() {
  const t = useTranslations("Payments");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <EmptyState points={[t("points.obligations"), t("points.amounts"), t("points.windows"), t("points.history")]} pending={t("pending")} />
    </>
  );
}

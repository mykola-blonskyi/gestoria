import { useTranslations } from "next-intl";

import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

export function TransactionsPage() {
  const t = useTranslations("Transactions");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <EmptyState points={[t("points.table"), t("points.filters"), t("points.review")]} pending={t("pending")} />
    </>
  );
}

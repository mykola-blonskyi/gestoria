import { useTranslations } from "next-intl";

import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

export function AuthPage() {
  const t = useTranslations("Auth");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <EmptyState points={[t("points.key"), t("points.status")]} pending={t("pending")} />
    </>
  );
}

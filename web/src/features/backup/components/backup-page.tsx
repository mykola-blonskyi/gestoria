import { useTranslations } from "next-intl";

import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

export function BackupPage() {
  const t = useTranslations("Backup");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <EmptyState points={[t("points.export"), t("points.restore")]} pending={t("pending")} />
    </>
  );
}

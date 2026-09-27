import { useTranslations } from "next-intl";

import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

import { ExportSection } from "./export-section";

export function BackupPage() {
  const t = useTranslations("Backup");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="mb-6">
        <ExportSection />
      </div>
      <EmptyState points={[t("points.restore")]} pending={t("pending")} />
    </>
  );
}

import { useTranslations } from "next-intl";

import { PageHeader } from "@/shared/ui/page-header";

import { ExportSection } from "./export-section";
import { RestoreSection } from "./restore-section";

export function BackupPage() {
  const t = useTranslations("Backup");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="grid gap-6">
        <ExportSection />
        <RestoreSection />
      </div>
    </>
  );
}

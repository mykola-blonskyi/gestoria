import { useTranslations } from "next-intl";

import { LanguageToggle } from "@/shared/shell/language-toggle";
import { ThemeToggle } from "@/shared/theme/theme-toggle";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { EmptyState } from "@/shared/ui/empty-state";
import { PageHeader } from "@/shared/ui/page-header";

export function SettingsPage() {
  const t = useTranslations("Settings");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <Card className="mb-6">
        <CardHeader>
          <CardTitle>
            <h2>{t("preferences")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-6">
          <ThemeToggle />
          <LanguageToggle />
        </CardContent>
      </Card>
      <EmptyState points={[t("points.profile"), t("points.data")]} pending={t("pending")} />
    </>
  );
}

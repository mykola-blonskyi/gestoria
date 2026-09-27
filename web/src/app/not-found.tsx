import { useTranslations } from "next-intl";
import Link from "next/link";

import { PageHeader } from "@/shared/ui/page-header";

export default function NotFound() {
  const t = useTranslations("NotFound");

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <Link href="/" className="text-primary underline underline-offset-4">
        {t("home")}
      </Link>
    </>
  );
}

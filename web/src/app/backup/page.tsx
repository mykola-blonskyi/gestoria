import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { BackupPage } from "@/features/backup";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Backup");
  return { title: t("title") };
}

export default function Page() {
  return <BackupPage />;
}

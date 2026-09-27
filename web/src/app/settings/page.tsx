import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { SettingsPage } from "@/features/settings";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Settings");
  return { title: t("title") };
}

export default function Page() {
  return <SettingsPage />;
}

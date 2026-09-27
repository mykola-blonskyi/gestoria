import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { DashboardPage } from "@/features/dashboard";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Dashboard");
  return { title: { absolute: `${t("title")} · GestorIA` } };
}

export default function Page() {
  return <DashboardPage />;
}

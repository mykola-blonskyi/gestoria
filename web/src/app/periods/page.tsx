import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { PeriodsPage } from "@/features/periods";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Periods");
  return { title: t("title") };
}

export default function Page() {
  return <PeriodsPage />;
}

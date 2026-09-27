import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { AuthPage } from "@/features/auth";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Auth");
  return { title: t("title") };
}

export default function Page() {
  return <AuthPage />;
}

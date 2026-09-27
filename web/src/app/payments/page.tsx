import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { PaymentsPage } from "@/features/payments";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Payments");
  return { title: t("title") };
}

export default function Page() {
  return <PaymentsPage />;
}

import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { TransactionsPage } from "@/features/transactions";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Transactions");
  return { title: t("title") };
}

export default function Page() {
  return <TransactionsPage />;
}

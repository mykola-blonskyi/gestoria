"use client";

import { useTranslations } from "next-intl";

import { Button } from "@/shared/ui/button";
import { PageHeader } from "@/shared/ui/page-header";

import { useLock } from "../hooks/use-api-key";

// Reached only through AuthGate, so the app is unlocked whenever this renders.
export function AuthPage() {
  const t = useTranslations("Auth");
  const lock = useLock();

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="grid max-w-xl gap-4">
        <p role="status">{t("unlocked")}</p>
        <Button variant="outline" onClick={lock} className="justify-self-start">
          {t("lock")}
        </Button>
      </div>
    </>
  );
}

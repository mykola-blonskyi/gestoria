"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";

import { PageHeader } from "@/shared/ui/page-header";

import { useProfile } from "../hooks/use-profile";
import { ApiFailure } from "./api-failure";
import { CalendarView } from "./calendar-view";

const SETTINGS = "/settings";

export function PaymentsPage() {
  const t = useTranslations("Payments");
  const profile = useProfile();

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="grid gap-6">
        {profile.isPending ? (
          <p role="status">{t("profile.loading")}</p>
        ) : profile.isError ? (
          <ApiFailure error={profile.error} />
        ) : profile.data === null ? (
          <div className="grid gap-2 rounded-xl border border-border p-4">
            <h2 className="font-medium">{t("profile.missing")}</h2>
            <p className="text-sm">{t("profile.missingLead")}</p>
            <Link href={SETTINGS} className="text-sm font-medium underline underline-offset-4">
              {t("profile.enter")}
            </Link>
          </div>
        ) : (
          <CalendarView profileId={profile.data.id} />
        )}
      </div>
    </>
  );
}

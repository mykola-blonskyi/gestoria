"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useState } from "react";

import type { Profile } from "@/data/profiles";
import type { Quarter } from "@/data/set-aside";
import { QUARTERS, defaultQuarter } from "@/shared/lib/quarters";
import { ApiFailure } from "@/shared/ui/api-failure";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";
import { PageHeader } from "@/shared/ui/page-header";

import { useProfile } from "../hooks/use-profile";
import { useSetAsideEstimate } from "../hooks/use-set-aside-estimate";
import { EstimateView } from "./estimate-view";

const SETTINGS = "/settings";

export function DashboardPage() {
  const t = useTranslations("Dashboard");
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
          <Estimate profile={profile.data} />
        )}
      </div>
    </>
  );
}

function Estimate({ profile }: { profile: Profile }) {
  const t = useTranslations("Dashboard");
  const [asOf, setAsOf] = useState<Quarter>(() => defaultQuarter(profile.taxYear, profile.activity.alta, new Date()));
  const estimate = useSetAsideEstimate(profile.id, asOf);

  return (
    <>
      <div className="grid gap-3 sm:grid-cols-[auto_1fr] sm:items-end">
        <div className="grid gap-1.5">
          <label htmlFor="as-of" className="text-sm font-medium">
            {t("asOf")}
          </label>
          <NativeSelect id="as-of" value={asOf} onChange={(event) => setAsOf(event.target.value as Quarter)}>
            {QUARTERS.map((quarter) => (
              <NativeSelectOption key={quarter} value={quarter}>
                {quarter}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <p className="text-sm text-muted-foreground">
          {t("basis", { year: profile.taxYear })}{" "}
          <Link href={SETTINGS} className="font-medium underline underline-offset-4">
            {t("profile.edit")}
          </Link>
        </p>
      </div>
      {estimate.isPending ? (
        <p role="status">{t("calculating")}</p>
      ) : estimate.isError ? (
        <ApiFailure error={estimate.error} settings={SETTINGS} />
      ) : (
        <EstimateView estimate={estimate.data} />
      )}
    </>
  );
}

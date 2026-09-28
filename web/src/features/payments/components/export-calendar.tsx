"use client";

import { useLocale, useTranslations } from "next-intl";
import { useId, useState } from "react";

import { ApiError, PROBLEM_TYPES } from "@/shared/lib/api-error";
import type { Locale } from "@/shared/constants/locales";
import { Button } from "@/shared/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";

import { useExportIcs } from "../hooks/use-export-ics";

// Downloads the calendar as an RFC 5545 file for the taxpayer's own calendar app (#70). Amounts stay out of event titles
// unless this opt-in is checked (SPEC-013): nothing here defaults to sharing a figure with whatever the file syncs to.
export function ExportCalendar({ profileId, anyUpcoming }: { profileId: string; anyUpcoming: boolean }) {
  const t = useTranslations("Payments.export");
  const locale = useLocale() as Locale;
  const checkboxId = useId();
  const [includeAmounts, setIncludeAmounts] = useState(false);
  const exportIcs = useExportIcs(profileId, locale);

  const download = () => {
    exportIcs.mutate(includeAmounts, {
      onSuccess: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = "gestoria-payments.ics";
        link.click();
        // Safari and Firefox can cancel the download if the object URL is revoked synchronously after the click; defer it
        // to the next tick, past the browser's own navigation to the URL.
        setTimeout(() => URL.revokeObjectURL(url), 0);
      },
    });
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>{t("heading")}</h2>
        </CardTitle>
        <p className="text-sm text-muted-foreground">{t("lead")}</p>
      </CardHeader>
      <CardContent className="grid gap-3">
        <label className="flex items-center gap-2 text-sm">
          <input
            id={checkboxId}
            type="checkbox"
            checked={includeAmounts}
            onChange={(event) => setIncludeAmounts(event.target.checked)}
            className="size-4 rounded border-input"
          />
          {t("includeAmounts")}
        </label>
        <div>
          <Button onClick={download} disabled={!anyUpcoming || exportIcs.isPending}>
            {t("button")}
          </Button>
        </div>
        {!anyUpcoming || isNoUpcoming(exportIcs.error) ? (
          <p className="text-sm text-muted-foreground">{t("nothingUpcoming")}</p>
        ) : (
          exportIcs.isError && <p className="text-sm text-destructive">{t("failed")}</p>
        )}
      </CardContent>
    </Card>
  );
}

// The day can turn between loading the page and pressing the button; the API's own answer then says the same.
function isNoUpcoming(error: Error | null): boolean {
  return error instanceof ApiError && error.failure.kind === "problem" && error.failure.problem.type === PROBLEM_TYPES.noUpcomingObligations;
}

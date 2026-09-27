"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";

import { ApiError } from "@/data/api-error";
import { Button } from "@/shared/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/card";

import { useExportProfile, useProfile } from "../hooks/use-export";
import { saveExport } from "./save-export";

const SETTINGS = "/settings";

// Everything stored on this installation in one file (#74), labelled as personal financial data wherever it is offered.
export function ExportSection() {
  const t = useTranslations("Backup.export");
  const profile = useProfile();

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>{t("heading")}</h2>
        </CardTitle>
        <CardDescription>{t("lead")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        <div role="note" className="rounded-xl border border-destructive p-4">
          <p className="font-medium text-destructive">{t("label")}</p>
          <p className="mt-1 text-sm">{t("care")}</p>
        </div>
        {profile.isPending ? (
          <p role="status">{t("loading")}</p>
        ) : profile.isError ? (
          <Failure error={profile.error} />
        ) : profile.data === null ? (
          <p className="text-sm">
            {t("nothing")}{" "}
            <Link href={SETTINGS} className="font-medium underline underline-offset-4">
              {t("enter")}
            </Link>
          </p>
        ) : (
          <Download id={profile.data.id} />
        )}
      </CardContent>
    </Card>
  );
}

function Download({ id }: { id: string }) {
  const t = useTranslations("Backup.export");
  const download = useExportProfile();

  return (
    <>
      <div className="grid justify-items-start gap-2">
        <Button disabled={download.isPending} onClick={() => download.mutate(id, { onSuccess: saveExport })}>
          {download.isPending ? t("downloading") : t("download")}
        </Button>
        <p className="text-sm text-muted-foreground">{t("format")}</p>
      </div>
      {download.isError && <Failure error={download.error} />}
      {download.isSuccess && (
        <p role="status" className="text-sm font-medium">
          {t("done")}
        </p>
      )}
    </>
  );
}

// What went wrong, in words. Never rethrown or logged (SPEC-013).
function Failure({ error }: { error: Error }) {
  const t = useTranslations("Backup.export.failure");
  const failure = error instanceof ApiError ? error.failure : null;
  const status = failure?.kind === "problem" ? failure.problem.status : failure?.kind === "http" ? failure.status : 0;

  return (
    <p role="alert" className="rounded-xl border border-destructive p-4 font-medium text-destructive">
      {failure?.kind === "network" ? t("network") : status === 404 ? t("gone") : t("other", { status })}
    </p>
  );
}

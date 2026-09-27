"use client";

import { useTranslations } from "next-intl";

import { ApiError } from "@/data/api-error";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/card";

import { useProfile, useSaveProfile } from "../hooks/use-profile";
import { useTaxYears } from "../hooks/use-tax-years";
import { ProfileForm } from "./profile-form";
import { emptyValues, fromProfile } from "./profile-values";

// The taxpayer profile, entered once and stored by the API (#69). The overview estimates from it.
export function ProfileSection() {
  const t = useTranslations("Settings.profile");
  const taxYears = useTaxYears();
  const profile = useProfile();
  const save = useSaveProfile();

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>{t("heading")}</h2>
        </CardTitle>
        <CardDescription>{t("privacy")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-6">
        {taxYears.isError ? (
          <Failure error={taxYears.error} />
        ) : profile.isError ? (
          <Failure error={profile.error} />
        ) : taxYears.isPending || profile.isPending ? (
          <p role="status">{t("loading")}</p>
        ) : (
          // Keyed by the stored profile, so the form starts again from what the API holds once the first save creates it.
          <ProfileForm
            key={profile.data?.id ?? "new"}
            taxYears={taxYears.data}
            initial={profile.data ? fromProfile(profile.data) : emptyValues(taxYears.data)}
            pending={save.isPending}
            refusals={refusalsOf(save.error)}
            onSubmit={(input) => save.mutate({ id: profile.data?.id ?? null, input })}
          />
        )}
        {save.isError && <Failure error={save.error} />}
        {save.isSuccess && (
          <p role="status" className="text-sm font-medium">
            {t("saved")}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

// What went wrong, in words. Never rethrown or logged: detail can quote an amount (SPEC-013).
function Failure({ error }: { error: Error }) {
  const t = useTranslations("Settings.profile.failure");
  const failure = error instanceof ApiError ? error.failure : null;
  const title =
    failure?.kind === "network"
      ? t("network")
      : failure?.kind === "problem" && failure.problem.status === 400
        ? t("invalid")
        : t("other", { status: failure?.kind === "problem" ? failure.problem.status : failure?.kind === "http" ? failure.status : 0 });
  const detail = failure?.kind === "problem" && failure.problem.status !== 400 ? failure.problem.detail : undefined;

  return (
    <div role="alert" className="rounded-xl border border-destructive bg-background p-4">
      <p className="font-medium text-destructive">{title}</p>
      {detail && <p className="mt-1 text-sm">{detail}</p>}
    </div>
  );
}

// A 400's "errors" member maps the JSON path of each refused value to why (ValidationProblemDetails).
function refusalsOf(error: Error | null): Record<string, string> {
  if (!(error instanceof ApiError) || error.failure.kind !== "problem") return {};
  const errors = error.failure.problem.extensions.errors;
  if (typeof errors !== "object" || errors === null) return {};
  return Object.fromEntries(
    Object.entries(errors).flatMap(([path, reasons]) =>
      Array.isArray(reasons) && typeof reasons[0] === "string" ? [[path, reasons[0]]] : [],
    ),
  );
}

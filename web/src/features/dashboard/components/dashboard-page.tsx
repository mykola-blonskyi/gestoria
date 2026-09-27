"use client";

import { useTranslations } from "next-intl";

import { ApiError } from "@/data/api-error";
import { PageHeader } from "@/shared/ui/page-header";

import { useSetAsideEstimate } from "../hooks/use-set-aside-estimate";
import { useTaxYears } from "../hooks/use-tax-years";
import { ApiFailure } from "./api-failure";
import { EstimateForm } from "./estimate-form";
import { EstimateView } from "./estimate-view";

export function DashboardPage() {
  const t = useTranslations("Dashboard");
  const taxYears = useTaxYears();
  const estimate = useSetAsideEstimate();

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="grid gap-6">
        {taxYears.isPending ? (
          <p role="status">{t("taxYears.loading")}</p>
        ) : taxYears.isError ? (
          <ApiFailure error={taxYears.error} />
        ) : (
          <EstimateForm
            taxYears={taxYears.data}
            pending={estimate.isPending}
            refusals={refusalsOf(estimate.error)}
            onSubmit={(request) => estimate.mutate(request)}
          />
        )}
        {estimate.isError && <ApiFailure error={estimate.error} />}
        {estimate.isSuccess && <EstimateView estimate={estimate.data} />}
      </div>
    </>
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

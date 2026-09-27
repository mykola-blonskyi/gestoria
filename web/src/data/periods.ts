import { queryOptions } from "@tanstack/react-query";

import type { components } from "@/data/api-types";
import { apiFetch } from "@/data/client";
import type { Quarter } from "@/data/set-aside";

// A quarter's Modelo 130 result, or the annual true-up beyond it (#71). Neither is cached data the profile changes:
// both are computed fresh on every request, so a stored change reaches them without invalidating anything here.
export type QuarterResult = components["schemas"]["QuarterResultView"];
export type AnnualTrueUp = components["schemas"]["AnnualTrueUpView"];

export const periodKeys = {
  quarter: (id: string, quarter: Quarter) => ["profiles", id, "calculations", "quarter", quarter] as const,
  annualTrueUp: (id: string) => ["profiles", id, "calculations", "annual-true-up"] as const,
};

export const quarterResultQuery = (id: string, quarter: Quarter) =>
  queryOptions({
    queryKey: periodKeys.quarter(id, quarter),
    queryFn: () => apiFetch<QuarterResult>(`/profiles/${id}/calculations/quarter?quarter=${quarter}`, { method: "POST" }),
  });

export const annualTrueUpQuery = (id: string) =>
  queryOptions({
    queryKey: periodKeys.annualTrueUp(id),
    queryFn: () => apiFetch<AnnualTrueUp>(`/profiles/${id}/calculations/annual-true-up`, { method: "POST" }),
  });

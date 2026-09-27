import { queryOptions } from "@tanstack/react-query";

import type { components } from "@/data/api-types";
import { apiFetch } from "@/data/client";

export type TaxYear = components["schemas"]["TaxYearView"];

export const taxYearKeys = {
  all: ["tax-years"] as const,
};

// A configuration changes only when the API is redeployed with a new file.
export const taxYearsQuery = () =>
  queryOptions({
    queryKey: taxYearKeys.all,
    queryFn: () => apiFetch<TaxYear[]>("/config/tax-years"),
    staleTime: Infinity,
  });

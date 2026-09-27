import { mutationOptions } from "@tanstack/react-query";

import type { components } from "@/data/api-types";
import { apiFetch } from "@/data/client";

// The console's input file (src/GestorIA.Cli/README.md), which is also the request body.
export type SetAsideInput = components["schemas"]["SetAsideInputDocument"];
export type SetAsideEstimate = components["schemas"]["SetAsideEstimate"];
export type Quarter = components["schemas"]["Quarter"];
export type TraceStep = components["schemas"]["TraceStepView"];
export type TraceSection = components["schemas"]["TraceSection"];
export type Notice = components["schemas"]["NoticeView"];

export type EstimateRequest = { taxYear: number; input: SetAsideInput };

export const setAsideKeys = {
  estimate: ["set-aside", "estimate"] as const,
};

// A mutation, not a query: the answer is computed from what the user typed, not a resource to cache
// and refetch. Its variables are personal financial data and stay in this tab's memory (SPEC-013).
export const estimateSetAsideMutation = () =>
  mutationOptions({
    mutationKey: setAsideKeys.estimate,
    mutationFn: ({ taxYear, input }: EstimateRequest) =>
      apiFetch<SetAsideEstimate>(`/set-aside/estimate?taxYear=${taxYear}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
  });

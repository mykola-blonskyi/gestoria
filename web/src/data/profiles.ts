import { mutationOptions, queryOptions } from "@tanstack/react-query";

import type { components } from "@/data/api-types";
import { apiFetch } from "@/data/client";
import type { Quarter, SetAsideEstimate } from "@/data/set-aside";

// The taxpayer profile, stored by the API (SPEC-009 §2, #69). Local mode keeps one per installation, so the list holds
// none or one. What it holds is personal financial data: it stays in the query cache, in this tab's memory (SPEC-013).
export type Profile = components["schemas"]["ProfileView"];
export type ProfileInput = components["schemas"]["ProfileInputDocument"];

export const profileKeys = {
  all: ["profiles"] as const,
  estimate: (id: string, asOf: Quarter) => ["profiles", id, "set-aside", asOf] as const,
};

// The installation's one profile, or null before the first save.
export const profileQuery = () =>
  queryOptions({
    queryKey: profileKeys.all,
    queryFn: () => apiFetch<Profile[]>("/profiles"),
    select: (profiles): Profile | null => profiles[0] ?? null,
  });

// The first save creates the profile, every later one replaces it.
export const saveProfileMutation = () =>
  mutationOptions({
    mutationKey: ["profiles", "save"],
    mutationFn: ({ id, input }: { id: string | null; input: ProfileInput }) =>
      apiFetch<Profile>(id === null ? "/profiles" : `/profiles/${id}`, {
        method: id === null ? "POST" : "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
  });

// Computed by the API from the stored profile, so a saved change reaches it by invalidating profileKeys.all, the prefix of
// this key.
export const profileEstimateQuery = (id: string, asOf: Quarter) =>
  queryOptions({
    queryKey: profileKeys.estimate(id, asOf),
    queryFn: () => apiFetch<SetAsideEstimate>(`/profiles/${id}/set-aside/estimate?asOf=${asOf}`),
  });

import { queryOptions } from "@tanstack/react-query";

import type { components } from "@/data/api-types";
import { apiFetch, apiFetchBlob } from "@/data/client";
import type { Locale } from "@/shared/constants/locales";

// Every obligation of the profile's tax year (SPEC-009 §2, #70), computed by the engine: never recomputed here.
export type PaymentsCalendar = components["schemas"]["PaymentsCalendarView"];
export type Obligation = components["schemas"]["ObligationView"];
export type ObligationKind = components["schemas"]["ObligationKind"];
export type ObligationAmount = components["schemas"]["ObligationAmountView"];

export const paymentsCalendarQuery = (profileId: string) =>
  queryOptions({
    queryKey: ["profiles", profileId, "calendar"] as const,
    queryFn: () => apiFetch<PaymentsCalendar>(`/profiles/${profileId}/calendar`),
  });

// The same calendar as an RFC 5545 export, its event text in the caller's own locale; includeAmounts is the user's own
// opt-in, never on by default (SPEC-013).
export const paymentsCalendarIcs = (profileId: string, includeAmounts: boolean, locale: Locale) =>
  apiFetchBlob(`/profiles/${profileId}/calendar.ics?amounts=${includeAmounts}&lang=${locale}`);

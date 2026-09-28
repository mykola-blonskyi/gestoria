import { useMutation } from "@tanstack/react-query";

import { paymentsCalendarIcs } from "@/data/payments";
import type { Locale } from "@/shared/constants/locales";

// includeAmounts is the caller's own opt-in (SPEC-013): the API never puts a figure in an event title unless asked to.
export function useExportIcs(profileId: string, locale: Locale) {
  return useMutation({
    mutationFn: (includeAmounts: boolean) => paymentsCalendarIcs(profileId, includeAmounts, locale),
  });
}

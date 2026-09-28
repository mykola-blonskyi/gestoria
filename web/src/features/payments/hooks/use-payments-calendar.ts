import { useQuery } from "@tanstack/react-query";

import { paymentsCalendarQuery } from "@/data/payments";

export function usePaymentsCalendar(profileId: string) {
  return useQuery(paymentsCalendarQuery(profileId));
}

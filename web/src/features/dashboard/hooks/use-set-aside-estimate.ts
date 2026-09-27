import { useQuery } from "@tanstack/react-query";

import { profileEstimateQuery } from "@/data/profiles";
import type { Quarter } from "@/data/set-aside";

export function useSetAsideEstimate(profileId: string, asOf: Quarter) {
  return useQuery(profileEstimateQuery(profileId, asOf));
}

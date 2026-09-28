import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { profileKeys } from "@/data/profiles";
import type { Quarter } from "@/data/set-aside";
import { classifyMutation, importStatementMutation, reviewQueueQuery, transactionsQuery } from "@/data/transactions";

export function useTransactions(profileId: string, year: number, quarter: Quarter | null) {
  return useQuery(transactionsQuery(profileId, year, quarter));
}

export function useReviewQueue(profileId: string) {
  return useQuery(reviewQueueQuery(profileId));
}

// An import or a class can change any quarter's lists, the review queue and the actuals the estimate counts, so every
// cached answer about the profile is dropped.
function useInvalidateProfile(profileId: string) {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: profileKeys.profile(profileId) });
}

export function useImportStatement(profileId: string) {
  const invalidate = useInvalidateProfile(profileId);
  return useMutation({ ...importStatementMutation(), onSuccess: invalidate });
}

export function useClassify(profileId: string) {
  const invalidate = useInvalidateProfile(profileId);
  return useMutation({ ...classifyMutation(), onSuccess: invalidate });
}

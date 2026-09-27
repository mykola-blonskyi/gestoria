import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { Quarter } from "@/data/set-aside";
import { importStatementMutation, transactionKeys, transactionsQuery } from "@/data/transactions";

export function useTransactions(profileId: string, year: number, quarter: Quarter | null) {
  return useQuery(transactionsQuery(profileId, year, quarter));
}

// An import can add movements to any quarter, so every cached list of the profile is dropped.
export function useImportStatement(profileId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    ...importStatementMutation(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: transactionKeys.all(profileId) }),
  });
}

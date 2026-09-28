import { mutationOptions, queryOptions } from "@tanstack/react-query";

import type { components, operations } from "@/data/api-types";
import { apiFetch } from "@/data/client";
import type { Quarter } from "@/data/set-aside";

// Bank statements imported into the stored profile and the movements they hold (SPEC-009 §2, #72). Descriptions and
// amounts are personal financial data: they stay in the query cache, in this tab's memory (SPEC-013).
export type Transaction = components["schemas"]["TransactionView"];
export type Bank = operations["importBankStatement"]["parameters"]["query"]["bank"];
export type ReviewItem = components["schemas"]["ReviewItem"];
export type TransactionClass = components["schemas"]["TransactionClass"];

export const BANKS = ["bbva"] as const satisfies readonly Bank[];

export const TRANSACTION_CLASSES = [
  "activityIncome",
  "deductibleExpense",
  "socialSecurity",
  "aeatPayment",
  "employmentIncome",
  "savingsIncome",
  "ownTransfer",
  "personal",
] as const satisfies readonly TransactionClass[];

// The API refuses a larger statement (StatementFile.MaxBytes in GestorIA.Api); checking first saves the upload.
export const STATEMENT_MAX_BYTES = 2 * 1024 * 1024;

export const transactionKeys = {
  list: (profileId: string, year: number, quarter: Quarter | null) =>
    ["profiles", profileId, "transactions", year, quarter ?? "year"] as const,
  reviewQueue: (profileId: string) => ["profiles", profileId, "review-queue"] as const,
};

export const transactionsQuery = (profileId: string, year: number, quarter: Quarter | null) =>
  queryOptions({
    queryKey: transactionKeys.list(profileId, year, quarter),
    queryFn: () =>
      apiFetch<Transaction[]>(`/profiles/${profileId}/transactions?year=${year}${quarter === null ? "" : `&quarter=${quarter}`}`),
  });

// The file itself is the body; importing it again stores nothing twice, so a retry is harmless.
export const importStatementMutation = () =>
  mutationOptions({
    mutationKey: ["profiles", "bank-statements", "import"],
    mutationFn: ({ profileId, bank, file }: { profileId: string; bank: Bank; file: File }) =>
      apiFetch<components["schemas"]["BankStatementImport"]>(`/profiles/${profileId}/bank-statements?bank=${bank}`, {
        method: "POST",
        headers: { "Content-Type": "text/csv" },
        body: file,
      }),
  });

// The movements no rule is sure about, oldest first, each with the class a rule suggests when one does.
export const reviewQueueQuery = (profileId: string) =>
  queryOptions({
    queryKey: transactionKeys.reviewQueue(profileId),
    queryFn: () => apiFetch<ReviewItem[]>(`/profiles/${profileId}/review-queue`),
  });

// The user's class replaces any rule's; sending the same class again changes nothing.
export const classifyMutation = () =>
  mutationOptions({
    mutationKey: ["transactions", "classify"],
    mutationFn: ({ id, ...classification }: { id: string } & components["schemas"]["TransactionClassification"]) =>
      apiFetch<void>(`/transactions/${id}/classify`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(classification),
      }),
  });

import type { Transaction } from "@/data/transactions";

export const KINDS = ["all", "in", "out"] as const;
export type Kind = (typeof KINDS)[number];

// Read from the sign of the money string, never from a parsed number; a zero amount counts as money in.
export const kindOf = (transaction: Transaction): Exclude<Kind, "all"> => (transaction.amount.startsWith("-") ? "out" : "in");

"use client";

import { QueryClient, QueryClientProvider, environmentManager } from "@tanstack/react-query";
import type { ReactNode } from "react";

import { ApiError } from "@/data/api-error";

const MAX_RETRIES = 2;

// Only an unreachable API or a 5xx can change on retry; a 4xx (a validation error,
// a 422 for a declared configuration gap) answers the same every time.
export function shouldRetry(failureCount: number, error: Error): boolean {
  if (!(error instanceof ApiError) || failureCount >= MAX_RETRIES) return false;
  const { failure } = error;
  switch (failure.kind) {
    case "network":
      return true;
    case "http":
      return failure.status >= 500;
    case "problem":
      return failure.problem.status >= 500;
  }
}

export function makeQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: 60 * 1000, retry: shouldRetry },
    },
  });
}

let browserQueryClient: QueryClient | undefined;

function getQueryClient(): QueryClient {
  if (environmentManager.isServer()) return makeQueryClient();
  browserQueryClient ??= makeQueryClient();
  return browserQueryClient;
}

export function QueryProvider({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={getQueryClient()}>{children}</QueryClientProvider>;
}

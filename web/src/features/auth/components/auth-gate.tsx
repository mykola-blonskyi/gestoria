"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect, type ReactNode } from "react";

import { useApiKeyState } from "../hooks/use-api-key";
import { UnlockScreen } from "./unlock-screen";

// Every page sits behind this: nothing below it renders, so nothing calls the API, until the key is in memory.
export function AuthGate({ children }: { children: ReactNode }) {
  const state = useApiKeyState();
  const queryClient = useQueryClient();

  // What the API answered stays in memory no longer than the key that fetched it.
  useEffect(() => {
    if (state !== "unlocked") queryClient.clear();
  }, [state, queryClient]);

  return state === "unlocked" ? children : <UnlockScreen refused={state === "refused"} />;
}

"use client";

import type { ReactNode } from "react";

import { useApiKeyState } from "../hooks/use-api-key";
import { UnlockScreen } from "./unlock-screen";

// Every page sits behind this: nothing below it renders, so nothing calls the API, until the key is in memory.
export function AuthGate({ children }: { children: ReactNode }) {
  const state = useApiKeyState();

  return state === "unlocked" ? children : <UnlockScreen refused={state === "refused"} />;
}

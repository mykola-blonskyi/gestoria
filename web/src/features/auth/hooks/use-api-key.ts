import { useMutation } from "@tanstack/react-query";
import { useSyncExternalStore } from "react";

import { unlockMutation } from "@/data/api-key";
import { apiKeyStore, type ApiKeyState } from "@/data/api-key-store";

// The server never holds the key, so it renders the app locked, as a fresh tab does.
const onServer = (): ApiKeyState => "locked";

export function useApiKeyState(): ApiKeyState {
  return useSyncExternalStore(apiKeyStore.subscribe, apiKeyStore.state, onServer);
}

export function useUnlock() {
  return useMutation(unlockMutation());
}

export function useLock(): () => void {
  return apiKeyStore.forget;
}

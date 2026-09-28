import { mutationOptions } from "@tanstack/react-query";

import { API_KEY_HEADER, apiKeyStore } from "@/data/api-key-store";
import { apiFetch } from "@/data/client";

// The key is checked against a real endpoint before the app unlocks: any endpoint behind the key answers 401 to a wrong
// one, and the tax-year list is the cheapest. Then readiness: with the database down every page but this one would fail,
// so the unlock screen says so instead, and the key is sent again once it is started. gcTime 0 drops the finished
// mutation, and the key it holds as its variables, from the mutation cache at once.
export const unlockMutation = () =>
  mutationOptions({
    mutationKey: ["api-key", "unlock"],
    gcTime: 0,
    mutationFn: async (key: string) => {
      await apiFetch("/config/tax-years", { headers: { [API_KEY_HEADER]: key } });
      await apiFetch("/health/ready", { headers: { [API_KEY_HEADER]: key } });
      apiKeyStore.remember(key);
    },
  });

import { useMutation, useQueryClient } from "@tanstack/react-query";

import { profileKeys, restoreProfileMutation } from "@/data/profiles";

// A restore stores a profile where there was none, so everything cached under the profile is asked again.
export function useRestoreProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    ...restoreProfileMutation(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: profileKeys.all }),
  });
}

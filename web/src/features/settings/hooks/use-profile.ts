import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { profileKeys, profileQuery, saveProfileMutation } from "@/data/profiles";

export function useProfile() {
  return useQuery(profileQuery());
}

// A save changes what every estimate is computed from, so it drops the cached profile and estimates alike.
export function useSaveProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    ...saveProfileMutation(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: profileKeys.all }),
  });
}

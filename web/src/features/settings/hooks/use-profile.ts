import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { deleteProfileMutation, profileKeys, profileQuery, saveProfileMutation } from "@/data/profiles";

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

// After a delete nothing cached about the profile may outlive it: the profile and every estimate return to their empty state,
// and the ones on screen are asked again, so the form starts over.
export function useDeleteProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    ...deleteProfileMutation(),
    onSuccess: () => queryClient.resetQueries({ queryKey: profileKeys.all }),
  });
}

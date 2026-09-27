import { useMutation, useQuery } from "@tanstack/react-query";

import { exportProfileMutation, profileQuery } from "@/data/profiles";

export function useProfile() {
  return useQuery(profileQuery());
}

export function useExportProfile() {
  return useMutation(exportProfileMutation());
}

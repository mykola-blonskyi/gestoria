import { useQuery } from "@tanstack/react-query";

import { profileQuery } from "@/data/profiles";

export function useProfile() {
  return useQuery(profileQuery());
}

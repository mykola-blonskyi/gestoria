import { useMutation } from "@tanstack/react-query";

import { estimateSetAsideMutation } from "@/data/set-aside";

export function useSetAsideEstimate() {
  return useMutation(estimateSetAsideMutation());
}

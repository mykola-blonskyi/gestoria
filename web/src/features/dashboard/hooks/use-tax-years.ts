import { useQuery } from "@tanstack/react-query";

import { taxYearsQuery } from "@/data/tax-years";

export function useTaxYears() {
  return useQuery(taxYearsQuery());
}

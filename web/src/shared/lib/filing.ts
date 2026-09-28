// The API's Modelo130FilingView as a literal union: shared does not import the data layer (quarters.ts does the same).
export type Modelo130Filing = "ingreso" | "aDeducir" | "negativa";

export const DUE_MESSAGE_KEY = {
  ingreso: "due",
  aDeducir: "dueADeducir",
  negativa: "dueNegativa",
} as const satisfies Record<Modelo130Filing, string>;

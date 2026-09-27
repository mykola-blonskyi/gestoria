import type { Profile, ProfileInput } from "@/data/profiles";
import type { TaxYear } from "@/data/tax-years";

// The profile form's values: the API's profile (SPEC-009 §2) as strings the user types. Every field is keyed by the JSON
// path the API reports its refusal at, so a check here and an API refusal land on the same field.

export type ProfileValues = {
  taxYear: string;
  region: string;
  employmentIngresos: string;
  employmentSeguridadSocial: string;
  alta: string;
  previousYear: "noActivity" | "rendimientoNeto";
  previousYearNet: string;
  newActivity: "established" | "first" | "following";
  ingresosFromFormerEmployer: string;
  projectionIngresos: string;
  projectionGastos: string;
  baseCotizacion: string;
};

export type ErrorKey = "required" | "amount" | "signedAmount" | "date";

// A check here has a translated message; the API's reason is shown as it came.
export type FieldError = { key: ErrorKey } | { reason: string };

export type FieldErrors = Record<string, FieldError>;

export const PATHS = {
  taxYear: "$.taxYear",
  region: "$.region",
  employmentIngresos: "$.employment.ingresos",
  employmentSeguridadSocial: "$.employment.seguridadSocial",
  alta: "$.activity.alta",
  previousYearNet: "$.activity.previousYear.rendimientoNeto",
  ingresosFromFormerEmployer: "$.activity.newActivity.ingresosFromFormerEmployer",
  projectionIngresos: "$.projection.ingresos",
  projectionGastos: "$.projection.gastos",
  baseCotizacion: "$.projection.baseCotizacion",
} as const;

// A fresh profile starts on the newest year that declares no gap. A year with one is still offered, with its gaps shown,
// but its estimate may be refused until they close: 2026 refuses every estimate while its renta window is unpublished.
function defaultTaxYear(taxYears: readonly TaxYear[]): TaxYear | undefined {
  return taxYears.findLast((year) => year.gaps.length === 0) ?? taxYears.at(-1);
}

export function emptyValues(taxYears: readonly TaxYear[]): ProfileValues {
  const year = defaultTaxYear(taxYears);
  return {
    taxYear: String(year?.taxYear ?? ""),
    region: year?.regions[0]?.code ?? "",
    employmentIngresos: "",
    employmentSeguridadSocial: "",
    alta: "",
    previousYear: "noActivity",
    previousYearNet: "",
    newActivity: "established",
    ingresosFromFormerEmployer: "",
    projectionIngresos: "",
    projectionGastos: "",
    baseCotizacion: "",
  };
}

export function fromProfile(profile: Profile): ProfileValues {
  const { previousYear, newActivity } = profile.activity;
  return {
    taxYear: String(profile.taxYear),
    region: profile.region,
    employmentIngresos: profile.employment.ingresos,
    employmentSeguridadSocial: profile.employment.seguridadSocial,
    alta: profile.activity.alta,
    previousYear: previousYear.kind === "rendimientoNeto" ? "rendimientoNeto" : "noActivity",
    previousYearNet: previousYear.kind === "rendimientoNeto" ? previousYear.rendimientoNeto : "",
    newActivity: newActivity.kind === "started" ? newActivity.period : "established",
    ingresosFromFormerEmployer: newActivity.kind === "started" ? newActivity.ingresosFromFormerEmployer : "",
    projectionIngresos: profile.projection.ingresos,
    projectionGastos: profile.projection.gastos,
    baseCotizacion: profile.projection.baseCotizacion,
  };
}

// The API's rules that need no tax-year configuration: every field present, amounts in euros with at most two decimals,
// zero or more except the previous year's net, dates as yyyy-MM-dd. The rest (a region or a base the year does not have)
// the API judges and reports by the same paths.
export function validate(values: ProfileValues): FieldErrors {
  const errors: FieldErrors = {};
  const check = (path: string, value: string, rule: (text: string) => ErrorKey | null) => {
    const key = value.trim() === "" ? "required" : rule(value);
    if (key) errors[path] = { key };
  };

  check(PATHS.taxYear, values.taxYear, () => null);
  check(PATHS.region, values.region, () => null);
  check(PATHS.employmentIngresos, values.employmentIngresos, amount);
  check(PATHS.employmentSeguridadSocial, values.employmentSeguridadSocial, amount);
  check(PATHS.alta, values.alta, date);
  if (values.previousYear === "rendimientoNeto") check(PATHS.previousYearNet, values.previousYearNet, signedAmount);
  if (values.newActivity !== "established") check(PATHS.ingresosFromFormerEmployer, values.ingresosFromFormerEmployer, amount);
  check(PATHS.projectionIngresos, values.projectionIngresos, amount);
  check(PATHS.projectionGastos, values.projectionGastos, amount);
  check(PATHS.baseCotizacion, values.baseCotizacion, amount);

  return errors;
}

// The API's patterns (src/GestorIA.Api/Profiles/ProfileDocument.cs, ProfileAmounts).
const amount = (text: string): ErrorKey | null => (/^\d{1,12}(\.\d{1,2})?$/.test(text) ? null : "amount");
const signedAmount = (text: string): ErrorKey | null => (/^-?\d{1,12}(\.\d{1,2})?$/.test(text) ? null : "signedAmount");
const date = (text: string): ErrorKey | null => {
  const day = /^\d{4}-\d{2}-\d{2}$/.test(text) ? new Date(`${text}T00:00:00Z`) : null;
  return day !== null && !Number.isNaN(day.getTime()) && day.toISOString().startsWith(text) ? null : "date";
};

export function toInput(values: ProfileValues): ProfileInput {
  return {
    taxYear: Number(values.taxYear),
    region: values.region,
    employment: { ingresos: values.employmentIngresos, seguridadSocial: values.employmentSeguridadSocial },
    activity: {
      alta: values.alta,
      previousYear:
        values.previousYear === "noActivity" ? { kind: "noActivity" } : { kind: "rendimientoNeto", rendimientoNeto: values.previousYearNet },
      newActivity:
        values.newActivity === "established"
          ? { kind: "established" }
          : { kind: "started", period: values.newActivity, ingresosFromFormerEmployer: values.ingresosFromFormerEmployer },
    },
    projection: { ingresos: values.projectionIngresos, gastos: values.projectionGastos, baseCotizacion: values.baseCotizacion },
  };
}

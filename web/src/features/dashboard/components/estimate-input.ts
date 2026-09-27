import type { EstimateRequest, Quarter, SetAsideInput } from "@/data/set-aside";

// The dashboard form: the console's input file (src/GestorIA.Cli/README.md) as strings the user
// types, plus the tax year the console takes as a separate argument. Every field is keyed by the
// JSON path the console and the API report errors at, so an error from either lands on its field.

export const QUARTERS = ["Q1", "Q2", "Q3", "Q4"] as const satisfies readonly Quarter[];

export type Actual = { quarter: Quarter; ingresosYtd: string; gastosYtd: string; cuotasSsYtd: string };

export type EstimateValues = {
  taxYear: string;
  asOf: Quarter;
  region: string;
  employmentIngresos: string;
  employmentSeguridadSocial: string;
  alta: string;
  previousYear: "noActivity" | "rendimientoNeto";
  previousYearNet: string;
  newActivity: "established" | "first" | "following";
  ingresosFromFormerEmployer: string;
  actuals: Actual[];
  projectionIngresos: string;
  projectionGastos: string;
  baseCotizacion: string;
};

export type ErrorKey = "required" | "amount" | "signedAmount" | "date";

// A client-side check has a translated message; the API's reason is shown as it came.
export type FieldError = { key: ErrorKey } | { reason: string };

export type FieldErrors = Record<string, FieldError>;

export const PATHS = {
  taxYear: "taxYear",
  asOf: "$.asOf",
  region: "$.profile.region",
  employmentIngresos: "$.profile.employment.ingresos",
  employmentSeguridadSocial: "$.profile.employment.seguridadSocial",
  alta: "$.profile.activity.alta",
  previousYearNet: "$.profile.activity.previousYear.rendimientoNeto",
  ingresosFromFormerEmployer: "$.profile.activity.newActivity.ingresosFromFormerEmployer",
  projectionIngresos: "$.activity.projection.ingresos",
  projectionGastos: "$.activity.projection.gastos",
  baseCotizacion: "$.activity.projection.baseCotizacion",
  actual: (index: number, field: keyof Omit<Actual, "quarter">) => `$.activity.actuals[${index}].${field}`,
} as const;

export function emptyValues(taxYear: string, region: string): EstimateValues {
  return {
    taxYear,
    asOf: "Q1",
    region,
    employmentIngresos: "",
    employmentSeguridadSocial: "",
    alta: "",
    previousYear: "noActivity",
    previousYearNet: "",
    newActivity: "established",
    ingresosFromFormerEmployer: "",
    actuals: [],
    projectionIngresos: "",
    projectionGastos: "",
    baseCotizacion: "",
  };
}

// The quarter after the last one listed, since actuals are the closed quarters in order.
export function emptyActual(after: readonly Actual[]): Actual {
  const last = after.at(-1);
  const quarter = last ? (QUARTERS[QUARTERS.indexOf(last.quarter) + 1] ?? "Q4") : "Q1";
  return { quarter, ingresosYtd: "", gastosYtd: "", cuotasSsYtd: "" };
}

// The console's boundary rules that need no tax-year configuration: every field present, amounts as
// decimal strings, zero or more except the previous year's net, dates as yyyy-MM-dd. The rest (a base
// outside the year's tables, actuals out of order) the API judges and reports by the same paths.
export function validate(values: EstimateValues): FieldErrors {
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
  if (values.newActivity !== "established") {
    check(PATHS.ingresosFromFormerEmployer, values.ingresosFromFormerEmployer, amount);
  }
  values.actuals.forEach((actual, index) => {
    for (const field of ["ingresosYtd", "gastosYtd", "cuotasSsYtd"] as const) {
      check(PATHS.actual(index, field), actual[field], amount);
    }
  });
  check(PATHS.projectionIngresos, values.projectionIngresos, amount);
  check(PATHS.projectionGastos, values.projectionGastos, amount);
  check(PATHS.baseCotizacion, values.baseCotizacion, amount);

  return errors;
}

const amount = (text: string): ErrorKey | null => (/^\d+(\.\d+)?$/.test(text) ? null : "amount");
const signedAmount = (text: string): ErrorKey | null => (/^-?\d+(\.\d+)?$/.test(text) ? null : "signedAmount");
const date = (text: string): ErrorKey | null => {
  const day = /^\d{4}-\d{2}-\d{2}$/.test(text) ? new Date(`${text}T00:00:00Z`) : null;
  return day !== null && !Number.isNaN(day.getTime()) && day.toISOString().startsWith(text) ? null : "date";
};

// The request the console's input file makes, field for field and in the file's order.
export function toRequest(values: EstimateValues): EstimateRequest {
  const input: SetAsideInput = {
    asOf: values.asOf,
    profile: {
      region: values.region,
      employment: { ingresos: values.employmentIngresos, seguridadSocial: values.employmentSeguridadSocial },
      activity: {
        alta: values.alta,
        previousYear: values.previousYear === "noActivity" ? "noActivity" : { rendimientoNeto: values.previousYearNet },
        newActivity:
          values.newActivity === "established"
            ? "established"
            : { period: values.newActivity, ingresosFromFormerEmployer: values.ingresosFromFormerEmployer },
      },
    },
    activity: {
      retenciones: "foreignPayersOnly",
      actuals: values.actuals.map(({ quarter, ingresosYtd, gastosYtd, cuotasSsYtd }) => ({
        quarter,
        ingresosYtd,
        gastosYtd,
        cuotasSsYtd,
      })),
      projection: {
        ingresos: values.projectionIngresos,
        gastos: values.projectionGastos,
        baseCotizacion: values.baseCotizacion,
      },
    },
  };
  return { taxYear: Number(values.taxYear), input };
}

// Fills the form from a console input file. Only the shape is read here; what is missing or wrong is
// left empty or as found, and the form's own checks and the API report it by path like any typed value.
export function fromInputFile(file: unknown, current: EstimateValues): EstimateValues | null {
  if (!isObject(file)) return null;
  const profile = objectAt(file, "profile");
  const employment = objectAt(profile, "employment");
  const registration = objectAt(profile, "activity");
  const activity = objectAt(file, "activity");
  const projection = objectAt(activity, "projection");
  const previousYear = registration.previousYear;
  const newActivity = registration.newActivity;
  const started = isObject(newActivity) ? newActivity : {};
  const period = text(started, "period");

  return {
    taxYear: current.taxYear,
    asOf: quarterOr(text(file, "asOf"), current.asOf),
    region: text(profile, "region"),
    employmentIngresos: text(employment, "ingresos"),
    employmentSeguridadSocial: text(employment, "seguridadSocial"),
    alta: text(registration, "alta"),
    previousYear: isObject(previousYear) ? "rendimientoNeto" : "noActivity",
    previousYearNet: isObject(previousYear) ? text(previousYear, "rendimientoNeto") : "",
    newActivity: newActivity === "established" ? "established" : period === "following" ? "following" : "first",
    ingresosFromFormerEmployer: text(started, "ingresosFromFormerEmployer"),
    actuals: (Array.isArray(activity.actuals) ? activity.actuals : []).map((entry: unknown) => {
      const actual = isObject(entry) ? entry : {};
      return {
        quarter: quarterOr(text(actual, "quarter"), "Q1"),
        ingresosYtd: text(actual, "ingresosYtd"),
        gastosYtd: text(actual, "gastosYtd"),
        cuotasSsYtd: text(actual, "cuotasSsYtd"),
      };
    }),
    projectionIngresos: text(projection, "ingresos"),
    projectionGastos: text(projection, "gastos"),
    baseCotizacion: text(projection, "baseCotizacion"),
  };
}

type Json = Record<string, unknown>;

const isObject = (value: unknown): value is Json => typeof value === "object" && value !== null && !Array.isArray(value);
const objectAt = (parent: Json, key: string): Json => (isObject(parent[key]) ? parent[key] : {});
const text = (parent: Json, key: string): string => (typeof parent[key] === "string" ? parent[key] : "");
const quarterOr = (value: string, fallback: Quarter): Quarter =>
  (QUARTERS as readonly string[]).includes(value) ? (value as Quarter) : fallback;

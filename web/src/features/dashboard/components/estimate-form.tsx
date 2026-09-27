"use client";

import { useTranslations } from "next-intl";
import { useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from "react";

import type { EstimateRequest } from "@/data/set-aside";
import type { TaxYear } from "@/data/tax-years";
import { Button } from "@/shared/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/card";
import { Input } from "@/shared/ui/input";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";

import {
  PATHS,
  QUARTERS,
  emptyActual,
  emptyValues,
  fromInputFile,
  toRequest,
  validate,
  type Actual,
  type EstimateValues,
  type FieldError,
  type FieldErrors,
} from "./estimate-input";

type Props = {
  taxYears: readonly TaxYear[];
  pending: boolean;
  // The API's refusals of the last request, by JSON path.
  refusals: Readonly<Record<string, string>>;
  onSubmit: (request: EstimateRequest) => void;
};

const fieldId = (path: string) => `field${path.replace(/[^a-zA-Z0-9]+/g, "-")}`;

// The values live in this component's state and nowhere else: no storage, no cookie, no URL.
// Reloading or closing the tab empties the form (SPEC-013).
export function EstimateForm({ taxYears, pending, refusals, onSubmit }: Props) {
  const t = useTranslations("Dashboard.form");
  const newest = taxYears.at(-1);
  const [values, setValues] = useState(() => emptyValues(String(newest?.taxYear ?? ""), newest?.regions[0]?.code ?? ""));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [unreadableFile, setUnreadableFile] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  const regions = taxYears.find((year) => String(year.taxYear) === values.taxYear)?.regions ?? [];
  const shown: FieldErrors = {
    ...Object.fromEntries(Object.entries(refusals).map(([path, reason]) => [path, { reason }])),
    ...errors,
  };
  const set = (patch: Partial<EstimateValues>) => setValues((current) => ({ ...current, ...patch }));
  const setActual = (index: number, patch: Partial<Actual>) =>
    set({ actuals: values.actuals.map((actual, at) => (at === index ? { ...actual, ...patch } : actual)) });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const found = validate(values);
    setErrors(found);
    const first = Object.keys(found)[0];
    if (first !== undefined) {
      document.getElementById(fieldId(first))?.focus();
      return;
    }
    onSubmit(toRequest(values));
  };

  const load = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (file === undefined) return;
    let parsed: unknown = null;
    try {
      parsed = JSON.parse(await file.text());
    } catch {
      // Not JSON: fromInputFile refuses null below.
    }
    const next = fromInputFile(parsed, values);
    setUnreadableFile(next === null);
    if (next !== null) {
      setValues(next);
      setErrors({});
    }
  };

  const amount = (path: string, label: string, value: string, onChange: (value: string) => void) => (
    <Field path={path} label={label} error={shown[path]}>
      {(aria) => (
        <Input {...aria} inputMode="decimal" placeholder="0.00" value={value} onChange={(event) => onChange(event.target.value)} />
      )}
    </Field>
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>{t("heading")}</h2>
        </CardTitle>
        <CardDescription>{t("privacy")}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="mb-6 flex flex-wrap items-center gap-3">
          <Button variant="outline" onClick={() => fileInput.current?.click()}>
            {t("loadFile")}
          </Button>
          <input ref={fileInput} type="file" accept="application/json,.json" aria-label={t("loadFile")} hidden onChange={load} />
          <p className="text-sm text-muted-foreground">{t("loadFileHint")}</p>
          {unreadableFile && (
            <p role="alert" className="w-full text-sm text-destructive">
              {t("fileUnreadable")}
            </p>
          )}
        </div>

        <form noValidate onSubmit={submit} className="grid gap-6">
          <div className="grid gap-4 sm:grid-cols-3">
            <Field path={PATHS.taxYear} label={t("taxYear")} error={shown[PATHS.taxYear]}>
              {(aria) => (
                <NativeSelect {...aria} value={values.taxYear} onChange={(event) => set({ taxYear: event.target.value })}>
                  {taxYears.map((year) => (
                    <NativeSelectOption key={year.taxYear} value={String(year.taxYear)}>
                      {year.taxYear}
                    </NativeSelectOption>
                  ))}
                </NativeSelect>
              )}
            </Field>
            <Field path={PATHS.asOf} label={t("asOf")} error={shown[PATHS.asOf]}>
              {(aria) => (
                <NativeSelect {...aria} value={values.asOf} onChange={(event) => set({ asOf: event.target.value as EstimateValues["asOf"] })}>
                  {QUARTERS.map((quarter) => (
                    <NativeSelectOption key={quarter} value={quarter}>
                      {quarter}
                    </NativeSelectOption>
                  ))}
                </NativeSelect>
              )}
            </Field>
            <Field path={PATHS.region} label={t("region")} error={shown[PATHS.region]}>
              {(aria) => (
                <NativeSelect {...aria} value={values.region} onChange={(event) => set({ region: event.target.value })}>
                  {!regions.some((region) => region.code === values.region) && (
                    <NativeSelectOption value={values.region}>{values.region}</NativeSelectOption>
                  )}
                  {regions.map((region) => (
                    <NativeSelectOption key={region.code} value={region.code}>
                      {region.name}
                    </NativeSelectOption>
                  ))}
                </NativeSelect>
              )}
            </Field>
          </div>

          <Group legend={t("employment")}>
            {amount(PATHS.employmentIngresos, t("employmentIngresos"), values.employmentIngresos, (v) => set({ employmentIngresos: v }))}
            {amount(PATHS.employmentSeguridadSocial, t("employmentSeguridadSocial"), values.employmentSeguridadSocial, (v) =>
              set({ employmentSeguridadSocial: v }),
            )}
          </Group>

          <Group legend={t("registration")}>
            <Field path={PATHS.alta} label={t("alta")} error={shown[PATHS.alta]}>
              {(aria) => <Input {...aria} type="date" value={values.alta} onChange={(event) => set({ alta: event.target.value })} />}
            </Field>
            <Choice
              legend={t("previousYear")}
              name="previousYear"
              value={values.previousYear}
              options={[
                ["noActivity", t("previousYearNone")],
                ["rendimientoNeto", t("previousYearNet")],
              ]}
              onChange={(previousYear) => set({ previousYear })}
            />
            {values.previousYear === "rendimientoNeto" &&
              amount(PATHS.previousYearNet, t("previousYearNetAmount"), values.previousYearNet, (v) => set({ previousYearNet: v }))}
            <Choice
              legend={t("newActivity")}
              name="newActivity"
              value={values.newActivity}
              options={[
                ["established", t("established")],
                ["first", t("first")],
                ["following", t("following")],
              ]}
              onChange={(newActivity) => set({ newActivity })}
            />
            {values.newActivity !== "established" &&
              amount(PATHS.ingresosFromFormerEmployer, t("ingresosFromFormerEmployer"), values.ingresosFromFormerEmployer, (v) =>
                set({ ingresosFromFormerEmployer: v }),
              )}
            <p className="text-sm text-muted-foreground sm:col-span-2">{t("retenciones")}</p>
          </Group>

          <Group legend={t("actuals")} hint={t("actualsHint")}>
            {values.actuals.map((actual, index) => (
              <div key={index} className="grid gap-3 rounded-lg border border-border p-3 sm:col-span-2 sm:grid-cols-5 sm:items-end">
                <Field path={`$.activity.actuals[${index}].quarter`} label={t("quarter")} error={shown[`$.activity.actuals[${index}].quarter`]}>
                  {(aria) => (
                    <NativeSelect {...aria} value={actual.quarter} onChange={(event) => setActual(index, { quarter: event.target.value as Actual["quarter"] })}>
                      {QUARTERS.map((quarter) => (
                        <NativeSelectOption key={quarter} value={quarter}>
                          {quarter}
                        </NativeSelectOption>
                      ))}
                    </NativeSelect>
                  )}
                </Field>
                {amount(PATHS.actual(index, "ingresosYtd"), t("ingresosYtd"), actual.ingresosYtd, (v) => setActual(index, { ingresosYtd: v }))}
                {amount(PATHS.actual(index, "gastosYtd"), t("gastosYtd"), actual.gastosYtd, (v) => setActual(index, { gastosYtd: v }))}
                {amount(PATHS.actual(index, "cuotasSsYtd"), t("cuotasSsYtd"), actual.cuotasSsYtd, (v) => setActual(index, { cuotasSsYtd: v }))}
                <Button variant="outline" onClick={() => set({ actuals: values.actuals.filter((_, at) => at !== index) })}>
                  {t("removeQuarter", { quarter: actual.quarter })}
                </Button>
              </div>
            ))}
            <div className="sm:col-span-2">
              <Button variant="outline" disabled={values.actuals.length >= QUARTERS.length} onClick={() => set({ actuals: [...values.actuals, emptyActual(values.actuals)] })}>
                {t("addQuarter")}
              </Button>
            </div>
          </Group>

          <Group legend={t("projection")}>
            {amount(PATHS.projectionIngresos, t("projectionIngresos"), values.projectionIngresos, (v) => set({ projectionIngresos: v }))}
            {amount(PATHS.projectionGastos, t("projectionGastos"), values.projectionGastos, (v) => set({ projectionGastos: v }))}
            {amount(PATHS.baseCotizacion, t("baseCotizacion"), values.baseCotizacion, (v) => set({ baseCotizacion: v }))}
          </Group>

          <div>
            <Button type="submit" disabled={pending}>
              {pending ? t("calculating") : t("submit")}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

type Aria = { id: string; "aria-invalid": boolean; "aria-describedby": string | undefined };

function Field({ path, label, error, children }: { path: string; label: string; error: FieldError | undefined; children: (aria: Aria) => ReactNode }) {
  const t = useTranslations("Dashboard.errors");
  const id = fieldId(path);
  const errorId = `${id}-error`;

  return (
    <div className="grid content-start gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      {children({ id, "aria-invalid": error !== undefined, "aria-describedby": error ? errorId : undefined })}
      {error && (
        <p id={errorId} className="text-sm text-destructive">
          {"key" in error ? t(error.key) : t("fromApi", { reason: error.reason })}
        </p>
      )}
    </div>
  );
}

function Group({ legend, hint, children }: { legend: string; hint?: string; children: ReactNode }) {
  return (
    <fieldset className="grid gap-4 sm:grid-cols-2">
      <legend className="mb-2 font-medium">{legend}</legend>
      {hint && <p className="text-sm text-muted-foreground sm:col-span-2">{hint}</p>}
      {children}
    </fieldset>
  );
}

function Choice<T extends string>({
  legend,
  name,
  value,
  options,
  onChange,
}: {
  legend: string;
  name: string;
  value: T;
  options: readonly (readonly [T, string])[];
  onChange: (value: T) => void;
}) {
  return (
    <fieldset className="grid gap-1.5">
      <legend className="text-sm font-medium">{legend}</legend>
      {options.map(([option, label]) => (
        <label key={option} className="flex items-center gap-2 text-sm">
          <input type="radio" name={name} value={option} checked={value === option} onChange={() => onChange(option)} className="accent-primary" />
          {label}
        </label>
      ))}
    </fieldset>
  );
}

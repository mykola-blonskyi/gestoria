"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";

import type { ProfileInput } from "@/data/profiles";
import type { TaxYear } from "@/data/tax-years";
import { Button } from "@/shared/ui/button";
import { Input } from "@/shared/ui/input";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";

import { PATHS, toInput, validate, type FieldError, type FieldErrors, type ProfileValues } from "./profile-values";

type Props = {
  taxYears: readonly TaxYear[];
  initial: ProfileValues;
  pending: boolean;
  // The API's refusals of the last save, by JSON path.
  refusals: Readonly<Record<string, string>>;
  onSubmit: (input: ProfileInput) => void;
};

const fieldId = (path: string) => `profile${path.replace(/[^a-zA-Z0-9]+/g, "-")}`;

export function ProfileForm({ taxYears, initial, pending, refusals, onSubmit }: Props) {
  const t = useTranslations("Settings.profile");
  const [values, setValues] = useState(initial);
  const [errors, setErrors] = useState<FieldErrors>({});

  const year = taxYears.find((candidate) => String(candidate.taxYear) === values.taxYear);
  const regions = year?.regions ?? [];
  const shown: FieldErrors = {
    ...Object.fromEntries(Object.entries(refusals).map(([path, reason]) => [path, { reason }])),
    ...errors,
  };
  const set = (patch: Partial<ProfileValues>) => setValues((current) => ({ ...current, ...patch }));

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const found = validate(values);
    setErrors(found);
    const first = Object.keys(found)[0];
    if (first !== undefined) {
      document.getElementById(fieldId(first))?.focus();
      return;
    }
    onSubmit(toInput(values));
  };

  const amount = (path: string, label: string, value: string, onChange: (value: string) => void) => (
    <Field path={path} label={label} error={shown[path]}>
      {(aria) => <Input {...aria} inputMode="decimal" placeholder="0.00" value={value} onChange={(event) => onChange(event.target.value)} />}
    </Field>
  );

  return (
    <form noValidate onSubmit={submit} className="grid gap-6">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field path={PATHS.taxYear} label={t("taxYear")} error={shown[PATHS.taxYear]}>
          {(aria) => (
            <NativeSelect {...aria} value={values.taxYear} onChange={(event) => set({ taxYear: event.target.value })}>
              {taxYears.map((option) => (
                <NativeSelectOption key={option.taxYear} value={String(option.taxYear)}>
                  {option.taxYear}
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
        {year !== undefined && year.gaps.length > 0 && (
          <div className="grid gap-1 text-sm sm:col-span-2">
            <p className="font-medium">{t("gaps", { year: year.taxYear })}</p>
            <ul className="grid list-disc gap-1 ps-5 text-muted-foreground">
              {year.gaps.map((gap) => (
                <li key={gap.entry}>
                  <code>{gap.entry}</code>: <span lang="en">{gap.note}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
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

      <Group legend={t("projection")} hint={t("projectionHint")}>
        {amount(PATHS.projectionIngresos, t("projectionIngresos"), values.projectionIngresos, (v) => set({ projectionIngresos: v }))}
        {amount(PATHS.projectionGastos, t("projectionGastos"), values.projectionGastos, (v) => set({ projectionGastos: v }))}
        {amount(PATHS.baseCotizacion, t("baseCotizacion"), values.baseCotizacion, (v) => set({ baseCotizacion: v }))}
      </Group>

      <div>
        <Button type="submit" disabled={pending}>
          {pending ? t("saving") : t("submit")}
        </Button>
      </div>
    </form>
  );
}

type Aria = { id: string; "aria-invalid": boolean; "aria-describedby": string | undefined };

function Field({ path, label, error, children }: { path: string; label: string; error: FieldError | undefined; children: (aria: Aria) => ReactNode }) {
  const t = useTranslations("Settings.profile.errors");
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

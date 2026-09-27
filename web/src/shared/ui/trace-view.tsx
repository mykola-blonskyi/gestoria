"use client";

import { useTranslations } from "next-intl";
import { Fragment } from "react";

import type { Locale } from "@/shared/constants/locales";
import { formatDate, formatMoney, formatShare } from "@/shared/lib/format";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { VirtualList } from "@/shared/ui/virtual-list";

// The section names of a TraceStepView (GestorIA.Api's TraceSection), kept as a local literal union — not imported from
// data/set-aside — so this shared component never imports the data layer (web/README.md's layer rules). The literal set
// matches the API's exactly, and messages/*.json's Trace.sections.* is checked against it (tests/structure.test.ts /
// messages.test.ts's key-parity check), so a mismatch would be caught by a locale, not silently swallowed.
export type TraceSection =
  | "Trabajo"
  | "Actividad"
  | "Ahorro"
  | "Inmuebles"
  | "Bases"
  | "Minimo"
  | "Cuota"
  | "Deducciones"
  | "Resultado"
  | "SeguridadSocial"
  | "Modelo130";

// The shape of a TraceStepView (GestorIA.Api), kept structural here so this shared component never imports the data layer:
// a feature's own TraceStepView value satisfies this without conversion.
export type TraceStep = {
  id: string;
  section: TraceSection;
  title: string;
  inputs: readonly { name: string; value: string }[];
  formula: string;
  output: { kind: "money" | "rate" | "date" | "count"; value: string };
  reference: string;
};

// A section with more steps than this renders in a VirtualList instead of a plain <ol>: the G12 quarter fixture keeps
// every section under a dozen steps, but the set-aside estimate's Modelo130 section already has 45, and jsdom-free
// browsers would otherwise lay out and paint every one of them inside an open <details>. Below the threshold the plain
// list is simplest and keeps today's DOM unchanged.
const VIRTUALIZE_ABOVE = 12;
const ROW_HEIGHT = 180;

export function Trace({ steps, locale }: { steps: readonly TraceStep[]; locale: Locale }) {
  const t = useTranslations("Trace");
  const sections = Map.groupBy(steps, (step) => step.section);

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h3>{t("heading")}</h3>
        </CardTitle>
        <p className="text-sm text-muted-foreground">{t("lead", { count: steps.length })}</p>
      </CardHeader>
      <CardContent className="grid gap-2">
        {[...sections].map(([section, inSection]) => (
          <details key={section} className="rounded-lg border border-border p-3">
            <summary className="cursor-pointer font-medium">
              {t("section", { name: t(`sections.${section satisfies TraceSection}`), count: inSection.length })}
            </summary>
            {inSection.length > VIRTUALIZE_ABOVE ? (
              <VirtualList
                className="mt-3"
                items={inSection}
                label={t("section", { name: t(`sections.${section satisfies TraceSection}`), count: inSection.length })}
                rowHeight={ROW_HEIGHT}
                getKey={(step) => step.id}
                renderRow={(step) => (
                  <div className="grid gap-1 p-3 text-sm">
                    <TraceStepFields step={step} locale={locale} t={t} />
                  </div>
                )}
              />
            ) : (
              <ol className="mt-3 grid gap-4">
                {inSection.map((step) => (
                  <li key={step.id} className="grid gap-1 text-sm">
                    <TraceStepFields step={step} locale={locale} t={t} />
                  </li>
                ))}
              </ol>
            )}
          </details>
        ))}
      </CardContent>
    </Card>
  );
}

function TraceStepFields({ step, locale, t }: { step: TraceStep; locale: Locale; t: ReturnType<typeof useTranslations> }) {
  return (
    <Fragment>
      <p className="font-medium">
        <span lang="es">{step.title}</span> <code className="text-muted-foreground">{step.id}</code>
      </p>
      {step.inputs.length > 0 && (
        <p>
          {t("inputs")}: {step.inputs.map((input) => `${input.name} = ${input.value}`).join(", ")}
        </p>
      )}
      <p>
        {t("formula")}: <code className="break-words">{step.formula}</code>
      </p>
      <p className="font-medium">
        {t("result")}: {display(step.output, locale)}
      </p>
      <p lang="en" className="text-muted-foreground">
        {t("source")}: {step.reference}
      </p>
    </Fragment>
  );
}

function display(output: TraceStep["output"], locale: Locale): string {
  switch (output.kind) {
    case "money":
      return formatMoney(output.value, locale);
    case "rate":
      return formatShare(output.value, locale);
    case "date":
      return formatDate(output.value, locale);
    case "count":
      return output.value;
  }
}

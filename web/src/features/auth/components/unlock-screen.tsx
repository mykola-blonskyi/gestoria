"use client";

import { useTranslations } from "next-intl";
import { useId, useState, type FormEvent, type ReactNode } from "react";

import { ApiError, isDatabaseUnavailable } from "@/data/api-error";
import { Button } from "@/shared/ui/button";
import { Input } from "@/shared/ui/input";
import { PageHeader } from "@/shared/ui/page-header";

import { useUnlock } from "../hooks/use-api-key";

const START_API_COMMAND = "dotnet run --project src/GestorIA.Api";
const START_DATABASE_COMMAND = "docker compose up -d postgres";

export function UnlockScreen({ refused }: { refused: boolean }) {
  const t = useTranslations("Auth.unlock");
  const unlock = useUnlock();
  const [key, setKey] = useState("");
  const [unsendable, setUnsendable] = useState(false);
  const inputId = useId();

  // A header carries only visible ASCII, which is what `openssl rand -hex` makes. A key typed on a Cyrillic layout
  // could not be sent at all, and surrounding spaces would be stripped from the header but not from the stored key.
  function submit(event: FormEvent) {
    event.preventDefault();
    const candidate = key.trim();
    const sendable = /^[\x21-\x7E]+$/.test(candidate);
    setUnsendable(!sendable);
    if (sendable) unlock.mutate(candidate);
  }

  return (
    <>
      <PageHeader title={t("title")} lead={t("lead")} />
      <div className="grid max-w-md gap-4">
        {refused && !unlock.isError && !unsendable && <Alert title={t("refused")} />}
        {/* The input has no name, so a submit before the page is interactive sends nothing in the address. */}
        <form noValidate onSubmit={submit} className="grid gap-3">
          <label htmlFor={inputId} className="text-sm font-medium">
            {t("key")}
          </label>
          <Input
            id={inputId}
            type="password"
            autoComplete="off"
            spellCheck={false}
            value={key}
            onChange={(event) => setKey(event.target.value)}
            aria-invalid={unlock.isError || unsendable}
          />
          <Button type="submit" disabled={unlock.isPending || key.trim() === ""} className="justify-self-start">
            {unlock.isPending ? t("checking") : t("submit")}
          </Button>
        </form>
        {unsendable ? <Alert title={t("unsendable")} /> : unlock.isError && <UnlockFailure error={unlock.error} />}
      </div>
    </>
  );
}

// Never rethrown or logged: the problem's detail is shown, and nothing about the key is (SPEC-013).
function UnlockFailure({ error }: { error: Error }) {
  const t = useTranslations("Auth.unlock");
  const failure = error instanceof ApiError ? error.failure : null;
  const status = failure?.kind === "problem" ? failure.problem.status : failure?.kind === "http" ? failure.status : 0;

  if (failure?.kind === "network") return <StartIt title={t("unreachable")} next={t("unreachableNext")} command={START_API_COMMAND} />;
  if (isDatabaseUnavailable(error)) return <StartIt title={t("database")} next={t("databaseNext")} command={START_DATABASE_COMMAND} />;
  return <Alert title={status === 401 ? t("wrongKey") : t("other", { status })} />;
}

function StartIt({ title, next, command }: { title: string; next: string; command: string }) {
  return (
    <Alert title={title}>
      <p className="mt-1 text-sm">{next}</p>
      <pre className="mt-2 overflow-x-auto rounded-md bg-muted p-2 text-sm">
        <code>{command}</code>
      </pre>
    </Alert>
  );
}

function Alert({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div role="alert" className="rounded-xl border border-destructive bg-background p-4">
      <p className="font-medium text-destructive">{title}</p>
      {children}
    </div>
  );
}

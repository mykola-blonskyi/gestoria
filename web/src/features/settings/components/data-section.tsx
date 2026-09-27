"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useId, useState } from "react";

import { ApiError } from "@/data/api-error";
import type { Profile } from "@/data/profiles";
import { Button } from "@/shared/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/card";
import { Input } from "@/shared/ui/input";

import { useDeleteProfile, useProfile } from "../hooks/use-profile";

const BACKUP = "/backup";

// Export and delete (#74). The download lives in the backup feature; deleting everything lives here, behind a typed
// confirmation that lists what goes (SPEC-013).
export function DataSection() {
  const t = useTranslations("Settings.data");
  const profile = useProfile();
  const remove = useDeleteProfile();

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>{t("heading")}</h2>
        </CardTitle>
        <CardDescription>
          {t("export")}{" "}
          <Link href={BACKUP} className="font-medium underline underline-offset-4">
            {t("backup")}
          </Link>
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        {/* A profile that cannot be read is already said so by the profile section above, once. */}
        {profile.isPending ? (
          <p role="status">{t("loading")}</p>
        ) : profile.isError ? null : profile.data === null ? (
          remove.isSuccess ? (
            <p role="status" className="text-sm font-medium">
              {t("deleted")}
            </p>
          ) : (
            <p className="text-sm">{t("nothing")}</p>
          )
        ) : (
          <DeleteAll profile={profile.data} pending={remove.isPending} onConfirm={remove.mutate} />
        )}
        {remove.isError && <Failure error={remove.error} />}
      </CardContent>
    </Card>
  );
}

function DeleteAll({ profile, pending, onConfirm }: { profile: Profile; pending: boolean; onConfirm: (id: string) => void }) {
  const t = useTranslations("Settings.data.delete");
  const [typed, setTyped] = useState("");
  const confirmId = useId();
  const listId = useId();
  const word = t("word");
  const confirmed = typed.trim() === word;

  return (
    <form
      className="grid gap-3 rounded-xl border border-destructive p-4"
      aria-labelledby={`${confirmId}-heading`}
      onSubmit={(event) => {
        event.preventDefault();
        if (confirmed && !pending) onConfirm(profile.id);
      }}
    >
      <h3 id={`${confirmId}-heading`} className="font-medium text-destructive">
        {t("heading")}
      </h3>
      <div>
        <p id={listId} className="text-sm">
          {t("what")}
        </p>
        <ul className="mt-1 list-disc pl-5 text-sm">
          <li>{t("profile", { year: profile.taxYear, region: profile.region })}</li>
        </ul>
      </div>
      <p className="text-sm">{t("irreversible")}</p>
      <p className="text-sm">{t("retention")}</p>
      <div className="grid gap-1.5">
        <label htmlFor={confirmId} className="text-sm font-medium">
          {t("confirm", { word })}
        </label>
        <Input
          id={confirmId}
          value={typed}
          autoComplete="off"
          spellCheck={false}
          aria-describedby={listId}
          onChange={(event) => setTyped(event.target.value)}
          className="max-w-xs"
        />
      </div>
      <div>
        <Button type="submit" variant="destructive" disabled={!confirmed || pending}>
          {pending ? t("deleting") : t("submit")}
        </Button>
      </div>
    </form>
  );
}

// What went wrong, in words. Never rethrown or logged (SPEC-013).
function Failure({ error }: { error: Error }) {
  const t = useTranslations("Settings.data.failure");
  const failure = error instanceof ApiError ? error.failure : null;
  const status = failure?.kind === "problem" ? failure.problem.status : failure?.kind === "http" ? failure.status : 0;

  return (
    <p role="alert" className="rounded-xl border border-destructive p-4 font-medium text-destructive">
      {failure?.kind === "network" ? t("network") : t("other", { status })}
    </p>
  );
}

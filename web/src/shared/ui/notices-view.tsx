"use client";

import { useTranslations } from "next-intl";

import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";

// The shape of a NoticeView (GestorIA.Api), kept structural here so this shared component never imports the data layer
// (web/README.md's layer rules): a feature's own NoticeView value satisfies this without conversion.
export type Notice = {
  code: string;
  severity: "Error" | "Warning" | "Info";
  text: string;
};

const SEVERITY_RANK = { Error: 2, Warning: 1, Info: 0 } as const;

// Warnings first, as the console prints them; Array.prototype.sort is stable, so the engine's order holds within a severity.
export function Notices({ notices }: { notices: readonly Notice[] }) {
  const t = useTranslations("Notices");
  const ordered = [...notices].sort((a, b) => SEVERITY_RANK[b.severity] - SEVERITY_RANK[a.severity]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h3>{t("heading")}</h3>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <ul className="grid gap-3">
          {ordered.map((notice, index) => (
            <li key={index} className={notice.severity === "Info" ? "" : "border-s-4 border-destructive ps-3"}>
              <p className="text-sm font-medium">
                {t(notice.severity)} · <code>{notice.code}</code>
              </p>
              <p lang="en" className="text-sm">
                {notice.text}
              </p>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}

import { useTranslations } from "next-intl";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/card";

export function EmptyState({ points, pending }: { points: readonly string[]; pending: string }) {
  const t = useTranslations("EmptyState");

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>{t("heading")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <ul className="list-disc space-y-2 ps-5">
          {points.map((point) => (
            <li key={point}>{point}</li>
          ))}
        </ul>
      </CardContent>
      <CardContent>
        <p role="status" className="font-medium">
          {t("status")}
        </p>
        <CardDescription>{pending}</CardDescription>
      </CardContent>
    </Card>
  );
}

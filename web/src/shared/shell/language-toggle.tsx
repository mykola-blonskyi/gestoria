"use client";

import { useLocale, useTranslations } from "next-intl";
import { useRouter } from "next/navigation";
import { useOptimistic, useTransition } from "react";

import { LOCALES, LOCALE_COOKIE, LOCALE_NAMES, isLocale } from "@/shared/constants/locales";
import { writePreferenceCookie } from "@/shared/lib/cookies";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";

export function LanguageToggle() {
  const t = useTranslations("Shell.language");
  const router = useRouter();
  const [shownLocale, setShownLocale] = useOptimistic(useLocale());
  const [, startTransition] = useTransition();

  return (
    <label className="flex items-center gap-2 text-sm">
      <span>{t("label")}</span>
      <NativeSelect
        value={shownLocale}
        onChange={(event) => {
          const next = event.target.value;
          if (!isLocale(next)) return;
          writePreferenceCookie(LOCALE_COOKIE, next);
          startTransition(() => {
            setShownLocale(next);
            router.refresh();
          });
        }}
      >
        {LOCALES.map((locale) => (
          <NativeSelectOption key={locale} value={locale} lang={locale}>
            {LOCALE_NAMES[locale]}
          </NativeSelectOption>
        ))}
      </NativeSelect>
    </label>
  );
}

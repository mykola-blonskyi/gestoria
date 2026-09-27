"use client";

import { useTranslations } from "next-intl";

import { useTheme } from "@/shared/theme/theme-provider";
import { THEMES, isTheme } from "@/shared/theme/themes";
import { NativeSelect, NativeSelectOption } from "@/shared/ui/native-select";

export function ThemeToggle() {
  const t = useTranslations("Shell.theme");
  const { theme, setTheme } = useTheme();

  return (
    <label className="flex items-center gap-2 text-sm">
      <span>{t("label")}</span>
      <NativeSelect
        value={theme}
        onChange={(event) => {
          const next = event.target.value;
          if (isTheme(next)) setTheme(next);
        }}
      >
        {THEMES.map((option) => (
          <NativeSelectOption key={option} value={option}>
            {t(option)}
          </NativeSelectOption>
        ))}
      </NativeSelect>
    </label>
  );
}

import { render, type RenderResult } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import type { ReactElement } from "react";

import { QueryProvider } from "@/data/query-provider";
import en from "@/i18n/messages/en.json";
import es from "@/i18n/messages/es.json";
import ru from "@/i18n/messages/ru.json";
import uk from "@/i18n/messages/uk.json";
import type { Locale } from "@/shared/constants/locales";
import { ThemeProvider } from "@/shared/theme/theme-provider";
import type { Theme } from "@/shared/theme/themes";

export const MESSAGES = { uk, es, en, ru } as const satisfies Record<Locale, unknown>;

export function renderInApp(
  ui: ReactElement,
  { locale = "uk", theme = "light" }: { locale?: Locale; theme?: Theme } = {},
): RenderResult {
  return render(
    <NextIntlClientProvider locale={locale} messages={MESSAGES[locale]} timeZone="Europe/Madrid">
      <ThemeProvider initialTheme={theme}>
        <QueryProvider>{ui}</QueryProvider>
      </ThemeProvider>
    </NextIntlClientProvider>,
  );
}

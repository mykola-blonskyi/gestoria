import type { Metadata } from "next";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getTranslations } from "next-intl/server";
import { cookies } from "next/headers";

import { QueryProvider } from "@/data/query-provider";
import { AppShell } from "@/shared/shell/app-shell";
import { ThemeProvider } from "@/shared/theme/theme-provider";
import { THEME_COOKIE, resolveTheme } from "@/shared/theme/themes";

import "./globals.css";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Metadata");
  return {
    title: { template: "%s · GestorIA", default: "GestorIA" },
    description: t("description"),
  };
}

// The theme cookie is read on the server and rendered into <html>,
// so the first paint already has the right colours.
export default async function RootLayout({ children }: LayoutProps<"/">) {
  const locale = await getLocale();
  const theme = resolveTheme((await cookies()).get(THEME_COOKIE)?.value);

  return (
    <html lang={locale} data-theme={theme}>
      <body>
        <NextIntlClientProvider>
          <ThemeProvider initialTheme={theme}>
            <QueryProvider>
              <AppShell>{children}</AppShell>
            </QueryProvider>
          </ThemeProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}

import { useTranslations } from "next-intl";
import Link from "next/link";
import type { ReactNode } from "react";

import { LanguageToggle } from "@/shared/shell/language-toggle";
import { MainNav } from "@/shared/shell/main-nav";
import { ThemeToggle } from "@/shared/theme/theme-toggle";

export function AppShell({ children }: { children: ReactNode }) {
  const t = useTranslations("Shell");

  return (
    <>
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-primary text-primary-foreground focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:px-4 focus:py-2"
      >
        {t("skipToContent")}
      </a>
      <header className="border-b border-border bg-card text-card-foreground">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3 px-4 py-3">
          <Link href="/" className="rounded-md text-lg font-semibold">
            GestorIA
          </Link>
          <div className="flex flex-wrap items-center gap-4">
            <ThemeToggle />
            <LanguageToggle />
          </div>
        </div>
        <div className="mx-auto max-w-5xl px-4 pb-2">
          <MainNav />
        </div>
      </header>
      <p role="note" className="border-b border-border bg-muted px-4 py-2 text-center text-sm text-muted-foreground">
        {t("disclaimer")}
      </p>
      <main id="main" tabIndex={-1} className="mx-auto max-w-5xl px-4 py-8">
        {children}
      </main>
    </>
  );
}

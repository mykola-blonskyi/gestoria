"use client";

import { useTranslations } from "next-intl";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { NAV_ITEMS } from "@/shared/constants/routes";
import { cn } from "@/shared/lib/utils";

export function MainNav() {
  const t = useTranslations("Shell.nav");
  const pathname = usePathname();

  return (
    <nav aria-label={t("label")}>
      <ul className="flex flex-wrap gap-1">
        {NAV_ITEMS.map((item) => {
          const isCurrent = item.href === pathname;
          return (
            <li key={item.key}>
              <Link
                href={item.href}
                aria-current={isCurrent ? "page" : undefined}
                className={cn(
                  "inline-block rounded-md px-3 py-1.5 text-sm hover:bg-muted hover:text-foreground",
                  isCurrent ? "bg-secondary font-semibold text-secondary-foreground" : "text-muted-foreground",
                )}
              >
                {t(item.key)}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

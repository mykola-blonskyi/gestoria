"use client";

import { createContext, useContext, useState, type ReactNode } from "react";

import { writePreferenceCookie } from "@/shared/lib/cookies";
import { THEME_COOKIE, type Theme } from "@/shared/theme/themes";

type ThemeContextValue = {
  theme: Theme;
  setTheme: (theme: Theme) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

export function ThemeProvider({ initialTheme, children }: { initialTheme: Theme; children: ReactNode }) {
  const [theme, setThemeState] = useState(initialTheme);

  function setTheme(next: Theme) {
    document.documentElement.dataset.theme = next;
    writePreferenceCookie(THEME_COOKIE, next);
    setThemeState(next);
  }

  return <ThemeContext value={{ theme, setTheme }}>{children}</ThemeContext>;
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (context === null) {
    throw new Error("useTheme must be used inside ThemeProvider");
  }
  return context;
}

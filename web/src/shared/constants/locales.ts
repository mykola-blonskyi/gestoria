export const LOCALES = ["uk", "es", "en", "ru"] as const;

export type Locale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: Locale = "uk";

export const LOCALE_COOKIE = "NEXT_LOCALE";

export const LOCALE_NAMES: Record<Locale, string> = {
  uk: "Українська",
  es: "Español",
  en: "English",
  ru: "Русский",
};

export function isLocale(value: string): value is Locale {
  return (LOCALES as readonly string[]).includes(value);
}

export function resolveLocale(cookieValue: string | undefined): Locale {
  return cookieValue !== undefined && isLocale(cookieValue) ? cookieValue : DEFAULT_LOCALE;
}

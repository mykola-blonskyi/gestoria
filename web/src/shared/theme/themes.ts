export const THEMES = ["light", "dark", "sepia", "contrast", "ocean"] as const;

export type Theme = (typeof THEMES)[number];

export const DEFAULT_THEME: Theme = "light";

export const THEME_COOKIE = "theme";

export function isTheme(value: string): value is Theme {
  return (THEMES as readonly string[]).includes(value);
}

export function resolveTheme(cookieValue: string | undefined): Theme {
  return cookieValue !== undefined && isTheme(cookieValue) ? cookieValue : DEFAULT_THEME;
}

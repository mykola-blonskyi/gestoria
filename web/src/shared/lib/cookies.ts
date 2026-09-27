const ONE_YEAR_SECONDS = 60 * 60 * 24 * 365;

export function writePreferenceCookie(name: string, value: string): void {
  document.cookie = `${name}=${encodeURIComponent(value)}; path=/; max-age=${ONE_YEAR_SECONDS}; samesite=lax`;
}

export const NAV_ITEMS = [
  { key: "dashboard", href: "/" },
  { key: "payments", href: "/payments" },
  { key: "transactions", href: "/transactions" },
  { key: "periods", href: "/periods" },
  { key: "settings", href: "/settings" },
  { key: "backup", href: "/backup" },
  { key: "auth", href: "/auth" },
] as const;

export type NavKey = (typeof NAV_ITEMS)[number]["key"];

import type { Locale } from "@/shared/constants/locales";

import type messages from "./messages/uk.json";

declare module "next-intl" {
  interface AppConfig {
    Locale: Locale;
    Messages: typeof messages;
  }
}

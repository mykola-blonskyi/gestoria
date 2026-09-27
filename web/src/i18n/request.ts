import { getRequestConfig } from "next-intl/server";
import { cookies } from "next/headers";

import { LOCALE_COOKIE, resolveLocale } from "@/shared/constants/locales";

export default getRequestConfig(async () => {
  const locale = resolveLocale((await cookies()).get(LOCALE_COOKIE)?.value);

  return {
    locale,
    timeZone: "Europe/Madrid",
    messages: (await import(`./messages/${locale}.json`)).default,
  };
});

import type { ReactElement } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import RootLayout from "@/app/layout";
import requestConfig from "@/i18n/request";
import es from "@/i18n/messages/es.json";

const cookieJar = new Map<string, string>();

vi.mock("next/headers", () => ({
  cookies: async () => ({
    get: (name: string) => (cookieJar.has(name) ? { name, value: cookieJar.get(name) } : undefined),
  }),
}));

// Outside Next, next-intl/server resolves to its client build, which refuses to run.
// These stand-ins do what its server build does: getRequestConfig returns the function it is
// given, and getLocale reads the locale that src/i18n/request.ts resolves.
vi.mock("next-intl/server", () => ({
  getRequestConfig: (create: unknown) => create,
  getLocale: async () => (await resolveRequest()).locale,
}));

async function resolveRequest() {
  return requestConfig({ requestLocale: Promise.resolve(undefined) });
}

async function renderHtml(): Promise<ReactElement<Record<string, unknown>>> {
  return (await RootLayout({ children: null, params: Promise.resolve({}) })) as ReactElement<Record<string, unknown>>;
}

afterEach(() => {
  cookieJar.clear();
});

describe("the root layout renders <html> from the cookies", () => {
  it("in Ukrainian and the light theme when there are none", async () => {
    const html = await renderHtml();

    expect(html.type).toBe("html");
    expect(html.props.lang).toBe("uk");
    expect(html.props["data-theme"]).toBe("light");
  });

  it("in the cookie's language and theme, so the first paint is already right", async () => {
    cookieJar.set("NEXT_LOCALE", "es");
    cookieJar.set("theme", "ocean");

    const html = await renderHtml();

    expect(html.props.lang).toBe("es");
    expect(html.props["data-theme"]).toBe("ocean");
    expect((await resolveRequest()).messages).toEqual(es);
  });

  it("ignoring cookies it does not recognise", async () => {
    cookieJar.set("NEXT_LOCALE", "de");
    cookieJar.set("theme", "<script>");

    const html = await renderHtml();

    expect(html.props.lang).toBe("uk");
    expect(html.props["data-theme"]).toBe("light");
  });
});

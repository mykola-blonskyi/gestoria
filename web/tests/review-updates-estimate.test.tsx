import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import g12Estimate from "@tests/fixtures/g12-estimate.json";
import g12Ledger from "@tests/fixtures/g12-estimate-ledger.json";
import g12Profile from "@tests/fixtures/g12-profile.json";
import g12Queue from "@tests/fixtures/g12-review-queue.json";
import g12Year from "@tests/fixtures/g12-transactions-2025.json";
import { renderInApp } from "@tests/render";

import { DashboardPage } from "@/features/dashboard";
import { TransactionsPage } from "@/features/transactions";

// The overview and the transactions page share one query cache in the app, so this renders both in one tree. The API's
// estimate before and after classifying come from tests/GestorIA.Api.Tests/WebFixtures.cs.
function stubApi() {
  let classified = false;
  const fetchStub = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async (url) => {
    if (url.endsWith("/classify")) {
      classified = true;
      return new Response(null, { status: 204 });
    }
    const body = url.includes("/set-aside/estimate")
      ? classified
        ? g12Ledger
        : g12Estimate
      : url.endsWith("/review-queue")
        ? g12Queue
        : url.includes("/transactions")
          ? g12Year
          : [g12Profile];
    return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
  });
  vi.stubGlobal("fetch", fetchStub);
  return fetchStub;
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2025-05-20T12:00:00Z"));
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue(DOMRect.fromRect({ width: 800, height: 400 }));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it("resolving a movement in the review queue updates the overview's estimate", async () => {
  const fetchStub = stubApi();
  const user = userEvent.setup();
  renderInApp(
    <>
      <DashboardPage />
      <TransactionsPage />
    </>,
    { locale: "en" },
  );
  const estimateCalls = () => fetchStub.mock.calls.filter(([url]) => url.includes("/set-aside/estimate"));
  await screen.findByText(/with no closed quarter recorded/);
  await screen.findByRole("heading", { name: "9 movements to review" });
  expect(estimateCalls()).toHaveLength(1);

  const [first] = within(screen.getByRole("list", { name: "9 movements to review" })).getAllByRole("listitem");
  await user.click(within(first!).getByRole("button", { name: "Income from my activity" }));

  expect(
    await screen.findByText("Estimated for 2025 from actuals through Q2, 3 classified movements; the projection covers the rest of the year.", {
      exact: false,
    }),
  ).toBeInTheDocument();
  await waitFor(() => expect(estimateCalls()).toHaveLength(2));
  expect(estimateCalls().map(([url]) => url)).toEqual([
    `http://localhost:5080/api/v1/profiles/${g12Profile.id}/set-aside/estimate?asOf=Q2`,
    `http://localhost:5080/api/v1/profiles/${g12Profile.id}/set-aside/estimate?asOf=Q2`,
  ]);
});

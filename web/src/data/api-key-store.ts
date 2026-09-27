// The local API key (SPEC-009 §3) lives in this module's memory and nowhere else: not in localStorage, sessionStorage,
// a cookie or the address (SPEC-013). A reload or a new tab starts locked. web/README.md ("The API key") says why.
export const API_KEY_HEADER = "X-Api-Key";

// "refused": the API answered 401 to a key that had unlocked the app, because the key was changed or the API restarted
// with another one.
export type ApiKeyState = "locked" | "unlocked" | "refused";

let key: string | null = null;
let state: ApiKeyState = "locked";
const listeners = new Set<() => void>();

function set(nextKey: string | null, nextState: ApiKeyState): void {
  key = nextKey;
  state = nextState;
  for (const listener of listeners) listener();
}

export const apiKeyStore = {
  current: (): string | null => key,
  state: (): ApiKeyState => state,
  subscribe: (listener: () => void): (() => void) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
  remember: (next: string): void => set(next, "unlocked"),
  forget: (): void => set(null, "locked"),
  // Only the key in use: a late answer to a request sent with an earlier key, or to a wrong key being tried, changes nothing.
  refused: (sent: string | null): void => {
    if (key !== null && sent === key) set(null, "refused");
  },
};

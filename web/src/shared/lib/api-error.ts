// RFC 9457 problem details, as the API returns them (SPEC-009 §1, §4).
export type ProblemDetails = {
  type: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  extensions: Record<string, unknown>;
};

// The problem types GestorIA.Api answers with (src/GestorIA.Api/Problems.cs).
export const PROBLEM_TYPES = {
  invalidInput: "https://gestoria.local/problems/invalid-input",
  configGap: "https://gestoria.local/problems/config-gap",
  estimateRefused: "https://gestoria.local/problems/estimate-refused",
  apiKeyRequired: "https://gestoria.local/problems/api-key-required",
  statementTooLarge: "https://gestoria.local/problems/statement-too-large",
  databaseUnavailable: "https://gestoria.local/problems/database-unavailable",
  noUpcomingObligations: "https://gestoria.local/problems/no-upcoming-obligations",
  profileNotFound: "https://gestoria.local/problems/profile-not-found",
  profileExists: "https://gestoria.local/problems/profile-exists",
  installationNotEmpty: "https://gestoria.local/problems/installation-not-empty",
  exportTooLarge: "https://gestoria.local/problems/export-too-large",
  exportMediaType: "https://gestoria.local/problems/export-media-type",
} as const;

// The API runs but PostgreSQL does not answer it: a 503 from /health/ready or from any endpoint that reads the database.
export function isDatabaseUnavailable(error: unknown): boolean {
  return error instanceof ApiError && error.failure.kind === "problem" && error.failure.problem.type === PROBLEM_TYPES.databaseUnavailable;
}

export type ApiFailure =
  | { kind: "problem"; problem: ProblemDetails }
  | { kind: "http"; status: number }
  // `reachable`: something answered a probe that needs no CORS permission, so the browser refused the call, not the network.
  | { kind: "network"; reachable: boolean };

// The message carries only the status and the problem type's title, never `detail`
// or extensions, which may hold amounts: an uncaught error reaches the console (SPEC-013).
export class ApiError extends Error {
  readonly failure: ApiFailure;

  constructor(failure: ApiFailure) {
    super(describe(failure));
    this.name = "ApiError";
    this.failure = failure;
  }
}

function describe(failure: ApiFailure): string {
  switch (failure.kind) {
    case "problem":
      return `API ${failure.problem.status}: ${failure.problem.title}`;
    case "http":
      return `API ${failure.status}`;
    case "network":
      return "API unreachable";
  }
}

const KNOWN_MEMBERS = new Set(["type", "title", "status", "detail", "instance"]);

export function parseProblem(body: unknown, responseStatus: number): ProblemDetails | null {
  if (typeof body !== "object" || body === null || Array.isArray(body)) return null;
  const record = body as Record<string, unknown>;
  const text = (key: string) => (typeof record[key] === "string" ? record[key] : undefined);

  return {
    type: text("type") ?? "about:blank",
    title: text("title") ?? "",
    status: typeof record.status === "number" ? record.status : responseStatus,
    detail: text("detail"),
    instance: text("instance"),
    extensions: Object.fromEntries(Object.entries(record).filter(([key]) => !KNOWN_MEMBERS.has(key))),
  };
}

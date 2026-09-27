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
} as const;

export type ApiFailure =
  | { kind: "problem"; problem: ProblemDetails }
  | { kind: "http"; status: number }
  | { kind: "network" };

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

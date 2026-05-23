const ANTIFORGERY_HEADER = "X-XSRF-TOKEN";
const ANTIFORGERY_COOKIE = "XSRF-TOKEN";
const MUTATING_METHODS = new Set(["POST", "PUT", "PATCH", "DELETE"]);

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly body: unknown,
  ) {
    super(`API request failed: ${status}`);
    this.name = "ApiError";
  }
}

function readCookie(name: string): string | undefined {
  const match = document.cookie.split("; ").find((row) => row.startsWith(`${name}=`));
  return match?.split("=")[1];
}

export interface ApiRequestOptions<TRequest> {
  method?: string;
  body?: TRequest extends void ? never : TRequest;
  signal?: AbortSignal;
}

export async function apiRequest<TRequest, TResponse>(
  path: string,
  options: ApiRequestOptions<TRequest> = {},
): Promise<TResponse> {
  const method = (options.method ?? "GET").toUpperCase();
  const headers = new Headers({
    Accept: "application/json",
  });

  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  if (MUTATING_METHODS.has(method)) {
    const token = readCookie(ANTIFORGERY_COOKIE);
    if (token) {
      headers.set(ANTIFORGERY_HEADER, decodeURIComponent(token));
    }
  }

  const response = await fetch(path, {
    method,
    headers,
    credentials: "include",
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    signal: options.signal,
  });

  const contentType = response.headers.get("content-type") ?? "";
  const isJson = contentType.includes("/json") || contentType.includes("+json");
  const payload = isJson ? await response.json() : await response.text();

  if (!response.ok) {
    throw new ApiError(response.status, payload);
  }

  return payload as TResponse;
}

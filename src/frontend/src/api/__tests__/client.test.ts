import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiRequest } from "@/api/client";

describe("apiRequest", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    document.cookie = "";
  });

  afterEach(() => {
    fetchMock.mockReset();
    vi.unstubAllGlobals();
  });

  it("includes credentials and parses JSON on 2xx", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );

    const result = await apiRequest<void, { ok: boolean }>("/api/things");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.credentials).toBe("include");
    expect(init.method).toBe("GET");
    expect(result).toEqual({ ok: true });
  });

  it("throws ApiError on non-2xx with parsed body", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ message: "no good" }), {
        status: 401,
        headers: { "content-type": "application/json" },
      }),
    );

    await expect(apiRequest("/api/me")).rejects.toMatchObject({
      name: "ApiError", // Instance of ApiError
      status: 401,
      body: { message: "no good" },
    });
  });

  it("sends X-XSRF-TOKEN on mutations when the antiforgery cookie is present", async () => {
    document.cookie = "XSRF-TOKEN=abc%20def";
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await apiRequest("/api/things", { method: "POST", body: { x: 1 } });

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get("X-XSRF-TOKEN")).toBe("abc def");
    expect(headers.get("Content-Type")).toBe("application/json");
    expect(init.body).toBe(JSON.stringify({ x: 1 }));
  });

  it("does not send X-XSRF-TOKEN on GET", async () => {
    document.cookie = "XSRF-TOKEN=abc";
    fetchMock.mockResolvedValue(
      new Response("{}", {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );

    await apiRequest("/api/things");

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get("X-XSRF-TOKEN")).toBeNull();
  });
});

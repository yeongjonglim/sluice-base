import { renderHook, act } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { useSessionState } from "@/utils/useSessionState";

beforeEach(() => sessionStorage.clear());

describe("useSessionState", () => {
  it("returns defaultValue when sessionStorage is empty", () => {
    const { result } = renderHook(() => useSessionState("k", "default"));
    expect(result.current[0]).toBe("default");
  });

  it("returns persisted value when key is already in sessionStorage", () => {
    sessionStorage.setItem("k", JSON.stringify("persisted"));
    const { result } = renderHook(() => useSessionState("k", "default"));
    expect(result.current[0]).toBe("persisted");
  });

  it("writes new value to sessionStorage when state changes", () => {
    const { result } = renderHook(() => useSessionState("k", "default"));
    act(() => result.current[1]("updated"));
    expect(JSON.parse(sessionStorage.getItem("k")!)).toBe("updated");
  });

  it("falls back to defaultValue when sessionStorage contains invalid JSON", () => {
    sessionStorage.setItem("k", "{{{not-json");
    const { result } = renderHook(() => useSessionState("k", "default"));
    expect(result.current[0]).toBe("default");
  });

  it("works with null as defaultValue", () => {
    const { result } = renderHook(() => useSessionState<string | null>("k", null));
    expect(result.current[0]).toBeNull();
  });

  it("works with array values", () => {
    const { result } = renderHook(() => useSessionState<string[]>("k", []));
    act(() => result.current[1](["a", "b"]));
    expect(JSON.parse(sessionStorage.getItem("k")!)).toEqual(["a", "b"]);
  });
});

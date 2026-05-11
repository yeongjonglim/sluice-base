import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import * as hooksModule from "../../api/hooks";
import { useHasPermission } from "../permission";

vi.mock("../../api/hooks", () => ({
  useMe: vi.fn(),
}));

const mockUseMe = vi.mocked(hooksModule.useMe);

afterEach(() => {
  vi.clearAllMocks();
});

describe("useHasPermission", () => {
  it("returns true when permission is in the permissions array", () => {
    mockUseMe.mockReturnValue({
      data: {
        id: "user-1",
        sub: "alice-sub",
        email: "alice@example.com",
        name: "Alice",
        permissions: ["permission:manage", "query:execute"],
      },
    } as ReturnType<typeof hooksModule.useMe>);

    const { result } = renderHook(() => useHasPermission("permission:manage"));

    expect(result.current).toBe(true);
  });

  it("returns false when permission is not in the array", () => {
    mockUseMe.mockReturnValue({
      data: {
        id: "user-2",
        sub: "bob-sub",
        email: "bob@example.com",
        name: "Bob",
        permissions: ["query:execute"],
      },
    } as ReturnType<typeof hooksModule.useMe>);

    const { result } = renderHook(() => useHasPermission("permission:manage"));

    expect(result.current).toBe(false);
  });

  it("returns false while useMe has no data (loading)", () => {
    mockUseMe.mockReturnValue({
      data: undefined,
    } as ReturnType<typeof hooksModule.useMe>);

    const { result } = renderHook(() => useHasPermission("permission:manage"));

    expect(result.current).toBe(false);
  });
});

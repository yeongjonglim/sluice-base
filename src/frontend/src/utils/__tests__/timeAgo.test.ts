import { afterEach, describe, expect, it, vi } from "vitest";
import { timeAgo } from "@/utils/timeAgo";

const NOW = new Date("2026-11-02T12:00:00Z").getTime();

afterEach(() => {
  vi.useRealTimers();
});

function at(msAgo: number): string {
  return new Date(NOW - msAgo).toISOString();
}

describe("timeAgo", () => {
  it("collapses anything under a minute to 'just now'", () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    expect(timeAgo(at(0))).toBe("just now");
    expect(timeAgo(at(59_000))).toBe("just now");
  });

  it("reports minutes, hours and days", () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    expect(timeAgo(at(5 * 60_000))).toBe("5m ago");
    expect(timeAgo(at(3 * 3_600_000))).toBe("3h ago");
    expect(timeAgo(at(26 * 3_600_000))).toBe("1 day ago");
    expect(timeAgo(at(3 * 86_400_000))).toBe("3 days ago");
  });

  it("treats future timestamps as now rather than negative ages", () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    expect(timeAgo(new Date(NOW + 60_000).toISOString())).toBe("just now");
  });
});

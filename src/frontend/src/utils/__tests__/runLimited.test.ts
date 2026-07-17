import { describe, expect, it } from "vitest";
import { runLimited } from "@/utils/runLimited";

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((r) => (resolve = r));
  return { promise, resolve };
}

describe("runLimited", () => {
  it("runs every item exactly once, preserving index", async () => {
    const seen: Array<number> = [];
    await runLimited([10, 20, 30], 2, async (item, index) => {
      await Promise.resolve();
      seen.push(item + index);
    });
    expect(seen.sort((a, b) => a - b)).toEqual([10, 21, 32]);
  });

  it("never exceeds the concurrency limit", async () => {
    const gates = [deferred(), deferred(), deferred(), deferred()];
    let inFlight = 0;
    let maxInFlight = 0;

    const run = runLimited([0, 1, 2, 3], 2, async (i) => {
      inFlight++;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await gates[i].promise;
      inFlight--;
    });

    // Let the first wave start, then release gates one at a time.
    await Promise.resolve();
    gates[0].resolve();
    gates[1].resolve();
    gates[2].resolve();
    gates[3].resolve();
    await run;

    expect(maxInFlight).toBeLessThanOrEqual(2);
  });

  it("resolves immediately for an empty list", async () => {
    await expect(runLimited([], 3, async () => {})).resolves.toBeUndefined();
  });
});

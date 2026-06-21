import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { EffectiveCell } from "@/components/EffectiveCell";

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(), removeListener: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("EffectiveCell", () => {
  it("renders an unchecked editable checkbox when fromDirect=false and fromGroups=[]", () => {
    const onToggle = vi.fn();
    render(
      React.createElement(EffectiveCell, {
        fromDirect: false,
        fromGroups: [],
        onToggle,
        ariaLabel: "Query Execute on Blue App DB",
      }),
      { wrapper: Wrapper },
    );
    const checkbox = screen.getByRole("checkbox", { name: /query execute on blue app db/i });
    expect(checkbox).not.toBeChecked();
    expect(checkbox).not.toBeDisabled();
  });

  it("renders a checked editable checkbox when fromDirect=true and fromGroups=[]", async () => {
    const onToggle = vi.fn();
    render(
      React.createElement(EffectiveCell, {
        fromDirect: true,
        fromGroups: [],
        onToggle,
        ariaLabel: "Query Execute on Blue App DB",
      }),
      { wrapper: Wrapper },
    );
    const checkbox = screen.getByRole("checkbox", { name: /query execute on blue app db/i });
    expect(checkbox).toBeChecked();
    await userEvent.click(checkbox);
    expect(onToggle).toHaveBeenCalled();
  });

  it("renders a non-interactive inherited marker (no checkbox) when fromDirect=false and fromGroups has entries", () => {
    const onToggle = vi.fn();
    render(
      React.createElement(EffectiveCell, {
        fromDirect: false,
        fromGroups: [{ groupId: "g1", name: "Analysts" }],
        onToggle,
        ariaLabel: "Query Execute on Blue App DB",
      }),
      { wrapper: Wrapper },
    );
    // No interactive checkbox
    expect(screen.queryByRole("checkbox")).toBeNull();
    // Should show the inherited icon with aria-label containing "Analysts"
    const marker = screen.getByLabelText(/analysts/i);
    expect(marker).toBeDefined();
  });

  it("renders a checked checkbox with the redundancy indicator dot when fromDirect=true and fromGroups has entries", () => {
    const onToggle = vi.fn();
    const { container } = render(
      React.createElement(EffectiveCell, {
        fromDirect: true,
        fromGroups: [{ groupId: "g1", name: "Analysts" }],
        onToggle,
        ariaLabel: "Query Execute on Blue App DB",
      }),
      { wrapper: Wrapper },
    );
    // Shows a checked checkbox (direct grant)
    const checkbox = screen.getByRole("checkbox", { name: /query execute on blue app db/i });
    expect(checkbox).toBeChecked();
    // And the Mantine Indicator dot signalling "also inherited via a group"
    expect(container.querySelector(".mantine-Indicator-indicator")).not.toBeNull();
  });

  it("respects the disabled prop", () => {
    const onToggle = vi.fn();
    render(
      React.createElement(EffectiveCell, {
        fromDirect: false,
        fromGroups: [],
        onToggle,
        ariaLabel: "Query Execute on Blue App DB",
        disabled: true,
      }),
      { wrapper: Wrapper },
    );
    const checkbox = screen.getByRole("checkbox", { name: /query execute on blue app db/i });
    expect(checkbox).toBeDisabled();
  });
});

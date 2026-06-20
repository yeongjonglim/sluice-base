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
  it("renders a non-interactive inherited marker when fromDirect=false and fromGroups is non-empty", () => {
    render(
      React.createElement(EffectiveCell, {
        fromDirect: false,
        fromGroups: [{ groupId: "g-1", name: "Analysts" }],
        onToggle: () => {},
        ariaLabel: "Query Execute on My DB",
      }),
      { wrapper: Wrapper },
    );

    // No checkbox should be rendered
    expect(screen.queryByRole("checkbox")).toBeNull();

    // The span should expose "Analysts" in its aria-label
    const el = document.querySelector("[aria-label*='Analysts']");
    expect(el).not.toBeNull();
    expect(el!.getAttribute("aria-label")).toMatch(/Analysts/);
  });

  it("does not have a role=checkbox for inherited-only cell", () => {
    render(
      React.createElement(EffectiveCell, {
        fromDirect: false,
        fromGroups: [{ groupId: "g-1", name: "Analysts" }],
        onToggle: () => {},
        ariaLabel: "Query Execute on My DB",
      }),
      { wrapper: Wrapper },
    );
    expect(screen.queryByRole("checkbox")).toBeNull();
  });

  it("renders an editable checkbox when fromDirect=true", async () => {
    const onToggle = vi.fn();
    render(
      React.createElement(EffectiveCell, {
        fromDirect: true,
        fromGroups: [],
        onToggle,
        ariaLabel: "Query Execute on My DB",
      }),
      { wrapper: Wrapper },
    );

    const checkbox = screen.getByRole("checkbox");
    expect(checkbox).toBeChecked();
    // Clicking it should call onToggle
    await userEvent.click(checkbox);
    expect(onToggle).toHaveBeenCalledWith(false);
  });

  it("renders an unchecked editable checkbox when fromDirect=false and no groups", () => {
    render(
      React.createElement(EffectiveCell, {
        fromDirect: false,
        fromGroups: [],
        onToggle: () => {},
        ariaLabel: "Query Execute on My DB",
      }),
      { wrapper: Wrapper },
    );

    const checkbox = screen.getByRole("checkbox");
    expect(checkbox).not.toBeChecked();
  });

  it("renders a checked checkbox with indicator when fromDirect=true and fromGroups non-empty", () => {
    render(
      React.createElement(EffectiveCell, {
        fromDirect: true,
        fromGroups: [{ groupId: "g-1", name: "Analysts" }],
        onToggle: () => {},
        ariaLabel: "Query Execute on My DB",
      }),
      { wrapper: Wrapper },
    );

    // A checkbox should be present and checked (direct grant)
    const checkbox = screen.getByRole("checkbox");
    expect(checkbox).toBeChecked();
  });
});

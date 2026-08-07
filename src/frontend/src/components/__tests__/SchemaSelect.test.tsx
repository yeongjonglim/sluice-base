import { fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { describe, expect, it, vi } from "vitest";
import { SchemaSelect } from "@/components/SchemaSelect";

function renderSelect(value: Array<string>) {
  const onChange = vi.fn();
  const utils = render(
    <MantineProvider>
      <SchemaSelect schemas={["public", "audit"]} value={value} onChange={onChange} />
    </MantineProvider>,
  );
  return { onChange, ...utils };
}

function openDropdown(container: HTMLElement) {
  const input = container.querySelector('[class*="PillsInput-input"]');
  fireEvent.click(input!);
}

describe("SchemaSelect", () => {
  it("renders a pill for each selected schema", () => {
    renderSelect(["public"]);
    // Mantine's Pill renders the label in a nested span (root + label both match "public"),
    // so scope the assertion to an element inside a Pill.
    expect(screen.getAllByText("public").some((el) => el.closest('[class*="Pill-"]'))).toBe(true);
  });

  it("adds a schema when its option is picked", () => {
    const { onChange, container } = renderSelect([]);
    openDropdown(container);
    fireEvent.click(screen.getByText("audit"));
    expect(onChange).toHaveBeenCalledWith(["audit"]);
  });

  it("selects every schema from the 'Select all' row", () => {
    const { onChange, container } = renderSelect([]);
    openDropdown(container);
    fireEvent.click(screen.getByText("Select all"));
    expect(onChange).toHaveBeenCalledWith(["public", "audit"]);
  });

  it("clears the selection when 'Select all' is toggled off", () => {
    const { onChange, container } = renderSelect(["public", "audit"]);
    openDropdown(container);
    fireEvent.click(screen.getByText("Select all"));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("collapses pills beyond the third into a +N counter", () => {
    const onChange = vi.fn();
    render(
      <MantineProvider>
        <SchemaSelect
          schemas={["a", "b", "c", "d", "e"]}
          value={["a", "b", "c", "d", "e"]}
          onChange={onChange}
        />
      </MantineProvider>,
    );
    // Three pills shown, the remaining two collapsed into "+2".
    expect(screen.getByText("+2")).toBeInTheDocument();
    for (const shown of ["a", "b", "c"]) {
      expect(screen.getAllByText(shown).some((el) => el.closest('[class*="Pill-"]'))).toBe(true);
    }
    // The collapsed schemas are not rendered as their own pills.
    expect(screen.queryAllByText("d").some((el) => el.closest('[class*="Pill-"]'))).toBe(false);
  });
});

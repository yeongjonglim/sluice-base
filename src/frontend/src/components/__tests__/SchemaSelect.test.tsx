import { fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { describe, expect, it, vi } from "vitest";
import { SchemaSelect } from "@/components/SchemaSelect";

function renderSelect(value: Array<string>) {
  const onChange = vi.fn();
  render(
    <MantineProvider>
      <SchemaSelect schemas={["public", "audit"]} value={value} onChange={onChange} />
    </MantineProvider>,
  );
  return { onChange };
}

describe("SchemaSelect", () => {
  it("shows each selected schema as a pill", () => {
    renderSelect(["public"]);
    const publicText = screen.getAllByText("public").find(el => el.closest(".mantine-MultiSelect-pill"));
    expect(publicText).toBeInTheDocument();
  });

  it("calls onChange with the added schema when an option is picked", () => {
    const { onChange } = renderSelect([]);
    fireEvent.click(screen.getByPlaceholderText("Schemas"));
    fireEvent.click(screen.getByRole("option", { name: "audit" }));
    expect(onChange).toHaveBeenCalledWith(["audit"]);
  });
});

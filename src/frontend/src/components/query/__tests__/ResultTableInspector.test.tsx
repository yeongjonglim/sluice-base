import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { ResultTable } from "@/components/query/ResultTable";

// jsdom has no layout, so TanStack Virtual sees a zero-height viewport and renders
// no rows. The inspector lives on those rows, so this file stubs the virtualizer
// to hand back every row — the rest of the table (cells, toolbar, popover) is real.
vi.mock("@tanstack/react-virtual", () => ({
  useVirtualizer: ({ count }: { count: number }) => ({
    getVirtualItems: () =>
      Array.from({ length: count }, (_, index) => ({
        index,
        key: index,
        start: index * 33,
        end: (index + 1) * 33,
      })),
    getTotalSize: () => count * 33,
    measureElement: () => undefined,
    measure: () => undefined,
    scrollToIndex: () => undefined,
  }),
}));

function renderTable(columns: Array<string>, rows: Array<Array<string | null>>) {
  return render(
    <MantineProvider env="test">
      <ResultTable
        columns={columns}
        rows={rows}
        rowCount={rows.length}
        durationMs={1}
        resultIndex={0}
      />
    </MantineProvider>,
  );
}

describe("ResultTable value inspector", () => {
  it("shows the whole value of a clicked cell, which the grid may truncate", async () => {
    // trimEnd so the exact-text query matches the rendered cell text
    const note = "The full note. ".repeat(30).trim();
    renderTable(["id", "note"], [["1", note]]);

    fireEvent.click(screen.getByText(note));

    const dialog = await screen.findByRole("dialog", { hidden: true });
    expect(dialog).toHaveAttribute("aria-label", "note value, row 1");
    expect(within(dialog).getByText(note)).toBeInTheDocument();
    expect(
      within(dialog).getByText(`row 1 · ${note.length.toLocaleString()} chars`),
    ).toBeInTheDocument();
  });

  it("closes the value panel on Escape", async () => {
    renderTable(["id", "note"], [["1", "a long note"]]);

    fireEvent.click(screen.getByText("a long note"));
    const dialog = await screen.findByRole("dialog", { hidden: true });

    fireEvent.keyDown(dialog, { key: "Escape" });

    await waitFor(() => {
      expect(screen.queryByRole("dialog", { hidden: true })).not.toBeInTheDocument();
    });
  });

  it("formats compact JSON and lets the value be viewed raw", async () => {
    const raw = '{"plan":"scale","seats":48}';
    renderTable(["metadata"], [[raw]]);

    fireEvent.click(screen.getByText(raw));

    const dialog = await screen.findByRole("dialog", { hidden: true });
    expect(within(dialog).getByText("JSON")).toBeInTheDocument();
    expect(within(dialog).getByText('"plan": "scale",')).toBeInTheDocument();
    expect(within(dialog).getByText('"seats": 48')).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("radio", { name: "Raw" }));

    expect(dialog.querySelector("pre")?.textContent).toBe(raw);
  });

  it("keeps jsonb_pretty formatting as returned and hides the format toggle", async () => {
    const pretty = '{\n    "a": 1\n}';
    renderTable(["metadata"], [[pretty]]);

    // The cell's rendered text is whitespace-normalised, so match the raw text node.
    const cell = screen.getByText(
      (_, element) => element?.tagName === "TD" && element.textContent === pretty,
    );
    fireEvent.click(cell);

    const dialog = await screen.findByRole("dialog", { hidden: true });
    expect(within(dialog).getByText('"a": 1')).toBeInTheDocument();
    expect(within(dialog).queryByRole("radio", { name: "Raw" })).not.toBeInTheDocument();
  });

  it("copies the full JSON in the selected view", async () => {
    const raw = '{"a":1}';
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", {
      value: { writeText },
      configurable: true,
    });
    renderTable(["metadata"], [[raw]]);

    fireEvent.click(screen.getByText(raw));
    const dialog = await screen.findByRole("dialog", { hidden: true });
    fireEvent.click(within(dialog).getByRole("button", { name: /Copy metadata value/ }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('{\n  "a": 1\n}'));

    fireEvent.click(within(dialog).getByRole("radio", { name: "Raw" }));
    fireEvent.click(within(dialog).getByRole("button", { name: /Copy metadata value/ }));
    await waitFor(() => expect(writeText).toHaveBeenLastCalledWith(raw));
  });

  it("folds a single node without truncating its value", async () => {
    const raw = '{"features":["a","b"],"ok":true}';
    renderTable(["metadata"], [[raw]]);

    fireEvent.click(screen.getByText(raw));
    const dialog = await screen.findByRole("dialog", { hidden: true });
    expect(within(dialog).getByText('"a",')).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Collapse $.features" }));

    expect(within(dialog).queryByText('"a",')).not.toBeInTheDocument();
    expect(within(dialog).getByText('"features": [ … ],')).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Expand $.features" }));

    expect(within(dialog).getByText('"a",')).toBeInTheDocument();
  });

  it("folds everything, then copies the complete value", async () => {
    const raw = '{"a":1,"b":{"c":2}}';
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", {
      value: { writeText },
      configurable: true,
    });
    renderTable(["metadata"], [[raw]]);

    fireEvent.click(screen.getByText(raw));
    const dialog = await screen.findByRole("dialog", { hidden: true });

    fireEvent.click(within(dialog).getByRole("button", { name: "Collapse all" }));

    expect(within(dialog).getByText("{ … }")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Expand all" })).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: /Copy full metadata value/ }));

    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith(
        ['{', '  "a": 1,', '  "b": {', '    "c": 2', "  }", "}"].join("\n"),
      ),
    );
  });

  it("does not open a value panel for NULL cells", () => {
    renderTable(["id", "note"], [["1", null]]);

    fireEvent.click(screen.getByText("NULL"));

    expect(screen.queryByRole("dialog", { hidden: true })).not.toBeInTheDocument();
  });

  it("does not open when the click ends a text selection", () => {
    renderTable(["id", "name"], [["1", "Ada"]]);
    const cell = screen.getByText("Ada").closest("td") as HTMLTableCellElement;
    const range = document.createRange();
    range.selectNodeContents(cell);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);

    fireEvent.click(cell);

    expect(screen.queryByRole("dialog", { hidden: true })).not.toBeInTheDocument();
    selection?.removeAllRanges();
  });

  it("wraps cell contents when the Wrap toggle is on", () => {
    renderTable(["id", "name"], [["1", "Ada"]]);
    const cell = screen.getByText("Ada").closest("td") as HTMLTableCellElement;
    expect(cell).toHaveStyle({ whiteSpace: "nowrap" });

    fireEvent.click(screen.getByRole("button", { name: "Wrap" }));

    expect(screen.getByRole("button", { name: "Wrap" })).toHaveAttribute("aria-pressed", "true");
    expect(cell).toHaveStyle({ whiteSpace: "pre-wrap" });
  });
});

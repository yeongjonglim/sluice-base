import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { ResultTable } from "@/components/query/ResultTable";
import { filterRows } from "@/utils/filterRows";

describe("filterRows", () => {
  const rows: Array<Array<string | null>> = [
    ["1", "Ada Lovelace"],
    ["2", "Alan Turing"],
    ["3", null],
  ];

  it("returns the same array (all rows) for an empty or whitespace query", () => {
    expect(filterRows(rows, "")).toBe(rows);
    expect(filterRows(rows, "   ")).toBe(rows);
  });

  it("matches case-insensitively on any cell", () => {
    expect(filterRows(rows, "alan")).toEqual([["2", "Alan Turing"]]);
    expect(filterRows(rows, "LOVE")).toEqual([["1", "Ada Lovelace"]]);
  });

  it("matches across any column, including numeric-looking text", () => {
    expect(filterRows(rows, "2")).toEqual([["2", "Alan Turing"]]);
  });

  it("never matches NULL cells", () => {
    expect(filterRows(rows, "null")).toEqual([]);
  });

  it("returns an empty array when nothing matches", () => {
    expect(filterRows(rows, "zzz")).toEqual([]);
  });
});

describe("ResultTable", () => {
  function renderTable(rows: Array<Array<string | null>>) {
    return render(
      <MantineProvider>
        <ResultTable
          columns={["id", "name"]}
          rows={rows}
          rowCount={rows.length}
          durationMs={5}
          resultIndex={0}
        />
      </MantineProvider>,
    );
  }

  it("renders the column headers and a filter input", () => {
    renderTable([["1", "Ada"]]);
    expect(screen.getByText("id")).toBeInTheDocument();
    expect(screen.getByText("name")).toBeInTheDocument();
    expect(screen.getByLabelText("Filter rows")).toBeInTheDocument();
  });

  it("shows the row count and duration", () => {
    renderTable([["1", "Ada"], ["2", "Bob"]]);
    expect(screen.getByText(/2 rows/)).toBeInTheDocument();
    expect(screen.getByText(/5 ms/)).toBeInTheDocument();
  });
});

describe("ResultTable column resizing", () => {
  // Force the char-count fallback (6.6px/char) so widths are deterministic.
  beforeEach(() => {
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
  });
  afterEach(() => vi.restoreAllMocks());

  function renderCustom(columns: Array<string>, rows: Array<Array<string | null>>) {
    return render(
      <MantineProvider>
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

  function colWidths(container: HTMLElement): Array<string> {
    return Array.from(container.querySelectorAll("col")).map(
      (col) => (col as HTMLElement).style.width,
    );
  }

  it("widens a column when its handle is dragged right", () => {
    const { container } = renderCustom(["id", "name"], [["1", "Ada"]]);
    expect(colWidths(container)[0]).toBe("56px");

    const handle = screen.getByLabelText("Resize id column");
    fireEvent.pointerDown(handle, { clientX: 0 });
    fireEvent.pointerMove(window, { clientX: 100 });
    fireEvent.pointerUp(window, { clientX: 100 });

    // 56 + 100 = 156, within [56, 800].
    expect(colWidths(container)[0]).toBe("156px");
  });

  it("auto-fits a column to its content on double-click", () => {
    const long = "x".repeat(80);
    const { container } = renderCustom(["id", "name"], [["1", long]]);
    // Estimate clamps the 552px content down to the 360 cap.
    expect(colWidths(container)[1]).toBe("360px");

    fireEvent.dblClick(screen.getByLabelText("Resize name column"));
    // Auto-fit ignores the 360 cap and fits the real content.
    expect(colWidths(container)[1]).toBe("552px");
  });

  it("resets widths when the result's columns change", () => {
    const { container, rerender } = renderCustom(["id", "name"], [["1", "Ada"]]);
    fireEvent.pointerDown(screen.getByLabelText("Resize id column"), { clientX: 0 });
    fireEvent.pointerMove(window, { clientX: 100 });
    fireEvent.pointerUp(window, { clientX: 100 });
    expect(colWidths(container)[0]).toBe("156px");

    rerender(
      <MantineProvider>
        <ResultTable columns={["v"]} rows={[["1"]]} rowCount={1} durationMs={1} resultIndex={0} />
      </MantineProvider>,
    );
    // Fresh estimate for the new single column; the dragged 156px is gone.
    expect(colWidths(container)).toEqual(["56px"]);
  });
});

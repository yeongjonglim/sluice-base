import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
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

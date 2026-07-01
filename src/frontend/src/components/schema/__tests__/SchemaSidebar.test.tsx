import { fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { beforeEach, describe, expect, it } from "vitest";
import { SchemaSidebar } from "@/components/schema/SchemaSidebar";

function makeSchema() {
  return {
    isLoading: false,
    isError: false,
    data: {
      extensions: [{ name: "citext", version: "1.6", schema: "public" }],
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "orders",
              columns: [{ name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false }],
              primaryKey: { columns: ["id"] },
              foreignKeys: [],
              indexes: [{ name: "idx_orders_status", columns: ["status"], isUnique: false, isPrimary: false, method: "btree" }],
            },
          ],
          views: [{ name: "active_orders", columns: [{ name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false }] }],
          materializedViews: [{ name: "order_totals", columns: [], indexes: [] }],
          routines: [{ name: "order_count", kind: "function", returnType: "bigint", language: "sql", signature: "uid integer" }],
          sequences: [{ name: "ticket_seq", dataType: "bigint", start: 1000, increment: 5, minValue: 1, maxValue: 9007199254740991, cycle: false, ownedByColumn: null }],
          types: [{ name: "order_status", kind: "enum", enumLabels: ["pending", "shipped"], attributes: null, baseType: null }],
        },
      ],
    },
  } as unknown as Parameters<typeof SchemaSidebar>[0]["schema"];
}

function renderSidebar() {
  render(
    <MantineProvider>
      <SchemaSidebar schema={makeSchema()} onTableClick={() => {}} />
    </MantineProvider>,
  );
}

describe("SchemaSidebar", () => {
  beforeEach(() => {
    // Expanded state persists in sessionStorage; clear it so each test starts collapsed.
    sessionStorage.clear();
  });

  it("shows grouped object folders after expanding the schema", () => {
    renderSidebar();
    // The schema name also appears in its overflow-tooltip portal, so match the first.
    fireEvent.click(screen.getAllByText("public")[0]);

    expect(screen.getByText(/^Tables/)).toBeInTheDocument();
    expect(screen.getByText(/^Views/)).toBeInTheDocument();
    expect(screen.getByText(/^Materialized Views/)).toBeInTheDocument();
    expect(screen.getByText(/^Functions/)).toBeInTheDocument();
    expect(screen.getByText(/^Sequences/)).toBeInTheDocument();
    expect(screen.getByText(/^Types/)).toBeInTheDocument();
  });

  it("lists extensions with their version at the database level", () => {
    renderSidebar();
    const header = screen.getByText(/^Extensions/);
    expect(header).toBeInTheDocument();

    fireEvent.click(header);
    // Name and version render as separate nodes now.
    expect(screen.getByText("citext", { exact: true })).toBeInTheDocument();
    expect(screen.getByText("1.6", { exact: true })).toBeInTheDocument();
  });
});

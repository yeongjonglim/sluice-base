import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SchemaSidebar } from "@/components/schema/SchemaSidebar";

// The drawer renders SqlEditor for view/matview/function definitions, which calls
// useSchemaCompletions (a react-query hook). Stub it so the sidebar test doesn't need a
// QueryClientProvider — same approach as SqlEditor.test.tsx.
vi.mock("@/api/hooks", () => ({
  useSchemaCompletions: () => ({ data: undefined }),
}));

type Col = { name: string; dataType: string; isNullable: boolean; isSensitive: boolean; isRestricted: boolean };
const col = (name: string, dataType: string, extra: Partial<Col> = {}): Col => ({
  name,
  dataType,
  isNullable: false,
  isSensitive: false,
  isRestricted: false,
  ...extra,
});

// A schema exercising every branch: column roles (pk/fk/plain), nullable, sensitive and
// restricted columns, table + matview indexes, all object groups, and a second schema whose
// groups are all empty (so the empty-group path renders too).
function fullTree() {
  return {
    extensions: [{ name: "citext", version: "1.8", schema: "public" }],
    schemas: [
      {
        name: "public",
        tables: [
          {
            name: "orders",
            columns: [
              col("id", "integer"),
              col("user_id", "integer"),
              col("total", "numeric(10,2)", { isNullable: true }),
              col("secret", "text", { isNullable: true, isSensitive: true }),
              col("ssn", "text", { isNullable: true, isSensitive: true, isRestricted: true }),
            ],
            primaryKey: { columns: ["id"] },
            foreignKeys: [
              { constraintName: "orders_user_fk", columns: ["user_id"], referencedSchema: "public", referencedTable: "users", referencedColumns: ["id"] },
            ],
            indexes: [
              { name: "orders_pkey", columns: ["id"], isUnique: true, isPrimary: true, method: "btree" },
              { name: "idx_orders_status", columns: ["status"], isUnique: true, isPrimary: false, method: "btree" },
            ],
          },
        ],
        views: [
          { name: "active_orders", columns: [col("id", "integer", { isNullable: true })], definition: "SELECT id FROM orders WHERE active" },
          { name: "masked", columns: [col("x", "text", { isNullable: true, isSensitive: true })], definition: "SELECT x FROM secrets" },
        ],
        materializedViews: [
          {
            name: "order_totals",
            columns: [col("user_id", "integer", { isNullable: true })],
            indexes: [{ name: "mv_idx", columns: ["user_id"], isUnique: false, isPrimary: false, method: "btree" }],
            definition: "SELECT user_id FROM orders",
          },
        ],
        routines: [
          { name: "order_count", kind: "function", returnType: "bigint", language: "sql", signature: "uid integer", definition: "CREATE OR REPLACE FUNCTION order_count(uid integer) RETURNS bigint AS $$ $$" },
          { name: "refresh_it", kind: "procedure", returnType: null, language: "plpgsql", signature: "", definition: "CREATE OR REPLACE PROCEDURE refresh_it() AS $$ $$" },
        ],
        sequences: [
          { name: "ticket_seq", dataType: "bigint", start: 1000, increment: 5, minValue: 1, maxValue: 9007199254740991, cycle: false, ownedByColumn: null },
        ],
        types: [
          { name: "order_status", kind: "enum", enumLabels: ["pending", "shipped"], attributes: null, baseType: null },
          { name: "address", kind: "composite", enumLabels: null, attributes: ["street text"], baseType: null },
        ],
      },
      {
        name: "empty_schema",
        tables: [],
        views: [],
        materializedViews: [],
        routines: [],
        sequences: [],
        types: [],
      },
    ],
  };
}

const EXPAND_ALL = [
  "schema:public",
  "schema:public:tables",
  "table:public.orders",
  "schema:public:views",
  "view:public.active_orders",
  "view:public.masked",
  "schema:public:matviews",
  "matview:public.order_totals",
  "schema:public:functions",
  "schema:public:sequences",
  "schema:public:types",
  "schema:empty_schema",
  "extensions",
];

function seedExpanded(ids: Array<string>) {
  sessionStorage.setItem("sluice:query:expanded", JSON.stringify(ids));
}

function makeSchema(
  data: unknown = fullTree(),
  extra: Record<string, unknown> = {},
) {
  return { isLoading: false, isError: false, data, ...extra } as unknown as Parameters<typeof SchemaSidebar>[0]["schema"];
}

function renderSidebar(schema = makeSchema(), onTableClick = vi.fn()) {
  const utils = render(
    <MantineProvider>
      <SchemaSidebar schema={schema} onTableClick={onTableClick} />
    </MantineProvider>,
  );
  return { ...utils, onTableClick };
}

// The row name is duplicated in the overflow-tooltip portal, so target the real NavLink.
function clickRow(name: string) {
  const link = screen
    .getAllByText(name)
    .map((el) => el.closest("a"))
    .find((el): el is HTMLAnchorElement => el !== null);
  fireEvent.click(link!);
}

describe("SchemaSidebar", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it("prompts to pick a database when there is no data", () => {
    renderSidebar(makeSchema(null));
    expect(screen.getByText(/Select a database/)).toBeInTheDocument();
  });

  it("shows skeletons while loading", () => {
    const { container } = renderSidebar(makeSchema(undefined, { isLoading: true }));
    expect(container.querySelectorAll(".mantine-Skeleton-root").length).toBeGreaterThan(0);
  });

  it("shows an error message when loading fails", () => {
    renderSidebar(makeSchema(undefined, { isError: true }));
    expect(screen.getByText(/Couldn't load schema/)).toBeInTheDocument();
  });

  it("renders every object type with its metadata when expanded", () => {
    seedExpanded(EXPAND_ALL);
    renderSidebar();

    // Group headers
    expect(screen.getByText(/^Tables/)).toBeInTheDocument();
    expect(screen.getByText(/^Views/)).toBeInTheDocument();
    expect(screen.getByText(/^Materialized Views/)).toBeInTheDocument();
    expect(screen.getByText(/^Functions/)).toBeInTheDocument();
    expect(screen.getByText(/^Sequences/)).toBeInTheDocument();
    expect(screen.getByText(/^Types/)).toBeInTheDocument();
    expect(screen.getByText(/^Extensions/)).toBeInTheDocument();

    // Table columns (pk/fk/plain/nullable) and indexes
    expect(screen.getAllByText("id")[0]).toBeInTheDocument();
    expect(screen.getAllByText("user_id")[0]).toBeInTheDocument();
    expect(screen.getAllByText(/numeric\(10,2\) · null/)[0]).toBeInTheDocument();
    expect(screen.getAllByText("orders_pkey")[0]).toBeInTheDocument();
    expect(screen.getAllByText(/id · pk/)[0]).toBeInTheDocument();
    expect(screen.getAllByText(/status · unique|status/)[0]).toBeInTheDocument();

    // Other object types
    expect(screen.getAllByText("active_orders")[0]).toBeInTheDocument();
    expect(screen.getAllByText("order_totals")[0]).toBeInTheDocument();
    expect(screen.getAllByText("order_count")[0]).toBeInTheDocument();
    expect(screen.getAllByText(/uid integer.*bigint/)[0]).toBeInTheDocument();
    expect(screen.getAllByText("refresh_it")[0]).toBeInTheDocument();
    expect(screen.getAllByText("ticket_seq")[0]).toBeInTheDocument();
    expect(screen.getAllByText("order_status")[0]).toBeInTheDocument();
    expect(screen.getAllByText(/enum · pending, shipped/)[0]).toBeInTheDocument();
    expect(screen.getAllByText("address")[0]).toBeInTheDocument();
    expect(screen.getAllByText("citext")[0]).toBeInTheDocument();

    // Exercise the overflow-tooltip mouse handler.
    fireEvent.mouseEnter(screen.getAllByText("id")[0].closest("a") ?? screen.getAllByText("id")[0]);

    // Index method is shown inline after the columns/uniqueness.
    expect(screen.getAllByText(/status · unique · btree/)[0]).toBeInTheDocument();
    // Foreign-key columns show their reference target inline.
    expect(screen.getAllByText(/→ users\.id/)[0]).toBeInTheDocument();
    // Extensions show their owning schema after the version.
    expect(screen.getAllByText(/1\.8 · public/)[0]).toBeInTheDocument();
  });

  it("expands and collapses a schema on click", () => {
    renderSidebar();
    expect(screen.queryByText(/^Tables/)).not.toBeInTheDocument();

    clickRow("public");
    expect(screen.getByText(/^Tables/)).toBeInTheDocument();

    clickRow("public");
    expect(screen.queryByText(/^Tables/)).not.toBeInTheDocument();
  });

  it("toggles every group and object by clicking, and appends from a view", () => {
    const { onTableClick } = renderSidebar();

    clickRow("public");
    for (const header of [/^Tables/, /^Views/, /^Materialized Views/, /^Functions/, /^Sequences/, /^Types/, /^Extensions/]) {
      fireEvent.click(screen.getByText(header));
    }
    // Collapsible objects fire their own toggle handlers.
    clickRow("orders");
    clickRow("active_orders");
    clickRow("order_totals");

    // Fire every enabled append control (covers both the table and view onClick paths).
    screen
      .getAllByRole("button", { name: "Append SELECT to the editor" })
      .filter((b) => !(b as HTMLButtonElement).disabled)
      .forEach((b) => fireEvent.click(b));

    expect(onTableClick).toHaveBeenCalled();
  });

  it("appends a SELECT for a table with selectable columns", () => {
    seedExpanded(["schema:public", "schema:public:tables"]);
    const { onTableClick } = renderSidebar();

    fireEvent.click(screen.getByRole("button", { name: "Append SELECT to the editor" }));

    expect(onTableClick).toHaveBeenCalledTimes(1);
    const [schemaName, tableName, columns] = onTableClick.mock.calls[0];
    expect(schemaName).toBe("public");
    expect(tableName).toBe("orders");
    expect(columns).toHaveLength(5);
  });

  it("disables the append control when every column is sensitive", () => {
    const tree = {
      extensions: [],
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "vault",
              columns: [col("pw", "text", { isSensitive: true })],
              primaryKey: null,
              foreignKeys: [],
              indexes: [],
            },
          ],
          views: [],
          materializedViews: [],
          routines: [],
          sequences: [],
          types: [],
        },
      ],
    };
    seedExpanded(["schema:public", "schema:public:tables"]);
    renderSidebar(makeSchema(tree));

    expect(screen.getByRole("button", { name: "Append SELECT to the editor" })).toBeDisabled();
  });

  it("opens the metadata drawer for a sequence", async () => {
    seedExpanded(["schema:public", "schema:public:sequences"]);
    renderSidebar();

    fireEvent.click(screen.getByRole("button", { name: "View metadata for ticket_seq" }));

    // Mantine Drawer renders into a portal and mounts its content after a transition —
    // wait for it rather than asserting synchronously (see ConnectMcpTrigger.test.tsx).
    await waitFor(() => expect(screen.getByText(/^Sequence$/)).toBeInTheDocument());
    expect(screen.getByText("1000")).toBeInTheDocument(); // start
    expect(screen.getByText("5")).toBeInTheDocument(); // increment
  });

  it("shows composite attributes in the drawer for a type", async () => {
    seedExpanded(["schema:public", "schema:public:types"]);
    renderSidebar();

    // order_status (enum) has no attributes; address (composite) does. Both rows have a button.
    fireEvent.click(screen.getByRole("button", { name: "View metadata for address" }));

    await waitFor(() => expect(screen.getByText("street text")).toBeInTheDocument());
  });

  it("renders a view definition in the drawer", async () => {
    seedExpanded(["schema:public", "schema:public:views"]);
    renderSidebar();

    fireEvent.click(screen.getByRole("button", { name: "View metadata for active_orders" }));

    await waitFor(() => expect(screen.getByText("Definition")).toBeInTheDocument());
  });
});

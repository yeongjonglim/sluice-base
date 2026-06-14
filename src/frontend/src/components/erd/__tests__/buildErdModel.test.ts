import { describe, expect, it } from "vitest";
import { buildErdModel } from "@/components/erd/buildErdModel";

const tree = {
  schemas: [
    {
      name: "public",
      tables: [
        {
          name: "users",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "email", dataType: "text", isNullable: false, isSensitive: true, isRestricted: true },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [],
        },
        {
          name: "orders",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "user_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [
            {
              constraintName: "orders_user_id_fkey",
              columns: ["user_id"],
              referencedSchema: "public",
              referencedTable: "users",
              referencedColumns: ["id"],
            },
          ],
        },
      ],
    },
  ],
};

describe("buildErdModel", () => {
  it("creates one node per table keyed by schema.table", () => {
    const { nodes } = buildErdModel(tree);
    expect(nodes.map((n) => n.id).sort()).toEqual(["public.orders", "public.users"]);
  });

  it("marks primary-key and foreign-key columns", () => {
    const { nodes } = buildErdModel(tree);
    const orders = nodes.find((n) => n.id === "public.orders")!;
    const idCol = orders.data.columns.find((c) => c.name === "id")!;
    const fkCol = orders.data.columns.find((c) => c.name === "user_id")!;
    expect(idCol.isPrimaryKey).toBe(true);
    expect(fkCol.isForeignKey).toBe(true);
    expect(fkCol.isPrimaryKey).toBe(false);
  });

  it("passes through the sensitive and restricted flags", () => {
    const { nodes } = buildErdModel(tree);
    const users = nodes.find((n) => n.id === "public.users")!;
    const email = users.data.columns.find((c) => c.name === "email")!;
    expect(email.isSensitive).toBe(true);
    expect(email.isRestricted).toBe(true);
  });

  it("creates one edge per foreign key linking the two tables", () => {
    const { edges } = buildErdModel(tree);
    expect(edges).toHaveLength(1);
    expect(edges[0]).toMatchObject({
      source: "public.orders",
      target: "public.users",
      label: "orders_user_id_fkey",
    });
  });
});

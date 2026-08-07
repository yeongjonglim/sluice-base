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
          indexes: [],
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
  extensions: [],
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

// A two-schema tree exercising the filter:
//  - public.orders -> public.users (same-schema FK)
//  - public.orders -> audit.log     (cross-schema FK: pulls audit.log in when audit is hidden)
//  - audit.log     -> public.users  (a pulled-in table's own FK: must NOT be drawn — one hop only)
//  - audit.settings -> public.users (hidden table referencing INTO a visible schema: no reverse pull-in)
const multiSchemaTree = {
  schemas: [
    {
      name: "public",
      tables: [
        {
          name: "users",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [],
          indexes: [],
        },
        {
          name: "orders",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "user_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "audit_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [
            {
              constraintName: "orders_user_fk",
              columns: ["user_id"],
              referencedSchema: "public",
              referencedTable: "users",
              referencedColumns: ["id"],
            },
            {
              constraintName: "orders_audit_fk",
              columns: ["audit_id"],
              referencedSchema: "audit",
              referencedTable: "log",
              referencedColumns: ["id"],
            },
          ],
          indexes: [],
        },
      ],
      views: [],
      materializedViews: [],
      routines: [],
      sequences: [],
      types: [],
    },
    {
      name: "audit",
      tables: [
        {
          name: "log",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "actor_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [
            {
              constraintName: "log_actor_fk",
              columns: ["actor_id"],
              referencedSchema: "public",
              referencedTable: "users",
              referencedColumns: ["id"],
            },
          ],
          indexes: [],
        },
        {
          name: "settings",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "owner_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [
            {
              constraintName: "settings_owner_fk",
              columns: ["owner_id"],
              referencedSchema: "public",
              referencedTable: "users",
              referencedColumns: ["id"],
            },
          ],
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
  extensions: [],
};

describe("buildErdModel schema filter", () => {
  it("unfiltered output includes every schema's tables and FKs", () => {
    const { nodes, edges } = buildErdModel(multiSchemaTree);
    expect(nodes.map((n) => n.id).sort()).toEqual([
      "audit.log",
      "audit.settings",
      "public.orders",
      "public.users",
    ]);
    // All four FKs are drawn when nothing is filtered.
    expect(edges.map((e) => e.label).sort()).toEqual([
      "log_actor_fk",
      "orders_audit_fk",
      "orders_user_fk",
      "settings_owner_fk",
    ]);
    // Base nodes are never flagged external.
    expect(nodes.every((n) => n.data.isExternal === false)).toBe(true);
  });

  it("filtering to one schema keeps only its tables plus pulled-in referenced tables", () => {
    const { nodes } = buildErdModel(multiSchemaTree, new Set(["public"]));
    expect(nodes.map((n) => n.id).sort()).toEqual([
      "audit.log", // pulled in via public.orders -> audit.log
      "public.orders",
      "public.users",
    ]);
    // audit.settings is NOT pulled in (it only references INTO public; no reverse pull-in).
    expect(nodes.find((n) => n.id === "audit.settings")).toBeUndefined();
  });

  it("flags a pulled-in cross-schema table as external and keeps base tables non-external", () => {
    const { nodes } = buildErdModel(multiSchemaTree, new Set(["public"]));
    expect(nodes.find((n) => n.id === "audit.log")!.data.isExternal).toBe(true);
    expect(nodes.find((n) => n.id === "public.orders")!.data.isExternal).toBe(false);
  });

  it("keeps the cross-schema FK edge to the pulled-in table but not the pulled-in table's own FKs", () => {
    const { edges } = buildErdModel(multiSchemaTree, new Set(["public"]));
    const labels = edges.map((e) => e.label).sort();
    // orders' two FKs are drawn; audit.log's own FK (log_actor_fk) is NOT (one hop only).
    expect(labels).toEqual(["orders_audit_fk", "orders_user_fk"]);
    expect(edges.find((e) => e.source === "public.orders" && e.target === "audit.log")).toBeTruthy();
    expect(edges.find((e) => e.source === "audit.log")).toBeUndefined();
  });
});

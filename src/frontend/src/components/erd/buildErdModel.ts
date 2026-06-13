import type { Edge, Node } from "@xyflow/react";
import type { paths } from "@/api/schema";

export type SchemaTree =
  paths["/api/schema/{databaseId}"]["get"]["responses"][200]["content"]["application/json"];

export interface ErdColumn {
  name: string;
  dataType: string;
  isNullable: boolean;
  isSensitive: boolean;
  isRestricted: boolean;
  isPrimaryKey: boolean;
  isForeignKey: boolean;
}

export interface TableNodeData extends Record<string, unknown> {
  schema: string;
  table: string;
  columns: Array<ErdColumn>;
}

export type TableNode = Node<TableNodeData, "table">;

export interface ErdModel {
  nodes: Array<TableNode>;
  edges: Array<Edge>;
}

export function buildErdModel(tree: SchemaTree): ErdModel {
  const pkColumns = new Map<string, Set<string>>();
  for (const pk of tree.primaryKeys) {
    pkColumns.set(`${pk.schema}.${pk.table}`, new Set(pk.columns));
  }

  const fkColumns = new Map<string, Set<string>>();
  for (const fk of tree.foreignKeys) {
    const key = `${fk.schema}.${fk.table}`;
    const set = fkColumns.get(key) ?? new Set<string>();
    for (const c of fk.columns) set.add(c);
    fkColumns.set(key, set);
  }

  const nodes: Array<TableNode> = [];
  for (const schema of tree.schemas) {
    for (const table of schema.tables) {
      const tableKey = `${schema.name}.${table.name}`;
      const pks = pkColumns.get(tableKey) ?? new Set<string>();
      const fks = fkColumns.get(tableKey) ?? new Set<string>();
      nodes.push({
        id: tableKey,
        type: "table",
        position: { x: 0, y: 0 },
        data: {
          schema: schema.name,
          table: table.name,
          columns: table.columns.map((c) => ({
            name: c.name,
            dataType: c.dataType,
            isNullable: c.isNullable,
            isSensitive: c.isSensitive,
            isRestricted: c.isRestricted,
            isPrimaryKey: pks.has(c.name),
            isForeignKey: fks.has(c.name),
          })),
        },
      });
    }
  }

  const edges: Array<Edge> = tree.foreignKeys.map((fk) => ({
    // constraintName is unique within the database — safe to use as the React Flow edge id.
    id: fk.constraintName,
    source: `${fk.schema}.${fk.table}`,
    target: `${fk.referencedSchema}.${fk.referencedTable}`,
    label: fk.constraintName,
  }));

  return { nodes, edges };
}

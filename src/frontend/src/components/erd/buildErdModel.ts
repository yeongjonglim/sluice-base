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
  const nodes: Array<TableNode> = [];
  const edges: Array<Edge> = [];

  for (const schema of tree.schemas) {
    for (const table of schema.tables) {
      const tableId = `${schema.name}.${table.name}`;
      const pkColumns = new Set(table.primaryKey?.columns ?? []);
      const fkColumns = new Set(table.foreignKeys.flatMap((fk) => fk.columns));

      nodes.push({
        id: tableId,
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
            isPrimaryKey: pkColumns.has(c.name),
            isForeignKey: fkColumns.has(c.name),
          })),
        },
      });

      for (const fk of table.foreignKeys) {
        edges.push({
          // constraintName is unique within the database — safe to use as the React Flow edge id.
          id: fk.constraintName,
          source: tableId,
          target: `${fk.referencedSchema}.${fk.referencedTable}`,
          label: fk.constraintName,
        });
      }
    }
  }

  return { nodes, edges };
}

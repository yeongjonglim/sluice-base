import type { Edge, Node } from "@xyflow/react";
import type { paths } from "@/api/schema";

export type SchemaTree =
  paths["/api/schema/{databaseId}"]["get"]["responses"][200]["content"]["application/json"];

type SchemaTable = SchemaTree["schemas"][number]["tables"][number];

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
  // True for a table pulled into the diagram only because a visible table's
  // foreign key references it, while its own schema is filtered out.
  isExternal: boolean;
}

export type TableNode = Node<TableNodeData, "table">;

export interface ErdModel {
  nodes: Array<TableNode>;
  edges: Array<Edge>;
}

function buildTableNode(schemaName: string, table: SchemaTable, isExternal: boolean): TableNode {
  const pkColumns = new Set(table.primaryKey?.columns ?? []);
  const fkColumns = new Set(table.foreignKeys.flatMap((fk) => fk.columns));

  return {
    id: `${schemaName}.${table.name}`,
    type: "table",
    position: { x: 0, y: 0 },
    data: {
      schema: schemaName,
      table: table.name,
      isExternal,
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
  };
}

/**
 * Build the React Flow node/edge model from a schema tree.
 *
 * When `visibleSchemas` is omitted, every table in every schema is rendered
 * (the diagram's original behaviour). When provided, only tables in the listed
 * schemas are rendered as "base" nodes; a base table's outbound foreign key
 * into a hidden schema pulls the referenced table in (one hop) flagged
 * `isExternal`, so the relationship stays visible.
 */
export function buildErdModel(tree: SchemaTree, visibleSchemas?: Set<string>): ErdModel {
  const nodes: Array<TableNode> = [];
  const edges: Array<Edge> = [];

  // Index every table by `${schema}.${table}` so a referenced table can be
  // resolved for pull-in regardless of whether its schema is currently visible.
  const tableIndex = new Map<string, { schemaName: string; table: SchemaTable }>();
  for (const schema of tree.schemas) {
    for (const table of schema.tables) {
      tableIndex.set(`${schema.name}.${table.name}`, { schemaName: schema.name, table });
    }
  }

  const nodeIds = new Set<string>();
  const addNode = (schemaName: string, table: SchemaTable, isExternal: boolean) => {
    const id = `${schemaName}.${table.name}`;
    if (nodeIds.has(id)) return;
    nodeIds.add(id);
    nodes.push(buildTableNode(schemaName, table, isExternal));
  };

  const isVisible = (schemaName: string) =>
    visibleSchemas === undefined || visibleSchemas.has(schemaName);

  // Base nodes: every table in a visible schema.
  for (const schema of tree.schemas) {
    if (!isVisible(schema.name)) continue;
    for (const table of schema.tables) {
      addNode(schema.name, table, false);
    }
  }

  // Edges from base tables' outbound FKs; pull in referenced tables from hidden
  // schemas (one hop) so the relationship stays drawn.
  for (const schema of tree.schemas) {
    if (!isVisible(schema.name)) continue;
    for (const table of schema.tables) {
      const sourceId = `${schema.name}.${table.name}`;
      for (const fk of table.foreignKeys) {
        const targetId = `${fk.referencedSchema}.${fk.referencedTable}`;
        if (!nodeIds.has(targetId)) {
          const referenced = tableIndex.get(targetId);
          if (referenced) {
            addNode(referenced.schemaName, referenced.table, true);
          }
        }
        edges.push({
          // constraintName is unique within the database — safe to use as the React Flow edge id.
          id: fk.constraintName,
          source: sourceId,
          target: targetId,
          // Anchor the edge at the related column rows (first column for composite keys),
          // matching the per-column handle ids rendered by TableNode.
          sourceHandle: fk.columns[0],
          targetHandle: fk.referencedColumns[0],
          label: fk.constraintName,
        });
      }
    }
  }

  return { nodes, edges };
}

import { useMemo } from "react";
import Dagre from "@dagrejs/dagre";
import { Background, Controls, MiniMap, ReactFlow } from "@xyflow/react";
import type { Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import type { SchemaTree, TableNode as TableNodeType } from "@/components/erd/buildErdModel";
import { buildErdModel } from "@/components/erd/buildErdModel";
import { TableNode } from "@/components/erd/TableNode";

const nodeTypes = { table: TableNode };

// Rough node height estimate for layout spacing: header + per-column rows.
function estimateHeight(node: TableNodeType): number {
  return 34 + node.data.columns.length * 22;
}

function layout(nodes: Array<TableNodeType>, edges: Array<Edge>): Array<TableNodeType> {
  const g = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: "LR", nodesep: 40, ranksep: 120 });

  const width = 240;
  for (const node of nodes) {
    g.setNode(node.id, { width, height: estimateHeight(node) });
  }
  for (const edge of edges) {
    g.setEdge(edge.source, edge.target);
  }

  Dagre.layout(g);

  return nodes.map((node) => {
    const { x, y } = g.node(node.id);
    const height = estimateHeight(node);
    return { ...node, position: { x: x - width / 2, y: y - height / 2 } };
  });
}

export function ErdCanvas({ tree }: { tree: SchemaTree }) {
  const { nodes, edges } = useMemo(() => {
    const model = buildErdModel(tree);
    return { nodes: layout(model.nodes, model.edges), edges: model.edges };
  }, [tree]);

  return (
    <ReactFlow
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      fitView
      minZoom={0.1}
      proOptions={{ hideAttribution: true }}
    >
      <Background />
      <Controls />
      <MiniMap pannable zoomable />
    </ReactFlow>
  );
}

export default ErdCanvas;

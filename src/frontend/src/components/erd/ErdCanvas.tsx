import { useEffect } from "react";
import Dagre from "@dagrejs/dagre";
import { Background, Controls, MiniMap, ReactFlow, useEdgesState, useNodesState } from "@xyflow/react";
import type { Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import type { SchemaTree, TableNode as TableNodeType } from "@/components/erd/buildErdModel";
import { buildErdModel } from "@/components/erd/buildErdModel";
import { TableNode } from "@/components/erd/TableNode";

const nodeTypes = { table: TableNode };

// Edge defaults: orthogonal routing plus a readable, backed label so foreign-key
// constraint names stand out against the diagram instead of being clipped on the line.
const defaultEdgeOptions = {
  type: "smoothstep" as const,
  labelShowBg: true,
  labelBgPadding: [6, 3] as [number, number],
  labelBgBorderRadius: 4,
  labelStyle: { fontSize: 11, fill: "var(--mantine-color-text)" },
  labelBgStyle: { fill: "var(--mantine-color-body)", fillOpacity: 0.95 },
  style: { stroke: "var(--mantine-color-dimmed)" },
};

// Rough node height estimate for layout spacing: header + per-column rows.
function estimateHeight(node: TableNodeType): number {
  return 34 + node.data.columns.length * 22;
}

function layout(nodes: Array<TableNodeType>, edges: Array<Edge>): Array<TableNodeType> {
  const g = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  // Wide ranksep leaves room for the foreign-key label to sit in the gap between
  // tables rather than overlapping them.
  g.setGraph({ rankdir: "LR", nodesep: 50, ranksep: 240 });

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
  // Controlled state with change handlers so nodes are draggable and React Flow can
  // record measured dimensions (which the minimap needs to render node rectangles).
  const [nodes, setNodes, onNodesChange] = useNodesState<TableNodeType>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);

  useEffect(() => {
    const model = buildErdModel(tree);
    setNodes(layout(model.nodes, model.edges));
    setEdges(model.edges);
  }, [tree, setNodes, setEdges]);

  return (
    <ReactFlow
      nodes={nodes}
      edges={edges}
      onNodesChange={onNodesChange}
      onEdgesChange={onEdgesChange}
      nodeTypes={nodeTypes}
      defaultEdgeOptions={defaultEdgeOptions}
      fitView
      minZoom={0.1}
      proOptions={{ hideAttribution: true }}
    >
      <Background />
      <Controls />
      <MiniMap pannable zoomable nodeColor="var(--mantine-color-blue-4)" nodeStrokeWidth={2} />
    </ReactFlow>
  );
}

export default ErdCanvas;

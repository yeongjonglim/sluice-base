import { useMemo, useState } from "react";
import { SegmentedControl, Stack } from "@mantine/core";
import type { PlanMetric, PlanNode } from "@/utils/queryPlan";
import { PlanNodeRow } from "@/components/query/PlanNodeRow";
import { flattenNodes, hasActuals, inclusiveWeight, nodeWeight } from "@/utils/queryPlan";

const AUTO_COLLAPSE_NODE_COUNT = 25;
const NEGLIGIBLE_SHARE = 0.01;

function initialCollapsed(root: PlanNode, metric: PlanMetric): Set<string> {
  const all = flattenNodes(root);
  if (all.length <= AUTO_COLLAPSE_NODE_COUNT) return new Set();
  const rootWeight = inclusiveWeight(root, metric) || 1;
  const collapsed = new Set<string>();
  for (const n of all) {
    if (n.id !== root.id && n.children.length > 0 && inclusiveWeight(n, metric) < NEGLIGIBLE_SHARE * rootWeight) {
      collapsed.add(n.id);
    }
  }
  return collapsed;
}

export function PlanTree({ root }: { root: PlanNode }) {
  const analyzed = hasActuals(root);
  const [metric, setMetric] = useState<PlanMetric>(analyzed ? "time" : "cost");
  const [collapsed, setCollapsed] = useState<Set<string>>(() => initialCollapsed(root, analyzed ? "time" : "cost"));

  const { maxWeight, hottestId } = useMemo(() => {
    let max = 0;
    let hottest = root.id;
    for (const n of flattenNodes(root)) {
      const w = nodeWeight(n, metric);
      if (w > max) {
        max = w;
        hottest = n.id;
      }
    }
    return { maxWeight: max, hottestId: hottest };
  }, [root, metric]);

  const rows = useMemo(() => {
    const out: Array<{ node: PlanNode; depth: number }> = [];
    const walk = (n: PlanNode, depth: number) => {
      out.push({ node: n, depth });
      if (!collapsed.has(n.id)) n.children.forEach((c) => walk(c, depth + 1));
    };
    walk(root, 0);
    return out;
  }, [root, collapsed]);

  const toggle = (id: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <Stack gap={4}>
      <SegmentedControl
        size="xs"
        value={metric}
        onChange={(v) => setMetric(v as PlanMetric)}
        data={[
          ...(analyzed ? [{ label: "Time", value: "time" }] : []),
          { label: "Rows", value: "rows" },
          { label: "Cost", value: "cost" },
        ]}
        style={{ alignSelf: "flex-start" }}
      />
      <Stack gap={0}>
        {rows.map(({ node, depth }) => (
          <PlanNodeRow
            key={node.id}
            node={node}
            depth={depth}
            weightShare={maxWeight > 0 ? nodeWeight(node, metric) / maxWeight : 0}
            isHottest={node.id === hottestId && maxWeight > 0}
            hasChildren={node.children.length > 0}
            collapsed={collapsed.has(node.id)}
            onToggle={() => toggle(node.id)}
          />
        ))}
      </Stack>
    </Stack>
  );
}

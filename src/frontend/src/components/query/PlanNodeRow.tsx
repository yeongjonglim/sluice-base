import { ActionIcon, Badge, Box, Group, Text } from "@mantine/core";
import { IconChevronRight } from "@tabler/icons-react";
import type { PlanMetric, PlanNode } from "@/utils/queryPlan";
import { hasActuals, isMisestimated } from "@/utils/queryPlan";

function fmt(n: number): string {
  return new Intl.NumberFormat().format(Math.round(n));
}

// Green → orange → red by share of the heaviest node.
function heatColor(share: number): string {
  if (share >= 0.66) return "var(--mantine-color-red-6)";
  if (share >= 0.33) return "var(--mantine-color-orange-5)";
  return "var(--mantine-color-green-5)";
}

export function PlanNodeRow({
  node,
  depth,
  weightShare,
  isHottest,
  hasChildren,
  collapsed,
  onToggle,
}: {
  node: PlanNode;
  depth: number;
  metric: PlanMetric;
  weightShare: number;
  isHottest: boolean;
  hasChildren: boolean;
  collapsed: boolean;
  onToggle: () => void;
}) {
  const analyzed = hasActuals(node);
  return (
    <Box
      style={{
        paddingLeft: depth * 16 + 4,
        borderLeft: isHottest ? "2px solid var(--mantine-color-red-6)" : "2px solid transparent",
      }}
    >
      <Group gap={6} wrap="nowrap" align="center" py={2}>
        {hasChildren ? (
          <ActionIcon
            variant="subtle"
            size="xs"
            color="gray"
            onClick={onToggle}
            aria-label={collapsed ? "expand node" : "collapse node"}
          >
            <IconChevronRight
              size={12}
              style={{ transform: collapsed ? "none" : "rotate(90deg)", transition: "transform 120ms" }}
            />
          </ActionIcon>
        ) : (
          <Box w={18} style={{ flexShrink: 0 }} />
        )}

        <Text size="xs" fw={600} style={{ whiteSpace: "nowrap" }}>
          {node.nodeType}
        </Text>
        {node.object && (
          <Text size="xs" c="dimmed" style={{ whiteSpace: "nowrap" }}>
            · {node.object}
          </Text>
        )}

        {/* heat bar */}
        <Box
          style={{
            flex: 1,
            minWidth: 40,
            height: 6,
            background: "var(--mantine-color-default-border)",
            borderRadius: 3,
          }}
        >
          <Box
            style={{
              width: `${Math.max(2, weightShare * 100)}%`,
              height: "100%",
              background: heatColor(weightShare),
              borderRadius: 3,
            }}
          />
        </Box>

        <Text size="xs" c="dimmed" style={{ whiteSpace: "nowrap" }}>
          {analyzed
            ? `${(node.selfTimeMs ?? 0).toFixed(1)} ms · ${fmt(node.actualRows ?? 0)} rows${
                (node.actualLoops ?? 1) > 1 ? ` · ${fmt(node.actualLoops ?? 1)} loops` : ""
              }`
            : `cost ${fmt(node.totalCost)} · ~${fmt(node.planRows)} rows`}
        </Text>

        {node.isSeqScan && (
          <Badge variant="light" color="orange" size="xs">
            Full Table Scan
          </Badge>
        )}
        {isMisestimated(node) && (
          <Badge variant="light" color="red" size="xs">
            est {fmt(node.planRows)} vs actual {fmt(node.actualRows ?? 0)} ({Math.round(node.misestimateFactor ?? 0)}×)
          </Badge>
        )}
      </Group>
    </Box>
  );
}

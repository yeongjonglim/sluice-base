import { Box, Code, Group, Text } from "@mantine/core";
import { IconKey, IconLink, IconLock } from "@tabler/icons-react";
import { Handle, Position } from "@xyflow/react";
import type { NodeProps } from "@xyflow/react";
import type { TableNode as TableNodeType } from "@/components/erd/buildErdModel";

export function TableNode({ data }: NodeProps<TableNodeType>) {
  return (
    <Box
      style={{
        border: "1px solid var(--mantine-color-default-border)",
        borderRadius: "var(--mantine-radius-sm)",
        background: "var(--mantine-color-body)",
        minWidth: 220,
        overflow: "hidden",
      }}
    >
      <Box
        px="sm"
        py={6}
        style={{
          background: "var(--mantine-color-default-hover)",
          borderBottom: "1px solid var(--mantine-color-default-border)",
        }}
      >
        <Text size="sm" fw={600}>
          {data.schema}.{data.table}
        </Text>
      </Box>
      {data.columns.map((col) => (
        // position: relative anchors this column's handles at the row's height, so a
        // foreign-key edge leaves/enters at the related column instead of the node centre.
        <Group
          key={col.name}
          px="sm"
          py={3}
          gap="xs"
          wrap="nowrap"
          justify="space-between"
          style={{ position: "relative" }}
        >
          <Handle type="target" position={Position.Left} id={col.name} style={{ opacity: 0 }} />
          <Handle type="source" position={Position.Right} id={col.name} style={{ opacity: 0 }} />
          <Group gap={6} wrap="nowrap">
            {col.isPrimaryKey && <IconKey size={13} color="var(--mantine-color-yellow-6)" />}
            {col.isForeignKey && <IconLink size={13} color="var(--mantine-color-blue-5)" />}
            <Text size="xs">{col.name}</Text>
            {col.isRestricted && <IconLock size={12} color="var(--mantine-color-dimmed)" />}
          </Group>
          <Code fz={10}>{col.dataType}</Code>
        </Group>
      ))}
    </Box>
  );
}

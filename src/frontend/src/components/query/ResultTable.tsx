import { useMemo, useRef, useState } from "react";
import { Button, CloseButton, Flex, Group, Highlight, Table, Text, TextInput } from "@mantine/core";
import { IconDownload, IconSearch } from "@tabler/icons-react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { CSSProperties, PointerEvent as ReactPointerEvent } from "react";
import { useColumnWidths } from "@/components/query/useColumnWidths";
import { exportToCsv } from "@/utils/csv.ts";
import { filterRows } from "@/utils/filterRows";

// Only ~viewport-worth of rows are ever in the DOM, so switching to a tab with a
// huge result set — and scrolling it — stays fast regardless of row count. The
// trade-off is that the browser's native find-in-page can't see off-screen rows,
// so the filter box below replaces it: it matches across the FULL result (held
// in JS) and narrows the grid to matching rows. Rendering uses the real Mantine
// Table with "spacer" rows above/below the window so the styling (borders,
// striping, sticky header) matches a normal, non-virtualized table.

const ROW_HEIGHT = 33; // initial estimate; real heights are measured per row

const ELLIPSIS: CSSProperties = {
  whiteSpace: "nowrap",
  overflow: "hidden",
  textOverflow: "ellipsis",
};

function ColumnResizeHandle({
  label,
  onPointerDown,
  onDoubleClick,
}: {
  label: string;
  onPointerDown: (event: ReactPointerEvent) => void;
  onDoubleClick: () => void;
}) {
  return (
    <div
      role="separator"
      aria-orientation="vertical"
      aria-label={`Resize ${label} column`}
      onPointerDown={onPointerDown}
      onDoubleClick={onDoubleClick}
      style={{
        position: "absolute",
        top: 0,
        right: 0,
        height: "100%",
        width: 6,
        cursor: "col-resize",
        touchAction: "none",
        userSelect: "none",
      }}
    />
  );
}

export function ResultTable({
  columns,
  rows,
  rowCount,
  durationMs,
  resultIndex,
}: {
  columns: Array<string>;
  rows: Array<Array<string | null>>;
  rowCount: number;
  durationMs: number;
  resultIndex: number;
}) {
  const [query, setQuery] = useState("");
  const filtering = query.trim() !== "";
  const filtered = useMemo(() => filterRows(rows, query), [rows, query]);
  const { widths, totalWidth, onResizeStart, onAutoFit } = useColumnWidths(columns, rows);

  const scrollRef = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({
    count: filtered.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 12,
  });

  const virtualItems = virtualizer.getVirtualItems();
  const paddingTop = virtualItems.length > 0 ? virtualItems[0].start : 0;
  const paddingBottom =
    virtualItems.length > 0
      ? virtualizer.getTotalSize() - virtualItems[virtualItems.length - 1].end
      : 0;

  return (
    <Flex direction="column" style={{ height: "100%" }}>
      <Group
        justify="space-between"
        align="center"
        px="xs"
        gap="xs"
        style={{
          flexShrink: 0,
          minHeight: 36,
          borderBottom: "1px solid var(--mantine-color-default-border)",
        }}
      >
        <Text size="xs" c="dimmed" style={{ flexShrink: 0, fontVariantNumeric: "tabular-nums" }}>
          {filtering ? `${filtered.length} of ${rowCount} rows` : `${rowCount} ${rowCount === 1 ? "row" : "rows"}`}
          {" · "}
          {durationMs} ms
        </Text>
        <TextInput
          size="xs"
          flex={1}
          maw={280}
          placeholder="Filter rows…"
          aria-label="Filter rows"
          leftSection={<IconSearch size={12} />}
          value={query}
          onChange={(e) => setQuery(e.currentTarget.value)}
          rightSection={
            query ? (
              <CloseButton size="xs" onClick={() => setQuery("")} aria-label="Clear filter" />
            ) : null
          }
        />
        <Button
          size="xs"
          variant="subtle"
          leftSection={<IconDownload size={12} />}
          onClick={() =>
            exportToCsv(columns, filtered, `query-results-${resultIndex + 1}.csv`)
          }
        >
          CSV
        </Button>
      </Group>

      <div ref={scrollRef} style={{ flex: 1, minHeight: 0, overflow: "auto" }}>
        <Table
          stickyHeader
          withTableBorder
          withColumnBorders
          fz="xs"
          style={{ tableLayout: "fixed", width: totalWidth }}
        >
          <colgroup>
            {widths.map((w, i) => (
              <col key={i} style={{ width: w }} />
            ))}
          </colgroup>
          <Table.Thead>
            <Table.Tr>
              {columns.map((col, i) => (
                <Table.Th key={col} style={{ ...ELLIPSIS, position: "relative" }}>
                  {col}
                  <ColumnResizeHandle
                    label={col}
                    onPointerDown={(e) => onResizeStart(i, e)}
                    onDoubleClick={() => onAutoFit(i)}
                  />
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {paddingTop > 0 && (
              <Table.Tr aria-hidden>
                <Table.Td colSpan={columns.length} style={{ height: paddingTop, padding: 0, border: 0 }} />
              </Table.Tr>
            )}
            {virtualItems.map((vi) => {
              const row = filtered[vi.index];
              return (
                <Table.Tr
                  key={vi.key}
                  data-index={vi.index}
                  ref={virtualizer.measureElement}
                  bg={vi.index % 2 === 1 ? "var(--mantine-color-default-hover)" : undefined}
                >
                  {row.map((cell, j) => (
                    <Table.Td key={j} style={ELLIPSIS}>
                      {cell === null ? (
                        <Text size="xs" c="dimmed" fs="italic">
                          NULL
                        </Text>
                      ) : filtering ? (
                        <Highlight span inherit highlight={query.trim()}>
                          {cell}
                        </Highlight>
                      ) : (
                        cell
                      )}
                    </Table.Td>
                  ))}
                </Table.Tr>
              );
            })}
            {paddingBottom > 0 && (
              <Table.Tr aria-hidden>
                <Table.Td colSpan={columns.length} style={{ height: paddingBottom, padding: 0, border: 0 }} />
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>

        {filtered.length === 0 && (
          <Text p="xs" size="xs" c="dimmed">
            {filtering ? `No rows match “${query.trim()}”.` : "No rows returned."}
          </Text>
        )}
      </div>
    </Flex>
  );
}

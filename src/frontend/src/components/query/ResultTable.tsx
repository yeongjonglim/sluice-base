import { useEffect, useMemo, useRef, useState } from "react";
import { Button, CloseButton, Flex, Group, Highlight, Table, Text, TextInput } from "@mantine/core";
import { IconDownload, IconSearch, IconTextWrap } from "@tabler/icons-react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { CSSProperties, PointerEvent as ReactPointerEvent } from "react";
import type { PanelAnchor, PanelSide } from "@/components/query/CellValuePopover";
import { CellValuePopover } from "@/components/query/CellValuePopover";
import classes from "@/components/query/ResultTable.module.css";
import { useColumnWidths } from "@/components/query/useColumnWidths";
import { exportToCsv } from "@/utils/csv.ts";
import { filterRows } from "@/utils/filterRows";
import { isJsonDocument } from "@/utils/jsonValue";

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

// Wrapping is opt-in: it is the fastest way to read several long values at once,
// at the cost of the grid's uniform row rhythm. `overflow-wrap: anywhere` covers
// the unbroken strings (URLs, JSON) that would otherwise widen the cell.
const WRAP: CSSProperties = {
  whiteSpace: "pre-wrap",
  overflowWrap: "anywhere",
};

// The value panel is for what the grid can't show: text cut off at the column
// edge, line breaks that one-line cells flatten, and JSON (which gets a tree).
// A value that already reads in full stays plain, selectable text.
function needsValuePanel(cell: HTMLTableCellElement, value: string, wrapped: boolean) {
  // Cheap layout checks first: this also runs on hover, and parsing a large
  // JSON value is the expensive part.
  if (!wrapped && (cell.scrollWidth > cell.clientWidth || value.includes("\n"))) return true;
  return isJsonDocument(value);
}

// The panel opens below its cell and shrinks to the room there; it only goes
// above when the cell is so low that the value would get a sliver, since above
// is the editor and toolbars the user is working with.
const MIN_PANEL_ROOM = 200;
function panelPlacement(
  cell: HTMLTableCellElement,
  grid: HTMLElement | null,
): { side: PanelSide; anchor?: PanelAnchor } {
  // A wrapped cell can be taller than the grid's viewport. Anchored to the whole
  // cell, the panel would sit past the clipped edge, out of sight, so it anchors
  // to the part of the cell the user can actually see.
  const rect = cell.getBoundingClientRect();
  const clip = grid?.getBoundingClientRect();
  const top = Math.max(rect.top, clip?.top ?? rect.top, 0);
  const bottom = Math.min(rect.bottom, clip?.bottom ?? rect.bottom, window.innerHeight);
  const below = window.innerHeight - bottom;
  const side = below >= MIN_PANEL_ROOM || below >= top ? "bottom" : "top";
  const clipped = top > rect.top || bottom < rect.bottom;
  return clipped ? { side, anchor: { top: top - rect.top, height: bottom - top } } : { side };
}

// Closing returns focus to the cell only if it was in the panel or the grid;
// Escape or a scroll while typing in the filter or the editor should leave the
// caret where it is.
function focusIsInPanelOrGrid(grid: HTMLElement | null) {
  const active = document.activeElement;
  return (
    active === null ||
    active === document.body ||
    active.closest("[data-cell-value-panel]") !== null ||
    grid?.contains(active) === true
  );
}

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

  // Which cell's full value is open. Kept as indices, so a fresh result (same
  // columns, different rows) would leave the popover pointing at the wrong row —
  // close it when the result data changes, mirroring the column-widths reset.
  // The indices point into the filtered rows, so a filter change closes it too.
  const [inspected, setInspected] = useState<{
    row: number;
    col: number;
    side: PanelSide;
    anchor?: PanelAnchor;
  } | null>(null);
  const [wrap, setWrap] = useState(false);
  const filteredRef = useRef(filtered);
  if (filteredRef.current !== filtered) {
    filteredRef.current = filtered;
    if (inspected) setInspected(null);
  }

  // The cell that opened the popover, so focus can go back to it on close.
  const cellRef = useRef<HTMLTableCellElement | null>(null);
  function closeInspector({ returnFocus = true } = {}) {
    setInspected(null);
    // preventScroll: the cell may have scrolled just out of view; returning focus
    // must not yank the grid back to it.
    if (returnFocus) cellRef.current?.focus({ preventScroll: true });
  }

  // Escape closes the panel wherever focus is (clicking the grid moves focus out
  // of the panel), and a press anywhere outside the panel and the grid's cells
  // closes it. Presses on cells are left to the cell's click handler, which
  // toggles the open cell and moves the panel to another in one step.
  const isInspecting = inspected !== null;
  useEffect(() => {
    if (!isInspecting) return;
    function onPointerDown(event: Event) {
      const target = event.target;
      if (!(target instanceof Element)) return;
      if (target.closest("[data-cell-value-panel]")) return;
      if (scrollRef.current?.contains(target) && target.closest(`.${classes.cell}`)) return;
      // Focus is going wherever the user pressed, so don't pull it back to the cell.
      setInspected(null);
    }
    function onKeyDown(event: KeyboardEvent) {
      // An Escape another control used (closing the editor's autocomplete, a
      // menu) is not meant for the panel.
      if (event.key !== "Escape" || event.defaultPrevented) return;
      setInspected(null);
      if (focusIsInPanelOrGrid(scrollRef.current)) cellRef.current?.focus({ preventScroll: true });
    }
    // pointerdown, not mousedown: controls that preventDefault their pointerdown
    // (the column resize handle) suppress the mousedown the browser would send.
    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown, true);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [isInspecting]);

  const scrollRef = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({
    count: filtered.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 12,
  });
  const cellStyle = wrap ? WRAP : ELLIPSIS;

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
          variant={wrap ? "light" : "subtle"}
          leftSection={<IconTextWrap size={12} />}
          aria-pressed={wrap}
          onClick={() => setWrap((value) => !value)}
        >
          Wrap
        </Button>
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

      <div
        ref={scrollRef}
        style={{ flex: 1, minHeight: 0, overflow: "auto" }}
        onScroll={() => {
          // The popover is anchored to a cell that virtualization can recycle;
          // scrolling away from it would leave the panel floating over the wrong
          // row, so dismiss instead of tracking.
          if (inspected) closeInspector({ returnFocus: focusIsInPanelOrGrid(scrollRef.current) });
        }}
      >
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
                // The resize handle (position: absolute) anchors to this cell's own
                // position: sticky, which Mantine sets on th under border-collapse
                // (see Table.css). Do NOT add position: relative here — it would
                // override the sticky positioning and unpin the header on scroll.
                <Table.Th key={col} style={ELLIPSIS}>
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
                  {row.map((cell, j) => {
                    const content =
                      cell === null ? (
                        <Text size="xs" c="dimmed" fs="italic">
                          NULL
                        </Text>
                      ) : filtering ? (
                        <Highlight span inherit highlight={query.trim()}>
                          {cell}
                        </Highlight>
                      ) : (
                        cell
                      );
                    const open = inspected !== null && inspected.row === vi.index && inspected.col === j;
                    return (
                      <Table.Td
                        key={j}
                        className={classes.cell}
                        style={cellStyle}
                        data-inspected={open || undefined}
                        // Always programmatically focusable so focus can return to the
                        // cell after its panel closes (tabIndex -1 keeps cells out of
                        // the tab order; only the panel and toolbar are tab stops).
                        tabIndex={-1}
                        // Whether a cell opens depends on its laid-out width, so the
                        // cue is worked out when the pointer arrives, not per render.
                        onPointerEnter={(event) => {
                          const target = event.currentTarget;
                          target.toggleAttribute(
                            "data-openable",
                            cell !== null && needsValuePanel(target, cell, wrap),
                          );
                        }}
                        onClick={(event) => {
                          // A drag-select (or double-click word select) inside a
                          // cell ends in a click too; don't open the value over the
                          // text the user just selected.
                          if (window.getSelection()?.toString()) {
                            if (inspected) closeInspector({ returnFocus: false });
                            return;
                          }
                          if (open) {
                            closeInspector();
                            return;
                          }
                          if (cell === null || !needsValuePanel(event.currentTarget, cell, wrap)) {
                            if (inspected) closeInspector({ returnFocus: false });
                            return;
                          }
                          cellRef.current = event.currentTarget;
                          setInspected({
                            row: vi.index,
                            col: j,
                            ...panelPlacement(event.currentTarget, scrollRef.current),
                          });
                        }}
                      >
                        {content}
                        {open && cell !== null && (
                          <CellValuePopover
                            column={columns[j]}
                            rowNumber={vi.index + 1}
                            value={cell}
                            side={inspected.side}
                            anchor={inspected.anchor}
                            onClose={closeInspector}
                          />
                        )}
                      </Table.Td>
                    );
                  })}
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

import {
  ActionIcon,
  Badge,
  Box,
  Button,
  CloseButton,
  CopyButton,
  Group,
  Popover,
  SegmentedControl,
  Text,
  Tooltip,
} from "@mantine/core";
import { IconCheck, IconChevronsDown, IconChevronsUp, IconCopy } from "@tabler/icons-react";
import { useEffect, useMemo, useRef, useState } from "react";
import classes from "@/components/query/CellValuePopover.module.css";
import { JsonTree } from "@/components/query/JsonTree";
import { readJsonValue } from "@/utils/jsonValue";

const MAX_VALUE_HEIGHT = "min(360px, 45vh)";
// Gap kept between the panel and the viewport edge when it shrinks to fit.
const VIEWPORT_PADDING = 8;

export type PanelSide = "bottom" | "top";
/** Vertical band of the cell, in px from its top, that the panel anchors to. */
export type PanelAnchor = { top: number; height: number };

/**
 * A cell's full value in a popover, for values the grid has to truncate.
 *
 * The grid sizes columns to their content and caps them so the table stays
 * scannable; anything past the cap is ellipsized. This is the escape hatch:
 * the complete value, hard-wrapped in a monospace block, with copy. It has to
 * live in a portal — the result grid is an overflow container, so an in-flow
 * panel would be clipped by it.
 *
 * JSON gets a tree: compact `jsonb` arrives as one long line and `jsonb_pretty`
 * arrives indented but is flattened by the grid's one-line cells; here every
 * object and array can be folded, and the Formatted/Raw control switches
 * between the tree and the exact text. Folding is a reading aid, never a
 * truncation — Copy always takes the complete value.
 */
export function CellValuePopover({
  column,
  rowNumber,
  value,
  side,
  anchor,
  onClose,
}: {
  column: string;
  rowNumber: number;
  value: string;
  side: PanelSide;
  anchor?: PanelAnchor;
  onClose: () => void;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const json = useMemo(() => readJsonValue(value), [value]);
  const [view, setView] = useState<"formatted" | "raw">("formatted");
  const [collapsed, setCollapsed] = useState<Set<number>>(() => new Set());

  const canToggleView = json !== null && json.formatted !== value;
  const allCollapsed =
    json !== null &&
    json.collapsibleIds.length > 0 &&
    json.collapsibleIds.every((id) => collapsed.has(id));
  const hasFolded =
    json !== null &&
    view === "formatted" &&
    json.collapsibleIds.some((id) => collapsed.has(id));
  const copyValue = json !== null && view === "formatted" ? json.formatted : value;

  // The panel is a non-modal dialog: focus moves into it so Escape and Tab work
  // without reaching for the mouse, and so screen readers announce the value.
  // The grid puts focus back on the cell when the popover closes.
  useEffect(() => {
    // The dropdown mounts through Mantine's Transition, so its ref is only
    // available after the first frame.
    const frame = requestAnimationFrame(() => panelRef.current?.focus());
    return () => cancelAnimationFrame(frame);
  }, []);

  function toggleNode(id: number) {
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    if (json === null) return;
    setCollapsed(allCollapsed ? new Set() : new Set(json.collapsibleIds));
  }

  return (
    // Dismissal (Escape, clicks elsewhere, re-clicking the cell) is owned by the
    // grid: Mantine's click-outside would treat a click on the anchoring cell as
    // "outside" and close-then-reopen it, and its Escape only fires while focus
    // is inside the panel.
    <Popover
      opened
      closeOnClickOutside={false}
      closeOnEscape={false}
      position={side === "bottom" ? "bottom-start" : "top-start"}
      // The grid picks the side (see ResultTable); instead of flipping when the
      // full height doesn't fit, the panel shrinks and its value box scrolls.
      middlewares={{
        flip: false,
        shift: true,
        size: {
          padding: VIEWPORT_PADDING,
          apply({ availableHeight, elements }) {
            elements.floating.style.maxHeight = `${Math.max(availableHeight, 0)}px`;
          },
        },
      }}
      offset={4}
      withArrow
      arrowSize={7}
      shadow="md"
      radius="sm"
      withinPortal
      withRoles={false}
    >
      <Popover.Target>
        {/* Covers the cell without wrapping its content, so the cell keeps its
            ellipsis and its text is not remounted (which would drop a
            double-click word selection). */}
        <span
          aria-hidden
          className={classes.anchor}
          style={anchor && { top: anchor.top, bottom: "auto", height: anchor.height }}
        />
      </Popover.Target>
      <Popover.Dropdown
        ref={panelRef}
        p="sm"
        miw={280}
        maw="min(560px, calc(100vw - 16px))"
        // JSON reads better in a fixed frame: formatted and raw forms have very
        // different line lengths, and an auto-sized panel would jump when toggled.
        style={{
          display: "flex",
          flexDirection: "column",
          ...(json !== null && { width: "min(480px, calc(100vw - 16px))" }),
        }}
        tabIndex={-1}
        role="dialog"
        aria-modal={false}
        aria-label={`${column} value, row ${rowNumber}`}
        data-cell-value-panel
        // The dropdown is a React child of the cell that opened it, so clicks in
        // the panel would bubble to the cell's click handler (re-focusing the
        // cell and pulling focus out of the panel). Keep them inside.
        onClick={(event) => event.stopPropagation()}
      >
        <Group justify="space-between" gap="sm" wrap="nowrap" mb={6} style={{ flexShrink: 0 }}>
          <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
            <Text size="xs" fw={600} truncate>
              {column}
            </Text>
            {json !== null && (
              <Badge size="md" variant="default" radius="sm" fw={500}>
                JSON
              </Badge>
            )}
            {json !== null && json.collapsibleIds.length > 0 && (
              <Tooltip label={allCollapsed ? "Expand all" : "Collapse all"} withArrow>
                <ActionIcon
                  size="xs"
                  variant="subtle"
                  color="gray"
                  aria-label={allCollapsed ? "Expand all" : "Collapse all"}
                  onClick={toggleAll}
                >
                  {allCollapsed ? <IconChevronsDown size={12} /> : <IconChevronsUp size={12} />}
                </ActionIcon>
              </Tooltip>
            )}
          </Group>
          <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
            <Text size="xs" c="dimmed" style={{ fontVariantNumeric: "tabular-nums" }}>
              row {rowNumber} · {value.length.toLocaleString()} chars
            </Text>
            <Tooltip label="Close (Esc)" withArrow>
              <CloseButton size="xs" aria-label="Close" onClick={() => onClose()} />
            </Tooltip>
          </Group>
        </Group>
        <Box
          className={classes.scroll}
          tabIndex={0}
          role="group"
          aria-label={`Full ${column} value`}
          style={{
            maxHeight: MAX_VALUE_HEIGHT,
            minHeight: 0,
            overflow: "auto",
            overscrollBehavior: "contain",
          }}
        >
          {json !== null && view === "formatted" ? (
            <JsonTree value={json} collapsed={collapsed} onToggle={toggleNode} />
          ) : (
            <Text
              component="pre"
              ff="monospace"
              fz={13}
              style={{
                margin: 0,
                whiteSpace: "pre-wrap",
                overflowWrap: "anywhere",
                lineHeight: 1.6,
              }}
            >
              {value}
            </Text>
          )}
        </Box>
        <Group
          justify={canToggleView ? "space-between" : "flex-end"}
          mt={8}
          gap="xs"
          wrap="nowrap"
          style={{ flexShrink: 0 }}
        >
          {canToggleView && (
            <SegmentedControl
              size="xs"
              value={view}
              onChange={setView}
              data={[
                { label: "Formatted", value: "formatted" },
                { label: "Raw", value: "raw" },
              ]}
              aria-label="JSON view"
            />
          )}
          {/* Remount on view change so a stale "Copied" cannot describe the other form. */}
          <CopyButton key={view} value={copyValue} timeout={1500}>
            {({ copied, copy }) => (
              <Button
                variant={copied ? "light" : "default"}
                size="xs"
                leftSection={copied ? <IconCheck size={12} /> : <IconCopy size={12} />}
                onClick={copy}
                aria-label={hasFolded ? `Copy full ${column} value` : `Copy ${column} value`}
              >
                {copied ? "Copied" : hasFolded ? "Copy full" : "Copy"}
              </Button>
            )}
          </CopyButton>
        </Group>
      </Popover.Dropdown>
    </Popover>
  );
}

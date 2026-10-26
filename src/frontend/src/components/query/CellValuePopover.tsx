import {
  ActionIcon,
  Badge,
  Box,
  Button,
  CopyButton,
  Group,
  Popover,
  SegmentedControl,
  Text,
  Tooltip,
} from "@mantine/core";
import { IconCheck, IconChevronsDown, IconChevronsUp, IconCopy } from "@tabler/icons-react";
import { useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import classes from "@/components/query/CellValuePopover.module.css";
import { JsonTree } from "@/components/query/JsonTree";
import { readJsonValue } from "@/utils/jsonValue";

const MAX_VALUE_HEIGHT = "min(360px, 45vh)";

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
  onClose,
  children,
}: {
  column: string;
  rowNumber: number;
  value: string;
  onClose: () => void;
  children: ReactNode;
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
    <Popover
      opened
      onChange={(opened) => {
        if (!opened) onClose();
      }}
      position="bottom-start"
      offset={4}
      withArrow
      arrowSize={7}
      shadow="md"
      radius="sm"
      withinPortal
      withRoles={false}
    >
      <Popover.Target>
        <span style={{ display: "block" }}>{children}</span>
      </Popover.Target>
      <Popover.Dropdown
        ref={panelRef}
        p="sm"
        miw={280}
        maw="min(560px, calc(100vw - 16px))"
        // JSON reads better in a fixed frame: formatted and raw forms have very
        // different line lengths, and an auto-sized panel would jump when toggled.
        style={json !== null ? { width: "min(480px, calc(100vw - 16px))" } : undefined}
        tabIndex={-1}
        role="dialog"
        aria-modal={false}
        aria-label={`${column} value, row ${rowNumber}`}
        // The dropdown is a React child of the cell that opened it, so clicks in
        // the panel would bubble to the cell's click handler (re-focusing the
        // cell and pulling focus out of the panel). Keep them inside.
        onClick={(event) => event.stopPropagation()}
      >
        <Group justify="space-between" gap="sm" wrap="nowrap" mb={6}>
          <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
            <Text size="xs" fw={600} truncate>
              {column}
            </Text>
            {json !== null && (
              <Badge size="xs" variant="default" radius="sm" fw={500}>
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
          <Text
            size="xs"
            c="dimmed"
            style={{ flexShrink: 0, fontVariantNumeric: "tabular-nums" }}
          >
            row {rowNumber} · {value.length.toLocaleString()} chars
          </Text>
        </Group>
        <Box
          className={classes.scroll}
          tabIndex={0}
          role="group"
          aria-label={`Full ${column} value`}
          style={{
            maxHeight: MAX_VALUE_HEIGHT,
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

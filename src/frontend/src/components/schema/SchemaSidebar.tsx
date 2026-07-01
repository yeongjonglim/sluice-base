import {
  ActionIcon,
  Alert,
  Box,
  Flex,
  Group,
  NavLink,
  Skeleton,
  Stack,
  Text,
  Tooltip,
} from "@mantine/core";
import {
  IconBinaryTree2,
  IconBraces,
  IconChevronRight,
  IconDatabase,
  IconEye,
  IconKey,
  IconLink,
  IconListNumbers,
  IconLock,
  IconMathFunction,
  IconPlaylistAdd,
  IconPointFilled,
  IconPuzzle,
  IconShieldLock,
  IconStack2,
  IconTable,
} from "@tabler/icons-react";
import { cloneElement, useRef, useState } from "react";
import type { CSSProperties, MouseEvent, ReactElement, ReactNode, Ref } from "react";
import type { useSchema } from "@/api/hooks";
import { useSessionState } from "@/utils/useSessionState";

export type TableClickHandler = (
  schemaName: string,
  tableName: string,
  columns: Array<{ name: string; isSensitive: boolean; isRestricted: boolean }>,
) => void;

// One indent step per tree level (schema → group → object → detail).
const STEP = 12;
// Fixed gutter for the disclosure chevron, so expandable and leaf rows align on the same
// left edge whether or not they can open.
const CHEVRON_SLOT = 14;

// Shows a floating tooltip with the full label, but only while the wrapped element is wider
// than the visible (scrollable) panel area. The names sit in a max-content container, so they
// never self-truncate — overflow has to be measured against the surrounding scroll viewport.
function OverflowTooltip({
  label,
  children,
}: {
  label: string;
  children: ReactElement<{
    ref?: Ref<HTMLElement>;
    onMouseEnter?: (event: MouseEvent<HTMLElement>) => void;
  }>;
}) {
  const ref = useRef<HTMLElement>(null);
  const [disabled, setDisabled] = useState(true);

  const handleMouseEnter = (event: MouseEvent<HTMLElement>) => {
    const el = ref.current;
    if (el) {
      const scroller = el.closest<HTMLElement>("[data-schema-scroll]");
      const available = scroller?.clientWidth ?? el.clientWidth;
      setDisabled(Math.ceil(el.getBoundingClientRect().width) <= available);
    }
    children.props.onMouseEnter?.(event);
  };

  return (
    <Tooltip.Floating label={label} disabled={disabled}>
      {cloneElement(children, { ref, onMouseEnter: handleMouseEnter })}
    </Tooltip.Floating>
  );
}

// Pins trailing controls to the right edge of the scroll viewport so they stay visible while a
// long name scrolls horizontally underneath them.
const stickyRight: CSSProperties = {
  position: "sticky",
  right: 0,
  marginLeft: "auto",
  flexShrink: 0,
  alignItems: "center",
  background: "var(--mantine-color-body)",
};

// The single disclosure affordance used across the whole tree: a chevron that rotates from
// pointing-right (closed) to pointing-down (open). Leaf rows render an empty slot of the same
// width so their icons line up with expandable siblings.
function Disclosure({ open, expandable }: { open: boolean; expandable: boolean }) {
  return (
    <Box
      w={CHEVRON_SLOT}
      style={{ display: "flex", justifyContent: "center", flexShrink: 0 }}
    >
      {expandable ? (
        <IconChevronRight
          size={12}
          color="var(--mantine-color-dimmed)"
          style={{
            transform: open ? "rotate(90deg)" : "none",
            transition: "transform 120ms ease",
          }}
        />
      ) : null}
    </Box>
  );
}

// A single tree row. The entire row is the click target — clicking anywhere toggles when the
// row is expandable. `detail` is secondary metadata rendered dimmed/mono after the name.
function TreeRow({
  name,
  detail,
  icon,
  depth,
  expandable = false,
  open = false,
  onToggle,
  trailing,
  leaf = false,
  emphasis = false,
  faded = false,
}: {
  name: string;
  detail?: string;
  icon: ReactNode;
  depth: number;
  expandable?: boolean;
  open?: boolean;
  onToggle?: () => void;
  trailing?: ReactNode;
  // Leaf rows (columns, indexes) sit one tier below object rows and use the smaller `xs` name
  // size so a table's columns and indexes read as the same level.
  leaf?: boolean;
  // Emphasises the row as a container anchor (used for the schema node).
  emphasis?: boolean;
  // Dims the row (used for columns the current user can't access).
  faded?: boolean;
}) {
  const tooltip = detail ? `${name} · ${detail}` : name;
  return (
    <Flex wrap="nowrap" align="center" miw="100%">
      <OverflowTooltip label={tooltip}>
        <NavLink
          onClick={onToggle}
          pl={4 + depth * STEP}
          active={false}
          leftSection={
            <Group gap={4} wrap="nowrap">
              <Disclosure open={open} expandable={expandable} />
              {icon}
            </Group>
          }
          label={
            <span
              style={{
                display: "inline-flex",
                alignItems: "baseline",
                gap: 8,
                whiteSpace: "nowrap",
                opacity: faded ? 0.55 : 1,
              }}
            >
              <Text span fz={leaf ? "xs" : "sm"} fw={emphasis ? 600 : undefined}>
                {name}
              </Text>
              {detail ? (
                <Text span fz="xs" c="dimmed" ff="monospace">
                  {detail}
                </Text>
              ) : null}
            </span>
          }
          style={{ minWidth: "100%", width: "max-content", flexShrink: 0 }}
        />
      </OverflowTooltip>
      {trailing ? (
        <Group gap={2} wrap="nowrap" pl={4} style={stickyRight}>
          {trailing}
        </Group>
      ) : null}
    </Flex>
  );
}

// A collapsible category divider (Tables, Views, …). Rendered as a quiet uppercase eyebrow so
// the grouping structure reads distinctly from the object rows it contains. Hidden when empty.
function GroupHeader({
  title,
  count,
  depth,
  open,
  onToggle,
}: {
  title: string;
  count: number;
  depth: number;
  open: boolean;
  onToggle: () => void;
}) {
  if (count === 0) return null;
  return (
    <NavLink
      onClick={onToggle}
      pl={4 + depth * STEP}
      active={false}
      leftSection={<Disclosure open={open} expandable />}
      label={
        <Text
          span
          ff="monospace"
          fz={10}
          fw={700}
          tt="uppercase"
          c="dimmed"
          style={{ letterSpacing: "0.06em", whiteSpace: "nowrap" }}
        >
          {title} · {count}
        </Text>
      }
      style={{ minWidth: "100%", width: "max-content", flexShrink: 0 }}
    />
  );
}

type Column = { name: string; dataType: string; isNullable: boolean; isSensitive: boolean; isRestricted: boolean };

// Leading icon that encodes a column's role, so every column row has the same aligned marker:
// a key for the primary key, a link for foreign keys, a quiet dot otherwise.
function columnIcon(role: "pk" | "fk" | "plain") {
  if (role === "pk") {
    return (
      <Tooltip label="Primary key" withArrow>
        <IconKey size={13} color="var(--mantine-color-yellow-6)" />
      </Tooltip>
    );
  }
  if (role === "fk") {
    return (
      <Tooltip label="Foreign key" withArrow>
        <IconLink size={12} color="var(--mantine-color-dimmed)" />
      </Tooltip>
    );
  }
  return <IconPointFilled size={9} color="var(--mantine-color-dimmed)" />;
}

// A trailing lock/shield when a column is restricted or sensitive; nothing otherwise.
function sensitivityMarker(c: Column) {
  if (c.isRestricted) {
    return (
      <Tooltip label="Restricted — you can't query this column" withArrow>
        <IconLock size={12} color="var(--mantine-color-red-6)" />
      </Tooltip>
    );
  }
  if (c.isSensitive) {
    return (
      <Tooltip label="Sensitive — left out of generated queries" withArrow>
        <IconShieldLock size={12} color="var(--mantine-color-yellow-6)" />
      </Tooltip>
    );
  }
  return null;
}

function ColumnRows({
  columns,
  depth,
  primaryKey = [],
  foreignKeys = [],
}: {
  columns: Array<Column>;
  depth: number;
  primaryKey?: Array<string>;
  foreignKeys?: Array<string>;
}) {
  const pk = new Set(primaryKey);
  const fk = new Set(foreignKeys);
  return (
    <>
      {columns.map((c) => {
        const role = pk.has(c.name) ? "pk" : fk.has(c.name) ? "fk" : "plain";
        return (
          <TreeRow
            key={c.name}
            leaf
            depth={depth}
            name={c.name}
            detail={`${c.dataType}${c.isNullable ? " · null" : ""}`}
            icon={columnIcon(role)}
            faded={c.isRestricted}
            trailing={sensitivityMarker(c)}
          />
        );
      })}
    </>
  );
}

// Indexes sit under a plain, non-collapsible "Indexes" divider beneath a table's columns —
// grouped so they never interleave with columns, but without a third accordion to click
// through for the usual one or two entries. Nothing renders when there are none.
function IndexRows({
  indexes,
  depth,
}: {
  indexes: Array<{ name: string; columns: Array<string>; isUnique: boolean; isPrimary: boolean; method: string }>;
  depth: number;
}) {
  if (indexes.length === 0) return null;
  return (
    <>
      <Text
        component="div"
        ff="monospace"
        fz={10}
        fw={700}
        tt="uppercase"
        c="dimmed"
        pl={4 + depth * STEP + CHEVRON_SLOT + 4}
        pt={6}
        pb={2}
        style={{ letterSpacing: "0.06em", whiteSpace: "nowrap" }}
      >
        Indexes
      </Text>
      {indexes.map((ix) => (
        <TreeRow
          key={ix.name}
          leaf
          depth={depth}
          name={ix.name}
          detail={`${ix.columns.join(", ")}${ix.isPrimary ? " · pk" : ix.isUnique ? " · unique" : ""}`}
          icon={<IconBinaryTree2 size={13} color="var(--mantine-color-dimmed)" />}
        />
      ))}
    </>
  );
}

// A trailing "append SELECT" control for the queryable objects (tables and views). Disabled
// when every column is sensitive, since there'd be nothing to select.
function AppendSelectButton({
  onClick,
  disabled,
}: {
  onClick: () => void;
  disabled: boolean;
}) {
  return (
    <Tooltip
      label={disabled ? "All columns are sensitive" : "Append SELECT to the editor"}
      position="left"
      withArrow
    >
      <ActionIcon
        variant="subtle"
        color="gray"
        size="sm"
        disabled={disabled}
        onClick={onClick}
        aria-label="Append SELECT to the editor"
      >
        <IconPlaylistAdd size={15} />
      </ActionIcon>
    </Tooltip>
  );
}

export function SchemaSidebar({
  schema,
  onTableClick,
}: {
  schema: ReturnType<typeof useSchema>;
  onTableClick: TableClickHandler;
}) {
  const [expanded, setExpanded] = useSessionState<Array<string>>("sluice:query:expanded", []);
  const isOpen = (id: string) => expanded.includes(id);
  const toggle = (id: string) =>
    setExpanded((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));

  if (schema.isLoading) {
    return (
      <Stack gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (schema.isError) {
    return (
      <Alert color="red" title="Couldn't load schema" mt="xs">
        Check the connection and try again.
      </Alert>
    );
  }

  if (!schema.data) {
    return (
      <Text size="sm" c="dimmed" p="xs">
        Select a database to browse its schema.
      </Text>
    );
  }

  const tree = schema.data;

  return (
    <Stack gap={0}>
      {tree.schemas.map((s) => {
        const schemaId = `schema:${s.name}`;
        const gid = (kind: string) => `${schemaId}:${kind}`;
        return (
          <div key={s.name}>
            <TreeRow
              name={s.name}
              icon={<IconDatabase size={15} color="var(--mantine-primary-color-filled)" />}
              depth={0}
              expandable
              emphasis
              open={isOpen(schemaId)}
              onToggle={() => toggle(schemaId)}
            />

            {isOpen(schemaId) && (
              <>
                <GroupHeader title="Tables" count={s.tables.length} depth={1} open={isOpen(gid("tables"))} onToggle={() => toggle(gid("tables"))} />
                {isOpen(gid("tables")) &&
                  s.tables.map((t) => {
                    const id = `table:${s.name}.${t.name}`;
                    const allSensitive = t.columns.every((c) => c.isSensitive);
                    return (
                      <div key={t.name}>
                        <TreeRow
                          name={t.name}
                          icon={<IconTable size={14} color="var(--mantine-color-dimmed)" />}
                          depth={2}
                          expandable
                          open={isOpen(id)}
                          onToggle={() => toggle(id)}
                          trailing={
                            <AppendSelectButton
                              disabled={allSensitive}
                              onClick={() => onTableClick(s.name, t.name, t.columns)}
                            />
                          }
                        />
                        {isOpen(id) && (
                          <>
                            <ColumnRows
                              columns={t.columns}
                              depth={3}
                              primaryKey={t.primaryKey?.columns}
                              foreignKeys={t.foreignKeys.flatMap((fk) => fk.columns)}
                            />
                            <IndexRows indexes={t.indexes} depth={3} />
                          </>
                        )}
                      </div>
                    );
                  })}

                <GroupHeader title="Views" count={s.views.length} depth={1} open={isOpen(gid("views"))} onToggle={() => toggle(gid("views"))} />
                {isOpen(gid("views")) &&
                  s.views.map((v) => {
                    const id = `view:${s.name}.${v.name}`;
                    const allSensitive = v.columns.every((c) => c.isSensitive);
                    return (
                      <div key={v.name}>
                        <TreeRow
                          name={v.name}
                          icon={<IconEye size={14} color="var(--mantine-color-dimmed)" />}
                          depth={2}
                          expandable
                          open={isOpen(id)}
                          onToggle={() => toggle(id)}
                          trailing={
                            <AppendSelectButton
                              disabled={allSensitive}
                              onClick={() => onTableClick(s.name, v.name, v.columns)}
                            />
                          }
                        />
                        {isOpen(id) && <ColumnRows columns={v.columns} depth={3} />}
                      </div>
                    );
                  })}

                <GroupHeader title="Materialized Views" count={s.materializedViews.length} depth={1} open={isOpen(gid("matviews"))} onToggle={() => toggle(gid("matviews"))} />
                {isOpen(gid("matviews")) &&
                  s.materializedViews.map((m) => {
                    const id = `matview:${s.name}.${m.name}`;
                    return (
                      <div key={m.name}>
                        <TreeRow
                          name={m.name}
                          icon={<IconStack2 size={14} color="var(--mantine-color-dimmed)" />}
                          depth={2}
                          expandable
                          open={isOpen(id)}
                          onToggle={() => toggle(id)}
                        />
                        {isOpen(id) && (
                          <>
                            <ColumnRows columns={m.columns} depth={3} />
                            <IndexRows indexes={m.indexes} depth={3} />
                          </>
                        )}
                      </div>
                    );
                  })}

                <GroupHeader title="Functions" count={s.routines.length} depth={1} open={isOpen(gid("functions"))} onToggle={() => toggle(gid("functions"))} />
                {isOpen(gid("functions")) &&
                  s.routines.map((r) => (
                    <TreeRow
                      key={`${r.name}(${r.signature})`}
                      name={r.name}
                      detail={`(${r.signature})${r.returnType ? ` → ${r.returnType}` : ""}`}
                      icon={<IconMathFunction size={14} color="var(--mantine-color-dimmed)" />}
                      depth={2}
                    />
                  ))}

                <GroupHeader title="Sequences" count={s.sequences.length} depth={1} open={isOpen(gid("sequences"))} onToggle={() => toggle(gid("sequences"))} />
                {isOpen(gid("sequences")) &&
                  s.sequences.map((seq) => (
                    <TreeRow
                      key={seq.name}
                      name={seq.name}
                      detail={seq.dataType}
                      icon={<IconListNumbers size={14} color="var(--mantine-color-dimmed)" />}
                      depth={2}
                    />
                  ))}

                <GroupHeader title="Types" count={s.types.length} depth={1} open={isOpen(gid("types"))} onToggle={() => toggle(gid("types"))} />
                {isOpen(gid("types")) &&
                  s.types.map((ty) => (
                    <TreeRow
                      key={ty.name}
                      name={ty.name}
                      detail={ty.enumLabels ? `${ty.kind} · ${ty.enumLabels.join(", ")}` : ty.kind}
                      icon={<IconBraces size={14} color="var(--mantine-color-dimmed)" />}
                      depth={2}
                    />
                  ))}
              </>
            )}
          </div>
        );
      })}

      <GroupHeader title="Extensions" count={tree.extensions.length} depth={0} open={isOpen("extensions")} onToggle={() => toggle("extensions")} />
      {isOpen("extensions") &&
        tree.extensions.map((e) => (
          <TreeRow
            key={e.name}
            name={e.name}
            detail={e.version}
            icon={<IconPuzzle size={14} color="var(--mantine-color-dimmed)" />}
            depth={1}
          />
        ))}
    </Stack>
  );
}

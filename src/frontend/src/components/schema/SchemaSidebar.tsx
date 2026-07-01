import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Code,
  Flex,
  Group,
  NavLink,
  Skeleton,
  Stack,
  Text,
  Tooltip,
} from "@mantine/core";
import {
  IconBraces,
  IconChevronDown,
  IconChevronRight,
  IconDatabase,
  IconEye,
  IconKey,
  IconListNumbers,
  IconLock,
  IconMathFunction,
  IconPlaylistAdd,
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

// Pins the chevron / action controls to the right edge of the scroll viewport so they stay
// visible at the panel width while long names scroll underneath them.
const stickyRight: CSSProperties = {
  position: "sticky",
  right: 0,
  marginLeft: "auto",
  flexShrink: 0,
  alignItems: "center",
  background: "var(--mantine-color-body)",
};

function Row({
  label,
  icon,
  indent,
  onClick,
  right,
}: {
  label: string;
  icon: ReactNode;
  indent?: string | number;
  onClick?: () => void;
  right?: ReactNode;
}) {
  return (
    <Flex wrap="nowrap" align="center" w="100%">
      <OverflowTooltip label={label}>
        <NavLink
          label={label}
          leftSection={icon}
          onClick={onClick}
          pl={indent}
          active={false}
          style={{ width: "max-content", flexShrink: 0 }}
          styles={{ label: { whiteSpace: "nowrap" } }}
        />
      </OverflowTooltip>
      {right ? (
        <Group gap={2} wrap="nowrap" pl={4} style={stickyRight}>
          {right}
        </Group>
      ) : null}
    </Flex>
  );
}

// A collapsible "folder" grouping objects of one kind under a schema. Rendered only when the
// group is non-empty so schemas stay tidy.
function Group_({
  id,
  title,
  count,
  expanded,
  onToggle,
  children,
}: {
  id: string;
  title: string;
  count: number;
  expanded: Array<string>;
  onToggle: (id: string) => void;
  children: ReactNode;
}) {
  if (count === 0) return null;
  const isOpen = expanded.includes(id);
  return (
    <div>
      <Flex wrap="nowrap" align="center" w="100%">
        <NavLink
          label={`${title} (${count})`}
          leftSection={isOpen ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />}
          onClick={() => onToggle(id)}
          pl="lg"
          active={false}
          style={{ width: "max-content", flexShrink: 0 }}
          styles={{ label: { whiteSpace: "nowrap", fontWeight: 600 } }}
        />
      </Flex>
      {isOpen ? <Box>{children}</Box> : null}
    </div>
  );
}

function ColumnList({
  columns,
}: {
  columns: Array<{ name: string; dataType: string; isNullable: boolean; isSensitive: boolean; isRestricted: boolean }>;
}) {
  return (
    <Stack gap={0} pl="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-lg))">
      {columns.map((c) => (
        <Group
          key={c.name}
          gap="xs"
          px="xs"
          py={2}
          wrap="nowrap"
          style={c.isRestricted ? { opacity: 0.45 } : undefined}
        >
          <OverflowTooltip label={`${c.name} · ${c.dataType}`}>
            <Text size="xs" style={{ minWidth: 0 }}>
              {c.name}
            </Text>
          </OverflowTooltip>
          <Code fz="xs">{c.dataType}</Code>
          {c.isNullable && (
            <Text size="xs" c="dimmed">
              null
            </Text>
          )}
          {c.isRestricted ? (
            <Tooltip label="Restricted — you cannot access this column" withArrow>
              <IconLock size={10} color="var(--mantine-color-red-6)" />
            </Tooltip>
          ) : c.isSensitive ? (
            <Tooltip label="Sensitive — excluded from generated queries" withArrow>
              <IconShieldLock size={10} color="var(--mantine-color-yellow-6)" />
            </Tooltip>
          ) : null}
        </Group>
      ))}
    </Stack>
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

  function toggle(id: string) {
    setExpanded((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

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
      <Alert color="red" title="Schema load failed" mt="xs">
        Could not connect to the server.
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
        const schemaOpen = expanded.includes(schemaId);
        return (
          <div key={s.name}>
            <Flex wrap="nowrap" align="center" w="100%">
              <NavLink
                label={s.name}
                leftSection={<IconDatabase size={14} />}
                onClick={() => toggle(schemaId)}
                active={false}
                style={{ width: "max-content", flexShrink: 0 }}
                styles={{ label: { whiteSpace: "nowrap" } }}
              />
              <Box
                px={4}
                onClick={() => toggle(schemaId)}
                style={{ ...stickyRight, display: "flex", cursor: "pointer" }}
              >
                {schemaOpen ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />}
              </Box>
            </Flex>

            {schemaOpen && (
              <>
                <Group_ id={`${schemaId}:tables`} title="Tables" count={s.tables.length} expanded={expanded} onToggle={toggle}>
                  {s.tables.map((t) => {
                    const id = `table:${s.name}.${t.name}`;
                    const open = expanded.includes(id);
                    return (
                      <div key={t.name}>
                        <Row
                          label={t.name}
                          icon={<IconTable size={14} />}
                          indent="xl"
                          onClick={() => toggle(id)}
                          right={
                            <>
                              <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => toggle(id)} aria-label={open ? "Collapse" : "Expand"}>
                                {open ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />}
                              </ActionIcon>
                              <Tooltip label="Append SELECT query" position="right" withArrow>
                                <Button onClick={() => onTableClick(s.name, t.name, t.columns)} size="xs" variant="subtle" disabled={t.columns.every((c) => c.isSensitive)}>
                                  <IconPlaylistAdd />
                                </Button>
                              </Tooltip>
                            </>
                          }
                        />
                        {open && (
                          <>
                            <ColumnList columns={t.columns} />
                            {t.indexes.map((ix) => (
                              <Row key={ix.name} label={`${ix.name} (${ix.columns.join(", ")})`} icon={<IconKey size={12} />} indent="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-md))" />
                            ))}
                          </>
                        )}
                      </div>
                    );
                  })}
                </Group_>

                <Group_ id={`${schemaId}:views`} title="Views" count={s.views.length} expanded={expanded} onToggle={toggle}>
                  {s.views.map((v) => {
                    const id = `view:${s.name}.${v.name}`;
                    const open = expanded.includes(id);
                    return (
                      <div key={v.name}>
                        <Row
                          label={v.name}
                          icon={<IconEye size={14} />}
                          indent="xl"
                          onClick={() => toggle(id)}
                          right={
                            <Tooltip label="Append SELECT query" position="right" withArrow>
                              <Button onClick={() => onTableClick(s.name, v.name, v.columns)} size="xs" variant="subtle" disabled={v.columns.every((c) => c.isSensitive)}>
                                <IconPlaylistAdd />
                              </Button>
                            </Tooltip>
                          }
                        />
                        {open && <ColumnList columns={v.columns} />}
                      </div>
                    );
                  })}
                </Group_>

                <Group_ id={`${schemaId}:matviews`} title="Materialized Views" count={s.materializedViews.length} expanded={expanded} onToggle={toggle}>
                  {s.materializedViews.map((m) => {
                    const id = `matview:${s.name}.${m.name}`;
                    const open = expanded.includes(id);
                    return (
                      <div key={m.name}>
                        <Row label={m.name} icon={<IconStack2 size={14} />} indent="xl" onClick={() => toggle(id)} />
                        {open && <ColumnList columns={m.columns} />}
                      </div>
                    );
                  })}
                </Group_>

                <Group_ id={`${schemaId}:functions`} title="Functions" count={s.routines.length} expanded={expanded} onToggle={toggle}>
                  {s.routines.map((r) => (
                    <Row
                      key={`${r.name}(${r.signature})`}
                      label={`${r.name}(${r.signature})${r.returnType ? ` → ${r.returnType}` : ""}`}
                      icon={<IconMathFunction size={14} />}
                      indent="xl"
                    />
                  ))}
                </Group_>

                <Group_ id={`${schemaId}:sequences`} title="Sequences" count={s.sequences.length} expanded={expanded} onToggle={toggle}>
                  {s.sequences.map((seq) => (
                    <Row key={seq.name} label={`${seq.name} (${seq.dataType})`} icon={<IconListNumbers size={14} />} indent="xl" />
                  ))}
                </Group_>

                <Group_ id={`${schemaId}:types`} title="Types" count={s.types.length} expanded={expanded} onToggle={toggle}>
                  {s.types.map((ty) => (
                    <Row
                      key={ty.name}
                      label={`${ty.name} {${ty.kind}}${ty.enumLabels ? `: ${ty.enumLabels.join(", ")}` : ""}`}
                      icon={<IconBraces size={14} />}
                      indent="xl"
                    />
                  ))}
                </Group_>
              </>
            )}
          </div>
        );
      })}

      {tree.extensions.length > 0 && (
        // Extensions are a database-level concept and typically few; show them expanded by
        // default so the names are always visible without requiring a click. Render entries
        // as plain NavLinks (not via Row/OverflowTooltip) since extension names are short.
        <div>
          <Flex wrap="nowrap" align="center" w="100%">
            <NavLink
              label={`Extensions (${tree.extensions.length})`}
              leftSection={<IconChevronDown size={12} />}
              pl="lg"
              active={false}
              style={{ width: "max-content", flexShrink: 0 }}
              styles={{ label: { whiteSpace: "nowrap", fontWeight: 600 } }}
            />
          </Flex>
          <Box>
            {tree.extensions.map((e) => (
              <Flex key={e.name} wrap="nowrap" align="center" w="100%">
                <NavLink
                  label={e.name}
                  leftSection={<IconPuzzle size={14} />}
                  pl="xl"
                  active={false}
                  style={{ width: "max-content", flexShrink: 0 }}
                  styles={{ label: { whiteSpace: "nowrap" } }}
                />
              </Flex>
            ))}
          </Box>
        </div>
      )}
    </Stack>
  );
}

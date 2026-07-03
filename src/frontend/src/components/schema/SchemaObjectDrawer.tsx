import { Drawer, Group, Stack, Text } from "@mantine/core";
import type { ReactNode } from "react";
import { SqlEditor } from "@/components/SqlEditor";

interface Column {
  name: string;
  dataType: string;
  isNullable: boolean;
  isSensitive: boolean;
  isRestricted: boolean;
}
interface IndexInfo {
  name: string;
  columns: Array<string>;
  isUnique: boolean;
  isPrimary: boolean;
  method: string;
}
interface ViewMeta {
  name: string;
  columns: Array<Column>;
  definition?: string | null;
}
interface MatviewMeta {
  name: string;
  columns: Array<Column>;
  indexes: Array<IndexInfo>;
  definition?: string | null;
}
interface RoutineMeta {
  name: string;
  kind: string;
  returnType: string | null;
  language: string;
  signature: string;
  definition?: string | null;
}
interface SequenceMeta {
  name: string;
  dataType: string;
  start: number | string;
  increment: number | string;
  minValue: number | string;
  maxValue: number | string;
  cycle: boolean;
  ownedByColumn: string | null;
}
interface TypeMeta {
  name: string;
  kind: string;
  enumLabels: Array<string> | null;
  attributes: Array<string> | null;
  baseType: string | null;
}

// Discriminated union of every object type that opens the detail drawer. Tables/views keep
// their inline treatment; only objects with metadata that doesn't fit one detail line appear here.
export type SchemaObjectSelection =
  | { kind: "view"; schemaName: string; object: ViewMeta }
  | { kind: "matview"; schemaName: string; object: MatviewMeta }
  | { kind: "function"; schemaName: string; object: RoutineMeta }
  | { kind: "sequence"; schemaName: string; object: SequenceMeta }
  | { kind: "type"; schemaName: string; object: TypeMeta };

const KIND_LABEL: Record<SchemaObjectSelection["kind"], string> = {
  view: "View",
  matview: "Materialized view",
  function: "Function",
  sequence: "Sequence",
  type: "Type",
};

// A label/value row in the metadata card. The label column is fixed-width so values align.
function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <Group gap="md" wrap="nowrap" align="flex-start">
      <Text size="xs" c="dimmed" style={{ width: 92, flexShrink: 0 }}>
        {label}
      </Text>
      <Text size="sm" style={{ wordBreak: "break-word" }}>
        {value}
      </Text>
    </Group>
  );
}

// Read-only, syntax-highlighted SQL definition. Reuses the editor so highlighting matches the
// query editor; databaseId is null because completions are irrelevant for a static definition.
function DefinitionBlock({ sql }: { sql: string }) {
  return (
    <Stack gap={2}>
      <Text size="xs" c="dimmed">
        Definition
      </Text>
      <SqlEditor value={sql} databaseId={null} editable={false} readOnly lineNumbers={false} maxHeight="360px" />
    </Stack>
  );
}

function ColumnList({ columns }: { columns: Array<Column> }) {
  return (
    <Stack gap={2}>
      <Text size="xs" c="dimmed">
        Columns
      </Text>
      {columns.map((c) => (
        <Text key={c.name} size="sm" ff="monospace">
          {c.name}{" "}
          <Text span c="dimmed">
            {c.dataType}
          </Text>
        </Text>
      ))}
    </Stack>
  );
}

function DetailBody({ selection }: { selection: SchemaObjectSelection }) {
  if (selection.kind === "sequence") {
    const s = selection.object;
    return (
      <Stack gap="xs">
        <Field label="Type" value={s.dataType} />
        <Field label="Start" value={String(s.start)} />
        <Field label="Increment" value={String(s.increment)} />
        <Field label="Min" value={String(s.minValue)} />
        <Field label="Max" value={String(s.maxValue)} />
        <Field label="Cycle" value={s.cycle ? "yes" : "no"} />
        <Field label="Owned by" value={s.ownedByColumn ?? "—"} />
      </Stack>
    );
  }
  if (selection.kind === "type") {
    const t = selection.object;
    return (
      <Stack gap="xs">
        <Field label="Kind" value={t.kind} />
        {t.baseType ? <Field label="Base type" value={t.baseType} /> : null}
        {t.enumLabels ? <Field label="Labels" value={t.enumLabels.join(", ")} /> : null}
        {t.attributes ? (
          <Stack gap={2}>
            <Text size="xs" c="dimmed">
              Attributes
            </Text>
            {t.attributes.map((a) => (
              <Text key={a} size="sm" ff="monospace">
                {a}
              </Text>
            ))}
          </Stack>
        ) : null}
      </Stack>
    );
  }
  if (selection.kind === "function") {
    const r = selection.object;
    return (
      <Stack gap="xs">
        <Field label="Kind" value={r.kind} />
        <Field label="Language" value={r.language} />
        <Field label="Signature" value={r.signature || "—"} />
        <Field label="Returns" value={r.returnType ?? "—"} />
        {r.definition ? <DefinitionBlock sql={r.definition} /> : null}
      </Stack>
    );
  }
  // view | matview
  const o = selection.object;
  return (
    <Stack gap="sm">
      <ColumnList columns={o.columns} />
      {selection.kind === "matview" && selection.object.indexes.length > 0 ? (
        <Stack gap={2}>
          <Text size="xs" c="dimmed">
            Indexes
          </Text>
          {selection.object.indexes.map((i) => (
            <Text key={i.name} size="sm" ff="monospace">
              {i.name}{" "}
              <Text span c="dimmed">
                {i.columns.join(", ")}
              </Text>
            </Text>
          ))}
        </Stack>
      ) : null}
      {o.definition ? <DefinitionBlock sql={o.definition} /> : null}
    </Stack>
  );
}

export function SchemaObjectDrawer({
  selection,
  onClose,
}: {
  selection: SchemaObjectSelection | null;
  onClose: () => void;
}) {
  return (
    <Drawer
      opened={selection !== null}
      onClose={onClose}
      position="right"
      size="lg"
      title={
        selection ? (
          <Group gap={8} wrap="nowrap">
            <Text size="xs" c="dimmed" tt="uppercase" ff="monospace" style={{ letterSpacing: "0.06em" }}>
              {KIND_LABEL[selection.kind]}
            </Text>
            <Text fw={600}>
              {selection.schemaName}.{selection.object.name}
            </Text>
          </Group>
        ) : null
      }
    >
      {selection ? <DetailBody selection={selection} /> : null}
    </Drawer>
  );
}

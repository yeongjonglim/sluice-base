import { MultiSelect } from "@mantine/core";

interface SchemaSelectProps {
  schemas: Array<string>;
  value: Array<string>;
  onChange: (value: Array<string>) => void;
}

export function SchemaSelect({ schemas, value, onChange }: SchemaSelectProps) {
  return (
    <MultiSelect
      placeholder="Schemas"
      data={schemas}
      value={value}
      onChange={onChange}
      size="sm"
      // Clearing to empty would blank the diagram; "all schemas" is the meaningful reset,
      // which the page handles via its null-selection sentinel.
      clearable={false}
    />
  );
}

import { Select } from "@mantine/core";
import { useCatalogServer } from "@/api/hooks";

interface DatabaseSelectProps {
  value: string | null;
  onChange: (value: string | null) => void;
}

export function DatabaseSelect({ value, onChange }: DatabaseSelectProps) {
  const servers = useCatalogServer();

  const databaseOptions = (servers.data?.servers ?? []).flatMap((s) =>
    s.databases.map((d) => ({
      value: d.id,
      label: `${s.name} — ${d.displayName}`,
    })),
  );

  return (
    <Select
      placeholder="Select a database"
      data={databaseOptions}
      value={value}
      onChange={onChange}
      size="sm"
    />
  );
}

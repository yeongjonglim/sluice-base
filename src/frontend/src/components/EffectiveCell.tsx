import { Checkbox, Indicator, Tooltip } from "@mantine/core";
import { IconUsers } from "@tabler/icons-react";

export interface GroupRef {
  groupId: string;
  name: string;
}

export interface EffectiveCellProps {
  fromDirect: boolean;
  fromGroups: Array<GroupRef>;
  onToggle: (next: boolean) => void;
  ariaLabel: string;
  disabled?: boolean;
}

export function EffectiveCell({ fromDirect, fromGroups, onToggle, ariaLabel, disabled }: EffectiveCellProps) {
  const inherited = fromGroups.length > 0;

  // Inherited-only: non-interactive marker
  if (inherited && !fromDirect) {
    const via = `Inherited via ${fromGroups.map((g) => g.name).join(", ")}`;
    return (
      <Tooltip label={via} withArrow>
        <span
          style={{ display: "inline-flex", color: "var(--mantine-color-cyan-7)", cursor: "not-allowed" }}
          aria-label={`${ariaLabel} — ${via}`}
        >
          <IconUsers size={16} />
        </span>
      </Tooltip>
    );
  }

  const checkbox = (
    <Checkbox
      checked={fromDirect}
      disabled={disabled}
      onChange={(e) => onToggle(e.currentTarget.checked)}
      aria-label={ariaLabel}
    />
  );

  // Direct AND also inherited → checked checkbox with a small corner indicator
  if (fromDirect && inherited) {
    const via = `Also inherited via ${fromGroups.map((g) => g.name).join(", ")}`;
    return (
      <Tooltip label={via} withArrow>
        <Indicator color="cyan" size={8} offset={2}>{checkbox}</Indicator>
      </Tooltip>
    );
  }

  // Direct-only or none → plain checkbox
  return checkbox;
}

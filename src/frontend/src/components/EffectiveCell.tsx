import { Checkbox, Indicator, Loader, Tooltip } from "@mantine/core";
import { IconUsers } from "@tabler/icons-react";
import type { CSSProperties } from "react";

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
  /** Saving state for this one grant, so a whole row of cells does not grey out. */
  pending?: boolean;
}

// The inherited mark is a filled box rather than a bare glyph: in a grid of empty
// checkboxes a lone cyan icon reads as decoration, while a filled cell reads as
// "granted, but owned by a group" at a glance.
const INHERITED_MARK: CSSProperties = {
  display: "inline-flex",
  alignItems: "center",
  justifyContent: "center",
  width: 20,
  height: 20,
  borderRadius: 4,
  flexShrink: 0,
  background: "var(--mantine-color-cyan-light)",
  color: "var(--mantine-color-cyan-light-color)",
  boxShadow: "inset 0 0 0 1px var(--mantine-color-cyan-4)",
};

export function InheritedGrantMark({ label, context }: { label: string; context?: string }) {
  const ariaLabel = context ? `${context} — ${label}` : label;
  return (
    <Tooltip label={label} withArrow>
      <span role="img" aria-label={ariaLabel} style={INHERITED_MARK}>
        <IconUsers size={12} stroke={2.25} aria-hidden />
      </span>
    </Tooltip>
  );
}

export function EffectiveCell({
  fromDirect,
  fromGroups,
  onToggle,
  ariaLabel,
  disabled,
  pending,
}: EffectiveCellProps) {
  const inherited = fromGroups.length > 0;
  const groupNames = fromGroups.map((g) => g.name).join(", ");

  if (pending) {
    return (
      <span role="status" aria-label={`${ariaLabel} — saving`} style={{ display: "inline-flex" }}>
        <Loader size={16} />
      </span>
    );
  }

  // Inherited-only: shown as granted, but the grant belongs to the group.
  if (inherited && !fromDirect) {
    return <InheritedGrantMark label={`Inherited via ${groupNames}`} context={ariaLabel} />;
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
    const via = `Also inherited via ${groupNames}`;
    return (
      <Tooltip label={via} withArrow>
        <Indicator color="cyan" size={8} offset={2}>{checkbox}</Indicator>
      </Tooltip>
    );
  }

  // Direct-only or none → plain checkbox
  return checkbox;
}

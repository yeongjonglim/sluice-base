import {
  CheckIcon,
  Checkbox,
  CloseButton,
  Combobox,
  Divider,
  Group,
  Pill,
  PillsInput,
  Tooltip,
  useCombobox,
} from "@mantine/core";

interface SchemaSelectProps {
  schemas: Array<string>;
  value: Array<string>;
  onChange: (value: Array<string>) => void;
}

// Sentinel value for the pinned "Select all" row so it can't collide with a schema name.
const SELECT_ALL = "\0select-all";

// Beyond this many pills, the rest collapse into a single "+N" counter pill so the control
// stays compact. All schemas remain visible/toggleable in the dropdown regardless.
const MAX_VISIBLE_PILLS = 3;

// A schema multi-select built on Combobox (rather than the plain MultiSelect) so the
// dropdown can carry a "Select all" row. The check icon sits on the right of each option,
// the control's width tracks the selected pills, and an empty selection is a valid state
// (the page shows a prompt for it).
export function SchemaSelect({ schemas, value, onChange }: SchemaSelectProps) {
  const combobox = useCombobox({
    onDropdownClose: () => combobox.resetSelectedOption(),
  });

  const allSelected = schemas.length > 0 && value.length === schemas.length;
  const someSelected = value.length > 0 && !allSelected;

  const visiblePills = value.slice(0, MAX_VISIBLE_PILLS);
  const overflowPills = value.slice(MAX_VISIBLE_PILLS);

  // WebKit resolves `width: fit-content` on this Mantine PillsInput to its max-width (a
  // flexbox intrinsic-sizing quirk that inflates max-content), so cap max-width near the
  // real content width. Chrome hugs precisely via fit-content; Safari lands at this estimate.
  // Per pill: ~6.5px per character plus ~42px for its padding, remove button, and the gap.
  const estimatedWidth =
    value.length === 0
      ? 160
      : Math.min(
          560,
          44 +
            visiblePills.reduce((sum, name) => sum + Math.ceil(name.length * 6.5) + 42, 0) +
            (overflowPills.length > 0 ? 46 : 0),
        );

  function handleSelect(val: string) {
    if (val === SELECT_ALL) {
      onChange(allSelected ? [] : [...schemas]);
      return;
    }
    onChange(value.includes(val) ? value.filter((v) => v !== val) : [...value, val]);
  }

  function handleRemove(val: string) {
    onChange(value.filter((v) => v !== val));
  }

  const pills = visiblePills.map((item) => (
    <Pill key={item} withRemoveButton onRemove={() => handleRemove(item)}>
      {item}
    </Pill>
  ));
  if (overflowPills.length > 0) {
    // Collapse the remainder into one counter pill; the tooltip lists the hidden schemas.
    pills.push(
      <Tooltip key="\0overflow" label={overflowPills.join(", ")} withArrow>
        <Pill>+{overflowPills.length}</Pill>
      </Tooltip>,
    );
  }

  const options = schemas.map((item) => (
    <Combobox.Option value={item} key={item} active={value.includes(item)}>
      <Group justify="space-between" gap="sm" wrap="nowrap">
        <span>{item}</span>
        {value.includes(item) ? <CheckIcon size={12} /> : null}
      </Group>
    </Combobox.Option>
  ));

  return (
    <Combobox store={combobox} onOptionSubmit={handleSelect} size="sm">
      <Combobox.DropdownTarget>
        <PillsInput
          size="sm"
          pointer
          rightSectionPointerEvents="all"
          onClick={() => combobox.toggleDropdown()}
          rightSection={
            value.length > 0 ? (
              <CloseButton
                size="sm"
                variant="transparent"
                aria-label="Clear schemas"
                onMouseDown={(event) => event.preventDefault()}
                onClick={(event) => {
                  event.stopPropagation();
                  onChange([]);
                }}
              />
            ) : (
              <Combobox.Chevron size="sm" />
            )
          }
          styles={{
            root: { width: "fit-content" },
            input: {
              width: "fit-content",
              // Hug the pills. Keep a comfortable floor only when empty, so the
              // placeholder stays readable; with pills there is no floor, so the control
              // shrinks to the pills + clear button instead of leaving an empty gap.
              minWidth: value.length > 0 ? 0 : 160,
              maxWidth: estimatedWidth,
            },
          }}
        >
          <Pill.Group>
            {pills}
            <Combobox.EventsTarget>
              <PillsInput.Field
                readOnly
                // Only show the placeholder when nothing is selected — redundant next to pills.
                placeholder={value.length > 0 ? undefined : "Schemas"}
                // Zero-width when pills are present so it never reserves a text input's default
                // width and can never wrap onto a second row (a 0-width item always fits the
                // line); widen it for the placeholder when empty.
                style={{
                  flex: "0 0 auto",
                  width: value.length > 0 ? 0 : 80,
                  minWidth: 0,
                  padding: 0,
                  cursor: "pointer",
                }}
                onKeyDown={(event) => {
                  if (event.key === "Backspace" && value.length > 0) {
                    event.preventDefault();
                    handleRemove(value[value.length - 1]);
                  }
                }}
              />
            </Combobox.EventsTarget>
          </Pill.Group>
        </PillsInput>
      </Combobox.DropdownTarget>

      <Combobox.Dropdown>
        <Combobox.Options>
          <Combobox.Option value={SELECT_ALL}>
            <Group gap="sm" wrap="nowrap">
              <Checkbox
                aria-hidden
                tabIndex={-1}
                size="xs"
                checked={allSelected}
                indeterminate={someSelected}
                readOnly
                style={{ pointerEvents: "none" }}
              />
              <span>Select all</span>
            </Group>
          </Combobox.Option>
          <Divider my={4} />
          {options}
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  );
}

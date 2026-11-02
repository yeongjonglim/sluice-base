import { forwardRef, useMemo } from "react";
import { Box, useComputedColorScheme } from "@mantine/core";
import CodeMirror from "@uiw/react-codemirror";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { PostgreSQL, sql } from "@codemirror/lang-sql";
import { EditorView } from "@codemirror/view";
import { Prec } from "@codemirror/state";
import type { Extension } from "@codemirror/state";
import type { BasicSetupOptions, ReactCodeMirrorRef } from "@uiw/react-codemirror";
import type React from "react";
import { useSchemaCompletions } from "@/api/hooks";

const noFocusOutline = EditorView.theme({ "&.cm-focused": { outline: "none" } });

// The bundled GitHub themes paint their own surface (#0d1117 in dark), which reads
// as a different material inside Mantine's #242424 shell. Keep their syntax
// colours, take the surface from the app's tokens.
const surfaceTheme = Prec.highest(
  EditorView.theme({
    "&": { backgroundColor: "var(--mantine-color-body)" },
    ".cm-content": { caretColor: "var(--mantine-color-text)" },
    ".cm-gutters": {
      backgroundColor: "var(--mantine-color-body)",
      borderRight: "1px solid var(--mantine-color-default-border)",
      color: "var(--mantine-color-dimmed)",
    },
    ".cm-activeLine": { backgroundColor: "var(--mantine-color-default-hover)" },
    ".cm-activeLineGutter": { backgroundColor: "var(--mantine-color-default-hover)" },
  }),
);

interface SqlEditorProps {
  value: string;
  onChange?: (value: string) => void;
  databaseId?: string | null;
  extensions?: Array<Extension>;
  editable?: boolean;
  readOnly?: boolean;
  lineNumbers?: boolean;
  minLines?: number;
  height?: string;
  minHeight?: string;
  maxHeight?: string;
  style?: React.CSSProperties;
}

function padToMinLines(value: string, minLines: number): string {
  const lineCount = value.split("\n").length;
  if (lineCount >= minLines) return value;
  return value + "\n".repeat(minLines - lineCount);
}

export const SqlEditor = forwardRef<ReactCodeMirrorRef, SqlEditorProps>(
  function SqlEditorInner(
    {
      value,
      onChange,
      databaseId,
      extensions: extra = [],
      editable,
      readOnly,
      lineNumbers = true,
      minLines,
      height,
      minHeight,
      maxHeight,
      style,
    },
    ref,
  ) {
    const computedColorScheme = useComputedColorScheme();
    const theme = computedColorScheme === "dark" ? githubDark : githubLight;
    const completions = useSchemaCompletions(databaseId ?? null);

    const basicSetup: BasicSetupOptions = useMemo(
      () => ({ lineNumbers, foldGutter: false }),
      [lineNumbers],
    );

    const lang = useMemo(
      () => sql({ dialect: PostgreSQL, schema: completions.data }),
      [completions.data],
    );

    const extensions = useMemo(
      () => [lang, surfaceTheme, ...extra, EditorView.lineWrapping, noFocusOutline],
      [lang, extra],
    );

    const displayValue = minLines ? padToMinLines(value, minLines) : value;

    return (
      <Box
        style={{
          border: "1px solid var(--mantine-color-default-border)",
          borderRadius: "var(--mantine-radius-sm)",
          overflow: "hidden",
          display: "flex",
          flexDirection: "column",
          ...style,
        }}
      >
        <CodeMirror
          ref={ref}
          value={displayValue}
          onChange={onChange}
          extensions={extensions}
          theme={theme}
          basicSetup={basicSetup}
          editable={editable}
          readOnly={readOnly}
          height={height}
          minHeight={minHeight}
          maxHeight={maxHeight}
          // Make the CodeMirror wrapper fill the bordered box so a height="100%" editor
          // has a bounded reference and its scroller can take over instead of growing.
          style={{ flex: 1, minHeight: 0 }}
        />
      </Box>
    );
  },
);

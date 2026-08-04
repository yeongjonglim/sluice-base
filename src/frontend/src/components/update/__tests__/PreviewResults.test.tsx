import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { describe, expect, it } from "vitest";
import { PreviewResults } from "@/components/update/PreviewResults";

function renderWithMantine(ui: React.ReactNode) {
  return render(<MantineProvider>{ui}</MantineProvider>);
}

describe("PreviewResults", () => {
  it("renders a tab per result set", () => {
    renderWithMantine(
      <PreviewResults
        result={{
          resultSets: [
            { columns: ["key"], rows: [["greeting"]] },
            { columns: ["n"], rows: [["1"]] },
          ],
          affectedRows: 1,
          durationMs: 4,
          error: null,
        }}
      />,
    );
    expect(screen.getByText("Result 1")).toBeInTheDocument();
    expect(screen.getByText("Result 2")).toBeInTheDocument();
  });

  it("renders an error alert when the preview errored", () => {
    renderWithMantine(
      <PreviewResults
        result={{ resultSets: [], affectedRows: 0, durationMs: 2, error: "syntax error" }}
      />,
    );
    expect(screen.getByText("syntax error")).toBeInTheDocument();
  });

  it("shows a rows-would-change summary when there are no result sets but no error", () => {
    renderWithMantine(
      <PreviewResults
        result={{ resultSets: [], affectedRows: 5, durationMs: 3, error: null }}
      />,
    );
    expect(screen.getByText(/5 rows would change/i)).toBeInTheDocument();
    expect(screen.getByText(/rolled back/i)).toBeInTheDocument();
  });
});

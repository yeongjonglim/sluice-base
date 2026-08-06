import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import type { PlanNode } from "@/utils/queryPlan";
import { PlanNodeRow } from "@/components/query/PlanNodeRow";

const base = (over: Partial<PlanNode> = {}): PlanNode => ({
  id: "0", nodeType: "Seq Scan", object: "orders", totalCost: 890, planRows: 10000,
  actualTotalTimeMs: null, actualRows: null, actualLoops: null, selfTimeMs: null,
  isSeqScan: true, misestimateFactor: null, children: [], ...over,
});

const renderRow = (props: Partial<React.ComponentProps<typeof PlanNodeRow>> = {}) =>
  render(
    <MantineProvider>
      <PlanNodeRow
        node={base()} depth={0} metric="cost" weightShare={0.5}
        isHottest={false} hasChildren={false} collapsed={false} onToggle={vi.fn()}
        {...props}
      />
    </MantineProvider>,
  );

describe("PlanNodeRow", () => {
  it("shows node type, object, cost and est rows, and a seq-scan flag", () => {
    renderRow();
    expect(screen.getByText("Seq Scan")).toBeInTheDocument();
    expect(screen.getByText(/orders/)).toBeInTheDocument();
    expect(screen.getByText(/Full Table Scan/i)).toBeInTheDocument();
  });

  it("shows actuals and a mis-estimate flag when analyzed", () => {
    renderRow({
      node: base({
        nodeType: "Seq Scan", actualTotalTimeMs: 8.1, actualRows: 9981, actualLoops: 1,
        selfTimeMs: 8.1, misestimateFactor: 19.96,
      }),
      metric: "time",
    });
    expect(screen.getByText(/8\.1/)).toBeInTheDocument();
    expect(screen.getByText(/20×|19|est.*actual/i)).toBeInTheDocument();
  });

  it("invokes onToggle when the chevron is clicked", async () => {
    const onToggle = vi.fn();
    renderRow({ hasChildren: true, onToggle });
    await userEvent.click(screen.getByLabelText(/collapse|expand/i));
    expect(onToggle).toHaveBeenCalled();
  });
});

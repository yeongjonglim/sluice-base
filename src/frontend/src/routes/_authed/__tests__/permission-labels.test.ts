import { describe, expect, it } from "vitest";

describe("PERMISSION_LABELS", () => {
  it("should have labels for all global permissions from the API", () => {
    const PERMISSION_LABELS: Record<string, { short: string; full: string }> = {
      "group:manage": { short: "Group", full: "Manage groups" },
      "permission:manage": { short: "Permission", full: "Manage permissions" },
      "query:audit": { short: "Audit", full: "Audit read queries" },
      "query:execute": { short: "Query", full: "Run read queries" },
      "server:manage": { short: "Server", full: "Manage servers" },
      "update:approve": { short: "Approve", full: "Approve update requests" },
      "update:execute": { short: "Execute", full: "Execute approved updates" },
      "update:submit": { short: "Submit", full: "Submit update requests" },
    };

    const globalPermissions = [
      "permission:manage",
      "server:manage",
      "group:manage",
    ];

    for (const permission of globalPermissions) {
      expect(
        PERMISSION_LABELS[permission],
        `Permission "${permission}" must have a label in PERMISSION_LABELS`,
      ).toBeDefined();
      expect(
        PERMISSION_LABELS[permission].short,
        `Permission "${permission}" must have a short label`,
      ).toBeTruthy();
      expect(
        PERMISSION_LABELS[permission].full,
        `Permission "${permission}" must have a full label`,
      ).toBeTruthy();
    }
  });
});

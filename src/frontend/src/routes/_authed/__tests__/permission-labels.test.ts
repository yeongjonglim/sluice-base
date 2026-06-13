import { describe, expect, it } from "vitest";
import { PERMISSION_LABELS, permissionLabel } from "@/auth/permission.ts";

describe("PERMISSION_LABELS", () => {
  it("should have labels for all global permissions from the API", () => {
    const globalPermissions = [
      "permission:manage",
      "server:manage",
    ];

    for (const permission of globalPermissions) {
      const label = permissionLabel(permission);
      expect(
        label.short,
        `Permission "${permission}" must have a short label`,
      ).toBeTruthy();
      expect(
        label.full,
        `Permission "${permission}" must have a full label`,
      ).toBeTruthy();
      // Falling back to the raw name means no real label was defined.
      expect(
        label.short,
        `Permission "${permission}" is missing a real entry in PERMISSION_LABELS`,
      ).not.toBe(permission);
    }
  });

  it("every label has non-empty short and full text", () => {
    for (const [permission, label] of Object.entries(PERMISSION_LABELS)) {
      expect(label.short, `${permission} short`).toBeTruthy();
      expect(label.full, `${permission} full`).toBeTruthy();
    }
  });
});

import {  expect, test } from "@playwright/test";
import type {Page} from "@playwright/test";

test.describe("Permission admin", () => {
  async function extracted(page: Page, username: string, password: string) {
    await page.goto("/");
    await expect(page).toHaveURL(/login-actions\/authenticate/, { timeout: 15_000 });
    await page.getByLabel(/username/i).fill(username);
    await page.locator('[id="password"]').fill(password);
    await page.getByRole("button", { name: /sign in/i }).click();
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, { timeout: 15_000 });
  }

  test("alice grants query:execute to bob", async ({ page }) => {
    // 1. Bob logs in to ensure a user row is created
    await extracted(page, "bob", "dev");

    // Sign bob out
    await page.getByTestId("user-menu").click();
    await page.getByRole("menuitem", { name: /log out/i }).click();

    // 2. Alice logs in
    await extracted(page, "alice", "dev");

    // 3. Alice sees the Permission nav link
    await expect(page.getByRole("link", { name: "Permission" })).toBeVisible();

    // 4. Navigate to /permission
    await page.getByRole("link", { name: "Permission" }).click();
    await expect(page).toHaveURL("/permission");
    await expect(page.getByRole("heading", { level: 2 })).toContainText("Permission management");

    // 5. Find bob's row and toggle query:execute on
    const bobRow = page.getByRole("row").filter({ hasText: "bob@example.com" });
    await expect(bobRow).toBeVisible();

    // Find the query:execute switch (aria-label matches the full label from PERMISSION_LABELS)
    const querySwitch = bobRow.getByRole("switch", { name: /run read queries/i });
    await expect(querySwitch).not.toBeChecked();
    await querySwitch.click({ force: true });

    // 6. Expect success toast and switch to be checked after refetch
    await expect(page.getByText("Permission granted")).toBeVisible({ timeout: 5_000 });
    await expect(querySwitch).toBeChecked({ timeout: 5_000 });

    // 7. Alice logs out
    await page.getByTestId("user-menu").click();
    await page.getByRole("menuitem", { name: /log out/i }).click();

    // Ensure session info are all cleared
    await page.evaluate(() => window.localStorage.clear());
    await page.evaluate(() => window.sessionStorage.clear());

    // 8. Bob logs back in
    await extracted(page, "bob", "dev");

    // 9. Bob's /api/me includes query:execute
    const [meResponse] = await Promise.all([
      page.waitForResponse((r) => r.url().includes("/api/me") && r.status() === 200),
    ]);
    const meBody = (await meResponse.json()) as { permissions: Array<string> };
    expect(meBody.permissions).toContain("query:execute");

    // 10. Bob does NOT see the Permission link
    await expect(page.getByRole("link", { name: "Permission" })).not.toBeVisible();

    // 11. Bob navigating directly to /permission is redirected to /
    await page.goto("/permission");
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, { timeout: 5_000 });

    // 12. Bob logout again, then we login Alice again to toggle it back off
    await page.getByTestId("user-menu").click();
    await page.getByRole("menuitem", { name: /log out/i }).click();

    // Find the query:execute switch (aria-label matches the full label from PERMISSION_LABELS)
    await extracted(page, "alice", "dev");
    await page.getByRole("link", { name: "Permission" }).click();
    await expect(page).toHaveURL("/permission");
    const querySwitch2 = bobRow.getByRole("switch", { name: /run read queries/i });
    await expect(querySwitch2).toBeChecked();
    await querySwitch2.click({ force: true });

    await expect(page.getByText("Permission revoked")).toBeVisible({ timeout: 5_000 });
    await expect(querySwitch).not.toBeChecked({ timeout: 5_000 });
  })
});

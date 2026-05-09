import { expect, test } from "@playwright/test";

test.describe("Query schema browser — alice", () => {
  test("can browse schema of a registered server", async ({ page }) => {
    // Sign in as alice
    await page.goto("http://localhost:5173");
    await page.waitForURL(/realms\/sluicebase/);
    await page.fill('[name="username"]', "alice");
    await page.fill('[name="password"]', "dev");
    await page.click('[type="submit"]');
    await page.waitForURL("http://localhost:5173/");

    // Grant query:execute via the Permission page
    await page.getByRole("link", { name: "Permission" }).click();
    await expect(page).toHaveURL("/permission");

    const aliceRow = page.getByRole("row").filter({ hasText: "alice@example.com" });
    await expect(aliceRow).toBeVisible();
    const querySwitch = aliceRow.getByRole("switch", { name: /Run read queries/i });
    if (!(await querySwitch.isChecked())) {
      await querySwitch.click({ force: true });
      await page.reload({ waitUntil: "domcontentloaded" });
    }

    // Navigate to /query
    await page.goto("http://localhost:5173/query");
    await expect(page.getByPlaceholder("Select a server")).toBeVisible();

    // Select Blue server from the dropdown
    await page.getByPlaceholder("Select a server").click({ force: true });
    await page.getByRole("option", { name: "Blue" }).click();

    // Schema tree should populate — public schema visible
    await expect(page.getByText("public")).toBeVisible({ timeout: 10_000 });

    // Expand the public schema
    await page.getByText("public").click();

    // At least one table visible (Blue has users, orders, products)
    await expect(page.getByText("users")).toBeVisible();

    // Expand the users table
    await page.getByText("users").click();

    // At least one column with a data type visible
    await expect(page.getByText("id", { exact: true })).toBeVisible();
    await expect(page.getByText("integer")).toBeVisible();
  });

  test("bob is redirected to / when navigating to /query", async ({ page, context }) => {
    await context.clearCookies();
    await page.goto("http://localhost:5173");
    await page.waitForURL(/realms\/sluicebase/);
    await page.fill('[name="username"]', "bob");
    await page.fill('[name="password"]', "dev");
    await page.click('[type="submit"]');
    await page.waitForURL("http://localhost:5173/");

    await page.goto("http://localhost:5173/query");
    await expect(page).toHaveURL("http://localhost:5173/");
  });
});

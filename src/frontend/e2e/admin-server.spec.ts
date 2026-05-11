import { expect, test } from "@playwright/test";

test.describe("Server management — alice", () => {
  test("can create, test, edit, and delete a server", async ({ page }) => {
    // Grant permission to self
    await page.goto("http://localhost:5173");
    await page.waitForURL(/realms\/sluicebase/);
    await page.fill('[name="username"]', "alice");
    await page.fill('[name="password"]', "dev");
    await page.click('[type="submit"]');
    await page.waitForURL("http://localhost:5173/");

    await page.getByRole("link", { name: "Permission" }).click();
    await expect(page).toHaveURL("/permission");
    await expect(page.getByRole("heading", { level: 2 })).toContainText("Permission management");

    // Find the server: switch (aria-label matches the full label from PERMISSION_LABELS)
    const aliceRow = page.getByRole("row").filter({ hasText: "alice@example.com" });
    await expect(aliceRow).toBeVisible();
    const querySwitch = aliceRow.getByRole("switch", { name: /Manage servers/i });
    if (!await querySwitch.isChecked()) {
      await querySwitch.click({ force: true });
      // Reload page to see the link
      await page.reload({ waitUntil: "domcontentloaded" });
    }

    const serverName = `e2e-srv-${Date.now()}`;

    // Navigate to /server
    await page.goto("http://localhost:5173/server");
    await expect(page.getByRole("heading", { name: "Server management" })).toBeVisible();

    // Open Add server modal
    await page.getByRole("button", { name: "Add server" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();

    // Fill in form
    await page.getByRole("textbox", { name: "Name", exact: true }).fill(serverName);
    await page.getByLabel("Host").fill("localhost");
    await page.getByLabel("Database").fill("appdb");
    await page.getByRole("textbox", { name: "Username"}).fill("reader_blue");
    await page.getByLabel("Password").first().fill("reader_blue");
    await page.getByLabel("Add server").getByRole("button", { name: "Add server" }).click();

    // Expect row in table — no password text visible
    await expect(page.getByRole("cell", { name: serverName })).toBeVisible();
    const bodyText = await page.evaluate(() => document.body.innerText);
    expect(bodyText).not.toContain("reader_blue_pass");

    // Test connection — expect Read: Connected badge
    const row = page.getByRole("row", { name: new RegExp(serverName) });
    await row.getByTitle("Test connection").click();
    await expect(page.getByText("Read: ")).toBeVisible({ timeout: 10_000 });

    // Edit — change name, leave password blank
    await row.getByTitle("Edit").click();
    await expect(page.getByRole("dialog")).toBeVisible();
    const nameInput = page.getByRole("textbox", { name: "Name", exact: true });
    await nameInput.clear();
    await nameInput.fill(serverName + "-renamed");
    await page.getByRole("button", { name: "Save changes" }).click();

    // Verify name updated and password still set
    await expect(page.getByRole("cell", { name: serverName + "-renamed" })).toBeVisible();

    // Delete
    const updatedRow = page.getByRole("row", { name: new RegExp(serverName + "-renamed") });
    await updatedRow.getByTitle("Delete").click();
    await expect(page.getByRole("cell", { name: serverName + "-renamed" })).not.toBeVisible();
  });

  test("bob is redirected to / when navigating to /server", async ({ page, context }) => {
    await context.clearCookies();
    await page.goto("http://localhost:5173");
    await page.waitForURL(/realms\/sluicebase/);
    await page.fill('[name="username"]', "bob");
    await page.fill('[name="password"]', "dev");
    await page.click('[type="submit"]');
    await page.waitForURL("http://localhost:5173/");

    await page.goto("http://localhost:5173/server");
    await expect(page).toHaveURL("http://localhost:5173/");
  });
});

// User-guide screenshots against the LOCAL demo stack (./start-local-demo.sh:
// frontend :5173 → backend :5001 on the local demo database; email off, no Xero).
//
// Usage: node shots.mjs <name|group|all> [...]     groups: admin, evan, sara
// Output: ./.out/shots/<admin|employee>/<name>.png — the same file names as
// user-guide/shots/, so a checked shot is copied across as-is. A step that
// fails leaves ./.out/shots/debug-<name>.png showing where it got stuck.
//
// Sizes match the guide's existing images: admin 1280×720 @1.25 (1600×900),
// phone 327×708 @2 (654×1416). Each recipe is a function below — add one per
// new screenshot, named after its file.
import { chromium } from "playwright";
import { existsSync, mkdirSync, readFileSync } from "node:fs";

const FRONT = "http://localhost:5173";
const PASSWORD = "password123"; // backend/Data/DbSeeder.cs
const OUT = new URL("./.out/shots/", import.meta.url).pathname;
const SITE_A = { latitude: 3.06465, longitude: 101.66668 }; // ~37 m from Site A's point

const DESKTOP = { viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1.25 };
const PHONE = { viewport: { width: 327, height: 708 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true };

const settle = (page, ms = 900) => page.waitForTimeout(ms);

async function session(browser, email, device, extra = {}) {
  const { password = PASSWORD, ...contextExtra } = extra;
  extra = contextExtra;
  const context = await browser.newContext({
    ...device,
    locale: "en-MY",
    timezoneId: "Asia/Kuala_Lumpur",
    ...extra,
  });
  const page = await context.newPage();
  // Login is rate limited (5 a minute): if a sign-in doesn't land, wait and retry.
  for (let attempt = 0; ; attempt++) {
    await page.goto(FRONT);
    await page.getByPlaceholder("your@email.com").fill(email);
    await page.getByPlaceholder("Enter your password").fill(password);
    await page.keyboard.press("Enter");
    try {
      await page.getByRole("button", { name: "Account menu" }).waitFor({ timeout: 20000 });
      break;
    } catch (err) {
      if (attempt >= 3) throw err;
      await page.waitForTimeout(20000);
    }
  }
  await settle(page, 1500);
  return { context, page };
}

async function go(page, v) {
  await page.goto(`${FRONT}/?v=${v}`);
  await page.getByRole("button", { name: "Account menu" }).waitFor();
  await settle(page, 1500);
}

async function shoot(page, group, name) {
  mkdirSync(`${OUT}${group}`, { recursive: true });
  await settle(page, 500);
  await page.screenshot({ path: `${OUT}${group}/${name}.png` });
  console.log(`✓ ${group}/${name}.png`);
}

// Clicks the first visible element with this exact text — tabs here are
// buttons, links or tabs depending on the screen.
async function clickText(page, text, opts = {}) {
  let el = page.getByText(text, { exact: true, ...opts }).filter({ visible: true }).first();
  // A tab with a count badge ("Personal 5") has no element whose text is only
  // its label, so fall back to a button/tab whose name starts with it.
  if ((await el.count()) === 0) {
    const escaped = text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    el = page.getByRole("button", { name: new RegExp(`^${escaped}\\b`) })
      .or(page.getByRole("tab", { name: new RegExp(`^${escaped}\\b`) }))
      .filter({ visible: true }).first();
  }
  await el.click();
  await settle(page);
}

// Scroll so `text` sits near the top of the viewport.
async function scrollTo(page, text, offset = 140, exact = false) {
  const el = page.getByText(text, { exact }).filter({ visible: true }).first();
  await el.evaluate((node, off) => {
    // The nearest scrollable parent (a dialog's list), else the page itself.
    let scroller = node.parentElement;
    while (scroller && scroller !== document.body) {
      const s = getComputedStyle(scroller);
      if (/(auto|scroll)/.test(s.overflowY) && scroller.scrollHeight > scroller.clientHeight + 10) break;
      scroller = scroller.parentElement;
    }
    if (scroller && scroller !== document.body) {
      scroller.scrollTop += node.getBoundingClientRect().top - scroller.getBoundingClientRect().top - off;
    } else {
      window.scrollTo(0, node.getBoundingClientRect().top + window.scrollY - off);
    }
  }, offset);
  await settle(page, 400);
}

// ─── Admin (desktop) ────────────────────────────────────────────────────
const admin = {
  // Needs `node setup.mjs work-permits` so the "Work permits" card shows first.
  "executive-overview": async (page) => {
    await go(page, "overview");
    await page.getByText("Work permits").first().waitFor({ timeout: 15000 });
    await settle(page);
    await shoot(page, "admin", "executive-overview");
  },
  // A foreign worker's Personal → Identity with the work permit fields
  // (needs `node setup.mjs work-permits`).
  "employee-work-permit": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Arjun Pillai");
    await clickText(page, "Personal");
    await page.getByText("Work permit number").first().waitFor();
    await scrollTo(page, "Identity", 100, true);
    await page.mouse.move(5, 5); // no hover ring on a field
    await shoot(page, "admin", "employee-work-permit");
  },
  "layout-menu": async (page) => {
    await go(page, "overview");
    await page.getByRole("button", { name: "Account menu" }).click();
    await shoot(page, "admin", "layout-menu");
    await page.keyboard.press("Escape");
  },
  "manage-employee": async (page) => {
    await go(page, "company/manage-employee");
    await shoot(page, "admin", "manage-employee");
  },
  // Needs `node setup.mjs transfers` (Priya has a transfer queued). The header
  // with Transfer / Duplicate and the banner with Cancel transfer.
  "employee-transfer-pending": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Priya Devi");
    await page.getByText("Cancel transfer").first().waitFor({ timeout: 15000 });
    await page.mouse.move(5, 5);
    await shoot(page, "admin", "employee-transfer-pending");
  },
  // The employee list with the "Transfer →" tag on Priya's row.
  "employee-transfer-list": async (page) => {
    await go(page, "company/manage-employee");
    await page.getByText(/Transfer → /).first().waitFor({ timeout: 15000 });
    await scrollTo(page, "Priya Devi", 220);
    await shoot(page, "admin", "employee-transfer-list");
  },
  // Header with the Transfer and Duplicate buttons (someone with nothing queued).
  "employee-transfer-buttons": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Chan Mei Ling");
    await page.getByRole("button", { name: "Duplicate", exact: true }).waitFor({ timeout: 15000 });
    await page.mouse.move(5, 5);
    await shoot(page, "admin", "employee-transfer-buttons");
  },
  // The Transfer dialog for someone with no transfer queued (Chan Mei Ling).
  "employee-transfer-dialog": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Chan Mei Ling");
    await page.getByRole("button", { name: "Transfer", exact: true }).click();
    await page.getByText("Transfer to another company").waitFor();
    await page.getByLabel("Target company").selectOption({ index: 1 }).catch(async () => {
      await page.locator("select").first().selectOption({ index: 1 });
    });
    await settle(page);
    await shoot(page, "admin", "employee-transfer-dialog");
    await page.getByRole("button", { name: "Cancel", exact: true }).filter({ visible: true }).last().click();
  },
  // Employment tab, scrolled to the Employment history list (Aisyah has a
  // leave, a restore and a second company).
  "employee-history": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Aisyah Binti Rahman");
    await clickText(page, "Employment");
    await scrollTo(page, "Employment history", 120, true);
    await shoot(page, "admin", "employee-history");
  },
  "employee-employment": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Evan Employee");
    await clickText(page, "Employment");
    await shoot(page, "admin", "employee-employment");
  },
  "employee-statutory": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Evan Employee");
    await clickText(page, "Statutory");
    await shoot(page, "admin", "employee-statutory");
  },
  "salary-change": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Evan Employee");
    await clickText(page, "Employment");
    const field = page.getByLabel("Monthly salary").or(
      page.locator("label", { hasText: "Monthly salary" }).locator("..").locator("input")).first();
    await field.scrollIntoViewIfNeeded();
    const was = await field.inputValue();
    await field.fill(String(Number(was.replace(/,/g, "")) + 300));
    await settle(page, 500);
    await page.getByRole("button", { name: "Save changes" }).click();
    await page.getByText("Why is the salary changing?").waitFor();
    await shoot(page, "admin", "salary-change");
    // Leave nothing changed: cancel the dialog, then discard the edit.
    await page.getByRole("button", { name: "Cancel" }).filter({ visible: true }).last().click();
    await settle(page, 400);
    await page.getByRole("button", { name: "Discard" }).filter({ visible: true }).first().click().catch(() => {});
  },
  "employee-import": async (page) => {
    await go(page, "company/manage-employee");
    await page.getByRole("button", { name: "Import", exact: true }).click();
    await page.getByText("Keep existing values").first().waitFor();
    await settle(page);
    await shoot(page, "admin", "employee-import");
    await page.keyboard.press("Escape");
  },
  "accounts-bank": async (page) => {
    await go(page, "settings/settings-accounts");
    await clickText(page, "Expenses");
    await page.getByText("Site Materials").first().waitFor();
    await scrollTo(page, "Bank accounts", 40);
    await shoot(page, "admin", "accounts-bank");
  },
  loans: async (page) => {
    await go(page, "payroll");
    await clickText(page, "Loans");
    // Open the row so its month-by-month schedule (with the paused months) shows.
    await clickText(page, "Evan Employee");
    await settle(page, 800);
    await shoot(page, "admin", "loans");
  },
  "payroll-downloads": async (page) => {
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await clickText(page, "September 2026");
    await page.getByRole("button", { name: "Download files" }).click();
    await page.getByText("Statutory uploads").first().waitFor();
    await settle(page);
    await scrollTo(page, "Statutory uploads", 20, true);
    await shoot(page, "admin", "payroll-downloads");
    await page.keyboard.press("Escape");
  },
  "annual-forms": async (page) => {
    await go(page, "payroll");
    await clickText(page, "Annual forms");
    // 2026 is the year the demo runs are in; the default year has none.
    const year = page.locator("input[type=number]").first();
    await year.fill("2026");
    await year.press("Tab");
    await settle(page, 2000);
    await scrollTo(page, "What the employer keeps", 160);
    await shoot(page, "admin", "annual-forms");
  },
  "leave-cancel": async (page) => {
    await go(page, "leave");
    await clickText(page, "History");
    await page.getByText("Evan Employee").filter({ visible: true }).first().waitFor();
    // The future leave `node setup.mjs leave` approved (its date is in .out/leave.json).
    const saved = new URL("./.out/leave.json", import.meta.url);
    if (!existsSync(saved)) throw new Error("run `node setup.mjs leave` first");
    const start = new Date(JSON.parse(readFileSync(saved, "utf8")).startDate + "T00:00:00");
    const d = start.getDate(), mon = start.toLocaleString("en-GB", { month: "short" });
    const dd = String(d).padStart(2, "0"), mm = String(start.getMonth() + 1).padStart(2, "0");
    // Cell texts run together ("Annual Leave07 Oct 2026"), so no \b: "not after a digit".
    const when = new RegExp(`(^|\\D)(${dd}|${d}) ${mon}|${start.getFullYear()}-${mm}-${dd}`);
    const row = page.locator("tr, li, button, [role=row]").filter({ hasText: "Evan Employee" })
      .filter({ hasText: when }).first();
    await row.click();
    await settle(page);
    await page.getByRole("button", { name: "Cancel this leave" }).click();
    await settle(page);
    await shoot(page, "admin", "leave-cancel");
    await page.keyboard.press("Escape");
  },
};

// ─── Employee & supervisor (phone) ──────────────────────────────────────
const evan = {
  "clock-in": async (page) => {
    await go(page, "dashboard");
    const picker = page.getByRole("combobox").filter({ visible: true }).first();
    await picker.click();
    await page.getByRole("option", { name: "Site A" }).click();
    await page.getByText(/On site|away/).first().waitFor({ timeout: 15000 });
    await settle(page);
    await shoot(page, "employee", "clock-in");
  },
  "overtime-list": async (page) => {
    await go(page, "attendance/att-overtime");
    await scrollTo(page, "Finishing the cable tray", 260);
    await shoot(page, "employee", "overtime-list");
  },
  "overtime-form": async (page) => {
    await go(page, "attendance/att-overtime");
    await page.getByRole("button", { name: "Submit overtime" }).first().click();
    await page.getByText("Before-work photos or files").waitFor();
    const png = Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
      "base64");
    await page.getByPlaceholder("What work requires overtime?")
      .fill("Finishing the cable tray installation before handover");
    const [chooser] = await Promise.all([
      page.waitForEvent("filechooser"),
      page.getByText("Choose photos or PDFs").click(),
    ]);
    await chooser.setFiles([
      { name: "site-before-1.jpg", mimeType: "image/png", buffer: png },
      { name: "permit-to-work.pdf", mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.4\n%%EOF") },
    ]);
    await settle(page, 2500);
    await scrollTo(page, "Reason", 40);
    await shoot(page, "employee", "overtime-form");
    await page.keyboard.press("Escape");
  },
  "payslip-breakdown": async (page) => {
    await go(page, "payslips");
    await shoot(page, "employee", "payslip-breakdown");
  },
  "leave-request-cancel": async (page) => {
    await go(page, "leave/leave-mine");
    await clickText(page, "History");
    // Tapping the leave opens its detail sheet; the button is at its foot.
    await page.getByRole("button", { name: "Request cancellation" }).first().click();
    await settle(page, 800);
    if (!(await page.getByText("Ask to cancel this leave?").isVisible())) {
      await page.getByRole("button", { name: "Request cancellation" }).filter({ visible: true }).last().click();
    }
    await page.getByText("Ask to cancel this leave?").waitFor();
    await settle(page);
    await shoot(page, "employee", "leave-request-cancel");
    // Send it, so the supervisor has a cancellation request to approve.
    await page.getByRole("button", { name: "Send request" }).click();
    await settle(page, 1500);
  },
};

const sara = {
  "account-menu": async (page) => {
    await go(page, "dashboard");
    await page.getByRole("button", { name: "Account menu" }).click();
    await shoot(page, "employee", "account-menu");
    await page.keyboard.press("Escape");
  },
  "sup-leave-cancel": async (page) => {
    await go(page, "leave/leave-approvals");
    await page.getByText(/cancel/i).first().waitFor({ timeout: 15000 });
    await settle(page);
    await scrollTo(page, "Cancellation request", 150);
    await shoot(page, "employee", "sup-leave-cancel");
  },
};

// Needs `node setup.mjs former-employee`. Nadia works at the second demo
// company now and is a former employee of Demo Co.
const nadia = {
  "former-switcher": async (page) => {
    await go(page, "dashboard");
    await page.getByRole("button", { name: "Account menu" }).click();
    await page.getByText("Former · payslips only").first().waitFor({ timeout: 10000 });
    await shoot(page, "employee", "former-switcher");
    await page.keyboard.press("Escape");
  },
  "former-company": async (page) => {
    await go(page, "dashboard");
    await page.getByRole("button", { name: "Account menu" }).click();
    await page.getByText("Former · payslips only").first().click();
    await page.getByText(/You no longer work at/).waitFor({ timeout: 15000 });
    await settle(page, 1200);
    await shoot(page, "employee", "former-company");
  },
  "former-leave-confirm": async (page) => {
    await page.getByRole("button", { name: "Leave company" }).click();
    await page.getByText(/^Leave .*\?$/).first().waitFor();
    await shoot(page, "employee", "former-leave-confirm");
    await page.getByRole("button", { name: "Cancel" }).filter({ visible: true }).last().click();
  },
};

const wanted = process.argv.slice(2);
const want = (group, name) => wanted.includes("all") || wanted.includes(group) || wanted.includes(name);

const browser = await chromium.launch();
const failures = [];
for (const [group, email, device, extra, steps] of [
  ["admin", "admin@altomate.com", DESKTOP, {}, admin],
  ["evan", "employee@altomate.com", PHONE, { geolocation: SITE_A, permissions: ["geolocation"] }, evan],
  ["sara", "supervisor@altomate.com", PHONE, {}, sara],
  ["nadia", "nadia.demo@altomate.com", PHONE, { password: "nadia.demo@altomate.com0517" }, nadia],
]) {
  const names = Object.keys(steps).filter((n) => want(group, n));
  if (names.length === 0) continue;
  const { context, page } = await session(browser, email, device, extra);
  for (const name of names) {
    try {
      await steps[name](page);
    } catch (err) {
      failures.push(`${name}: ${err.message.split("\n")[0]}`);
      await page.screenshot({ path: `${OUT}debug-${name}.png` }).catch(() => {});
    }
  }
  await context.close();
}
await browser.close();
if (failures.length) console.log("FAILED:\n  " + failures.join("\n  "));
process.exit(failures.length ? 1 : 0);

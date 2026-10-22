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

// A made-up receipt for the claim shots: drawn by the browser from HTML (a test
// input, not an edited screenshot), so no real receipt is ever used.
async function demoReceiptPng(page) {
  const art = await page.context().newPage();
  await art.setViewportSize({ width: 300, height: 214 });
  await art.setContent(`<meta name="viewport" content="width=300"><body style="margin:0;background:#8a5a44;display:flex;justify-content:center;font-family:monospace">
    <div style="margin-top:8px;width:230px;height:206px;background:#fff;padding:18px;box-sizing:border-box;font-size:12px;color:#222">
    <h2 style="text-align:center;margin:0 0 4px">DEMO CAFE</h2><div style="text-align:center">12 Jalan Contoh, Demo City</div><hr>
    <div>Receipt 0412 &nbsp; 07/09/2026</div><hr>
    <div style="display:flex;justify-content:space-between"><span>1 Flat white</span><span>12.00</span></div>
    <div style="display:flex;justify-content:space-between"><span>1 Croissant</span><span>8.50</span></div>
    <div style="display:flex;justify-content:space-between"><span>1 Iced tea</span><span>7.50</span></div><hr>
    <div style="display:flex;justify-content:space-between"><b>TOTAL RM</b><b>28.00</b></div><div style="text-align:center;margin-top:8px">THANK YOU</div></div></body>`);
  const buffer = await art.screenshot({ type: "png" });
  await art.close();
  return buffer;
}

async function attachDemoReceipt(page) {
  const buffer = await demoReceiptPng(page);
  await page.locator("#claimReceipt").setInputFiles({ name: "demo-cafe-receipt.png", mimeType: "image/png", buffer });
}

async function openClaimReceiptStep(page) {
  await go(page, "dashboard");
  await page.getByText("Submit new claim").filter({ visible: true }).first().click();
  await page.getByText("Who paid for this?").waitFor({ timeout: 15000 });
  await page.getByText("My own money", { exact: true }).click();
  await page.getByText("Expense claim", { exact: true }).click();
  await page.getByText("Attach the main receipt").waitFor({ timeout: 15000 });
  await settle(page);
}

// Stub for POST /documents/templates/ai — made-up demo letters using real merge
// field keys. A request with a current draft (the editor's Ask AI) gets a revision.
async function stubTemplateAi(page) {
  const offer = (extra) => [
    "# Offer of Employment", "",
    "Dear {{employee.name}},", "",
    "We are pleased to offer you the position of **{{employee.jobTitle}}** at {{company.name}}, starting on {{input.startDate}}.", "",
    "- Monthly salary: {{employee.monthlySalary}}",
    "- Probation period: {{employee.probationMonths}} months",
    "- Reporting to: {{input.reportingManager}}", "",
    ...extra,
    "Please sign and return a copy of this letter by {{input.replyByDate}} to confirm your acceptance.",
  ].join("\n");
  await page.route("**/documents/templates/ai", async (route) => {
    const req = route.request().postDataJSON();
    if (req.currentBody) {
      const body = req.currentBody.replace(
        "Please sign and return",
        "You are entitled to annual leave as set out in your contract of employment.\n\nPlease sign and return");
      return route.fulfill({ json: {
        reply: "I added a short paragraph on annual leave before the closing line.",
        drafts: [{ name: "Offer Letter - Site Engineer", category: "OFFER", body, unknownFields: [], warnings: [] }],
      } });
    }
    return route.fulfill({ json: {
      reply: "Here are two drafts of an offer letter for a site engineer. Draft 1 is short; draft 2 adds the terms in more detail.",
      drafts: [
        { name: "Offer Letter - Site Engineer", category: "OFFER", warnings: [], unknownFields: [],
          body: offer([]) },
        { name: "Offer Letter - Site Engineer (detailed)", category: "OFFER", warnings: [], unknownFields: [],
          body: offer(["This offer is subject to a satisfactory medical check-up and to the terms of your employment contract.", ""]) },
      ],
    } });
  });
}

async function aiEditorShot(page, name, scrolled) {
  await stubTemplateAi(page);
  await go(page, "documents/documents-templates");
  await page.getByRole("button", { name: "Write with AI" }).first().click();
  await page.getByRole("heading", { name: "Write with AI" }).waitFor();
  await page.getByPlaceholder(/An offer letter for a site engineer/).fill(
    "An offer letter for a site engineer: 3-month probation, salary, start date and who they report to.");
  await page.getByRole("button", { name: "Write two drafts" }).click();
  await page.getByText("Draft 2").first().waitFor({ timeout: 15000 });
  await page.getByRole("button", { name: "Use this draft" }).first().click();
  await page.getByText("Written with AI", { exact: true }).waitFor({ timeout: 15000 });
  await page.getByPlaceholder(/Add a paragraph about the 3-month notice period/).fill(
    "Add a line about the annual leave entitlement.");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await page.getByRole("button", { name: "Undo last change" }).waitFor({ timeout: 15000 });
  await page.evaluate(() => {
    for (const el of document.querySelectorAll("*")) {
      if (el.scrollHeight > el.clientHeight + 10 && /(auto|scroll)/.test(getComputedStyle(el).overflowY)) {
        el.scrollTop = el.tagName === "OL" ? el.scrollHeight : 0;
      }
    }
    window.scrollTo(0, 0);
  });
  if (scrolled) await scrollTo(page, "Say what to change", 120);
  await page.mouse.move(5, 5);
  await settle(page, 800);
  await shoot(page, "admin", name);
}

// Admin → Leave → History, with the leave `node setup.mjs leave-edit` filed for
// Priya Devi open in its details window (date is in .out/leave-edit.json).
async function openLeaveEditTarget(page) {
  const saved = new URL("./.out/leave-edit.json", import.meta.url);
  if (!existsSync(saved)) throw new Error("run `node setup.mjs leave-edit` first");
  const start = new Date(JSON.parse(readFileSync(saved, "utf8")).startDate + "T00:00:00");
  // Demo Admin belongs to several companies on this machine: move to the demo company first.
  const switcher = page.getByRole("button", { name: "Switch company" });
  if (!(await switcher.innerText()).includes("AltomateHR Demo Co")) {
    await switcher.click();
    await page.getByRole("button", { name: /AltomateHR Demo Co/ }).first().click();
    await settle(page, 2500);
  }
  await go(page, "leave");
  await clickText(page, "History");
  await page.getByText("Priya Devi").filter({ visible: true }).first().waitFor({ timeout: 15000 });
  const d = start.getDate(), mon = start.toLocaleString("en-GB", { month: "short" });
  const dd = String(d).padStart(2, "0"), mm = String(start.getMonth() + 1).padStart(2, "0");
  const when = new RegExp(`(^|\\D)(${dd}|${d}) ${mon}|${start.getFullYear()}-${mm}-${dd}`);
  const row = page.locator("tr, li, button, [role=row]").filter({ hasText: "Priya Devi" })
    .filter({ hasText: when }).first();
  await row.click();
  await page.getByRole("button", { name: "Edit leave" }).waitFor({ timeout: 10000 });
  await settle(page);
}

// Opens Edit leave and makes it a half day on the first date (a single date
// shows the Duration box). Leaves the dialog open.
async function fillLeaveEdit(page) {
  await page.getByRole("button", { name: "Edit leave" }).click();
  const dialog = page.locator("form").filter({ has: page.getByRole("heading", { name: "Edit leave" }) });
  await dialog.waitFor();
  const dates = dialog.locator("input[type=date]");
  await dates.nth(1).fill(await dates.nth(0).inputValue());
  await dialog.getByText("Duration", { exact: true }).waitFor();
  await dialog.getByRole("combobox").nth(1).click();
  await page.getByRole("option", { name: "Morning (half day)" }).click();
  await dialog.getByText(/Working days:\s*0\.5 days/).waitFor({ timeout: 10000 });
  await dialog.locator("textarea").fill("Family trip to Penang - half day only now.");
  await settle(page, 500);
  return dialog;
}

// ─── Admin (desktop) ────────────────────────────────────────────────────
// Moves the session to "AB Test Co A" (the local company with a November 2026
// draft run), for the split-month payroll shots.
async function toAbTestCoA(page) {
  const switcher = page.getByRole("button", { name: "Switch company" });
  if (!(await switcher.innerText()).includes("AB Test Co A")) {
    await switcher.click();
    await page.getByRole("button", { name: /AB Test Co A/ }).first().click();
    await settle(page, 2500);
  }
}

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
  // Documents → Templates with the sample letters added, so "Write with AI" shows
  // in the toolbar. Adds the four sample templates if the demo company has none.
  "documents-templates": async (page) => {
    await go(page, "documents/documents-templates");
    await page.getByRole("button", { name: "Write with AI" }).first().waitFor({ timeout: 15000 });
    if ((await page.getByRole("button", { name: "Add sample templates" }).count()) > 0) {
      await page.getByRole("button", { name: "Add sample templates" }).click();
      await page.getByText("Offer Letter").first().waitFor({ timeout: 15000 });
      await settle(page);
    }
    await page.mouse.move(5, 5);
    await shoot(page, "admin", "documents-templates");
  },
  // "Write with AI": the dialog with two drafts. The AI is stubbed with made-up
  // demo text (the demo backend has no Gemini key, and we never call the real one).
  "documents-ai-dialog": async (page) => {
    await stubTemplateAi(page);
    await go(page, "documents/documents-templates");
    await page.getByRole("button", { name: "Write with AI" }).first().click();
    await page.getByRole("heading", { name: "Write with AI" }).waitFor();
    await page.getByPlaceholder(/An offer letter for a site engineer/).fill(
      "An offer letter for a site engineer: 3-month probation, salary, start date and who they report to.");
    await page.getByRole("button", { name: "Write two drafts" }).click();
    await page.getByText("Draft 2").first().waitFor({ timeout: 15000 });
    await settle(page, 1200);
    await shoot(page, "admin", "documents-ai-dialog");
  },
  // The editor after "Use this draft": the Written with AI banner and the Ask AI
  // panel above the merge fields, after one (stubbed) change so Undo shows.
  "documents-ai-editor": (page) => aiEditorShot(page, "documents-ai-editor", false),
  // Same editor scrolled down: Ask AI with Undo last change, above the merge fields.
  "documents-ai-ask": (page) => aiEditorShot(page, "documents-ai-ask", true),
  // System Settings → Admins: each admin row with Manage access and Remove.
  "admins-list": async (page) => {
    await go(page, "settings/settings-admins");
    await page.getByRole("button", { name: "Manage access" }).first().waitFor({ timeout: 15000 });
    await settle(page);
    await shoot(page, "admin", "admins-list");
  },
  // The Manage access dialog on its Modules tab (Off / View / Manage), then Employees.
  "admins-access-modules": async (page) => {
    // Sign-in can land on another company on this machine: move to the demo company first.
    const switcher = page.getByRole("button", { name: "Switch company" });
    if (!(await switcher.innerText()).includes("AltomateHR Demo Co")) {
      await switcher.click();
      await page.getByRole("button", { name: /AltomateHR Demo Co/ }).first().click();
      await settle(page, 2500);
    }
    await go(page, "settings/settings-admins");
    await page.getByRole("button", { name: "Manage access" }).first().click();
    await page.getByText("Manage access", { exact: true }).last().waitFor();
    await settle(page);
    await shoot(page, "admin", "admins-access-modules");
  },
  "admins-access-employees": async (page) => {
    await go(page, "settings/settings-admins");
    await page.getByRole("button", { name: "Manage access" }).first().click();
    await page.getByRole("tab", { name: "Employees" }).click();
    await settle(page);
    await shoot(page, "admin", "admins-access-employees");
  },
  "employee-add": async (page) => {
    await go(page, "company/manage-employee");
    await page.getByRole("button", { name: "Add employee" }).click();
    await page.getByText(/\(auto\)/).first().waitFor({ state: "attached", timeout: 15000 }).catch(() => {});
    await settle(page, 1500);
    await shoot(page, "admin", "employee-add");
    await page.keyboard.press("Escape");
  },
  // Priya is on Other bank / e-wallet (Merchantrade): free-text provider, account, holder.
  "employee-payout": async (page) => {
    await go(page, "company/manage-employee");
    await clickText(page, "Priya Devi");
    await clickText(page, "Statutory");
    await page.getByText("Bank / payout").first().waitFor();
    await scrollTo(page, "Bank / payout", 80, true);
    await page.mouse.move(5, 5);
    await shoot(page, "admin", "employee-payout");
  },
  "layout-menu": async (page) => {
    await go(page, "overview");
    // Demo Admin belongs to several companies on this machine: show the demo company.
    const switcher = page.getByRole("button", { name: "Switch company" });
    if (!(await switcher.innerText()).includes("AltomateHR Demo Co")) {
      await switcher.click();
      await page.getByRole("button", { name: /AltomateHR Demo Co/ }).first().click();
      await settle(page, 2500);
      await go(page, "overview");
    }
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
    await scrollTo(page, "Allowance Report (Excel)", 200);
    await shoot(page, "admin", "payroll-downloads");
    await page.keyboard.press("Escape");
  },
  // A draft run (node setup.mjs payroll-draft): the "Payroll summary for approval" card.
  "payroll-summary-approval": async (page) => {
    // Demo Admin belongs to several companies on this machine; sign-in can
    // land on any of them, so move to the demo company first.
    const switcher = page.getByRole("button", { name: "Switch company" });
    if (!(await switcher.innerText()).includes("AltomateHR Demo Co")) {
      await switcher.click();
      await page.getByRole("button", { name: /AltomateHR Demo Co/ }).first().click();
      await settle(page, 2500);
    }
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await clickText(page, "January 2027");
    await page.getByText("Payroll summary for approval").first().waitFor({ timeout: 20000 });
    await settle(page);
    await scrollTo(page, "Payroll summary for approval", 120);
    await shoot(page, "admin", "payroll-summary-approval");
  },
  // A draft run (node setup.mjs payroll-draft) after `node setup.mjs payroll-gap`:
  // Needs attention lists the missing field as a link to the person's record.
  "payroll-needs-attention": async (page) => {
    const switcher = page.getByRole("button", { name: "Switch company" });
    if (!(await switcher.innerText()).includes("AltomateHR Demo Co")) {
      await switcher.click();
      await page.getByRole("button", { name: /AltomateHR Demo Co/ }).first().click();
      await settle(page, 2500);
    }
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await clickText(page, "January 2027");
    await page.getByText("Payroll summary for approval").first().waitFor({ timeout: 20000 });
    await clickText(page, "Needs attention");
    await page.getByText(/the statutory files need/i).first().waitFor({ timeout: 20000 });
    await settle(page);
    await shoot(page, "admin", "payroll-needs-attention");
  },
  // A run's Payslips list narrowed by the search box; the Total row stays the whole run's.
  "payroll-payslips-search": async (page) => {
    // Sign-in can land on another company on this machine: move to the demo company first.
    const switcher = page.getByRole("button", { name: "Switch company" });
    if (!(await switcher.innerText()).includes("AltomateHR Demo Co")) {
      await switcher.click();
      await page.getByRole("button", { name: /AltomateHR Demo Co/ }).first().click();
      await settle(page, 2500);
    }
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await clickText(page, "September 2026");
    const box = page.getByPlaceholder("Search name, employee ID, or job title").filter({ visible: true }).first();
    await box.waitFor({ timeout: 20000 });
    await box.fill("evan");
    await page.getByText(/shown\. Totals below are still the whole run/).first().waitFor({ timeout: 10000 });
    await box.evaluate((el) => el.scrollIntoView({ block: "start" }));
    await settle(page, 500);
    await page.evaluate(() => window.scrollBy(0, -160));
    await shoot(page, "admin", "payroll-payslips-search");
  },
  // AB Test Co A's November 2026 draft (not stale, so the computed HRDF levy
  // shows greyed): one monthly employee's adjustments drawer, scrolled to the
  // Unpaid leave / Employer HRDF levy / Overtime boxes. Nothing is saved.
  "payroll-adjustments": async (page) => {
    await toAbTestCoA(page);
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await clickText(page, "November 2026");
    await page.getByText("Payroll summary for approval").first().waitFor({ timeout: 20000 });
    const adjust = page.getByRole("button", { name: /^(Adjust|Edit) Alya Test/ }).first();
    await adjust.scrollIntoViewIfNeeded();
    await adjust.click();
    await page.getByText("Employer HRDF levy", { exact: true }).first().waitFor({ timeout: 20000 });
    await settle(page, 800);
    await scrollTo(page, "Unpaid leave", 60, true);
    await shoot(page, "admin", "payroll-adjustments");
    await page.getByRole("button", { name: "Cancel" }).click();
  },
  // AB Test Co A has a November 2026 draft (unnamed): starting another run for that
  // month needs a name, and the people already in it are greyed out.
  "payroll-run-picker": async (page) => {
    await toAbTestCoA(page);
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await page.getByRole("button", { name: "Create draft" }).first().click();
    await page.getByText("Policies and employees").first().waitFor({ timeout: 20000 });
    await page.locator("#runMonth").click();
    await page.getByRole("option", { name: "November" }).click();
    await page.locator("#runYear").fill("2026");
    await page.getByText("11 in another run").first().waitFor({ timeout: 20000 });
    // Expand the policy so the greyed people show, and scroll the dialog so the
    // Run name field and the people around the two free ones are both in view.
    await page.getByRole("button", { name: "Expand" }).first().click();
    await page.getByText("Already in November 2026").first().waitFor({ timeout: 10000 });
    await scrollTo(page, "Fiona Cp38", 40, true);
    await scrollTo(page, "Run name", 20, true);
    await settle(page, 600);
    await shoot(page, "admin", "payroll-run-picker");
    await page.keyboard.press("Escape");
  },
  // Needs: node setup.mjs split-run. The runs list with the "all runs" row.
  "payroll-runs-split": async (page) => {
    await toAbTestCoA(page);
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await page.getByText("November 2026 — all runs").first().waitFor({ timeout: 20000 });
    await settle(page, 800);
    // Zoom the page out a little so the Summary and Excel buttons at the end of
    // the row fit beside the figures (the table is wider than the card at 100%).
    await page.evaluate(() => { document.body.style.zoom = "0.88"; });
    await settle(page, 800);
    await scrollTo(page, "November 2026 — all runs", 140);
    await shoot(page, "admin", "payroll-runs-split");
  },
  // Needs: node setup.mjs split-run. A named run's header: its name and the pencil.
  "payroll-run-name": async (page) => {
    await toAbTestCoA(page);
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await page.getByText("November 2026 — all runs").first().waitFor({ timeout: 20000 });
    await page.getByRole("cell", { name: "Workers" }).first().click();
    await page.getByRole("button", { name: "Rename this run" }).waitFor({ timeout: 20000 });
    await settle(page, 800);
    await shoot(page, "admin", "payroll-run-name");
  },
  "annual-forms": async (page) => {
    await go(page, "payroll");
    await clickText(page, "Annual forms");
    // 2026 is the year the demo runs are in; the default year has none.
    const year = page.locator("input[type=number]").first();
    await year.fill("2026");
    await year.press("Tab");
    await settle(page, 2000);
    await scrollTo(page, "Payroll at this company started in", 150);
    await shoot(page, "admin", "annual-forms");
  },
  "claims-settings": async (page) => {
    await go(page, "claims");
    await clickText(page, "Settings");
    await page.getByText("How approved claims are paid").first().waitFor({ timeout: 15000 });
    await scrollTo(page, "How approved claims are paid", 120);
    await shoot(page, "admin", "claims-settings");
  },
  "attendance-today": async (page) => {
    await go(page, "attendance");
    await page.getByText(/No clock-in/).first().waitFor({ timeout: 15000 });
    await settle(page, 1500);
    await shoot(page, "admin", "attendance-today");
  },
  "leave-balances": async (page) => {
    await go(page, "leave");
    await clickText(page, "Balances");
    await page.getByText(/Showing \d+ of \d+/).first().waitFor({ timeout: 15000 });
    await settle(page, 1500);
    await shoot(page, "admin", "leave-balances");
  },
  "leave-types": async (page) => {
    // Demo Admin belongs to several companies on this machine; sign-in can
    // land on any of them, so move to the demo company first.
    const switcher = page.getByRole("button", { name: "Switch company" });
    if (!(await switcher.innerText()).includes("AltomateHR Demo Co")) {
      await switcher.click();
      await page.getByRole("button", { name: /AltomateHR Demo Co/ }).first().click();
      await settle(page, 2500);
    }
    await go(page, "leave");
    await clickText(page, "Types");
    await page.getByText("Kinds of leave employees can apply for").first().waitFor({ timeout: 15000 });
    await page.getByRole("columnheader", { name: "For" }).waitFor({ timeout: 15000 });
    await settle(page, 1000);
    await scrollTo(page, "Paid leave", 150, true);
    await shoot(page, "admin", "leave-types");
  },
  // The Edit leave dialog on an approved, not-yet-started leave, changed to a
  // morning off. Nothing is saved.
  "leave-edit": async (page) => {
    await openLeaveEditTarget(page);
    await fillLeaveEdit(page);
    await shoot(page, "admin", "leave-edit");
    await page.keyboard.press("Escape");
  },
  // The same leave's details after saving the change: Edit leave and Cancel this
  // leave side by side, and "Changed by admin" with before and after in the trail.
  "leave-edit-trail": async (page) => {
    await openLeaveEditTarget(page);
    const dialog = await fillLeaveEdit(page);
    await dialog.getByRole("button", { name: "Save changes" }).click();
    await page.getByText("Changed by admin").first().waitFor({ timeout: 15000 });
    await settle(page, 1200);
    await scrollTo(page, "Cancel this leave", 520, true);
    await shoot(page, "admin", "leave-edit-trail");
    await page.keyboard.press("Escape");
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
  // The main receipt is required: both buttons wait for a file. Never calls the
  // receipt AI (the analyze endpoint is aborted as a safety net).
  "claim-receipt": async (page) => {
    await page.route("**/claims/receipts/analyze", (route) => route.abort());
    await openClaimReceiptStep(page);
    // The receipt is optional: no file attached, both buttons work.
    await settle(page, 1000);
    await scrollTo(page, "Skip AI and fill manually", 520);
    await shoot(page, "employee", "claim-receipt");
    await page.getByRole("button", { name: "Close" }).filter({ visible: true }).first().click().catch(() => {});
  },
  // "Skip AI and fill manually" keeps the receipt; supporting documents add up,
  // each with its own remove button.
  "claim-supporting": async (page) => {
    await page.route("**/claims/receipts/analyze", (route) => route.abort());
    await openClaimReceiptStep(page);
    await attachDemoReceipt(page);
    await page.getByRole("button", { name: "Skip AI and fill manually" }).click();
    await page.getByText("Supporting documents").first().waitFor({ timeout: 15000 });
    const pdf = (name) => ({ name, mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.4\n%%EOF") });
    for (const batch of [[pdf("invoice-0412.pdf")], [pdf("approval-note.pdf"), pdf("parking-ticket.pdf")]]) {
      const [chooser] = await Promise.all([
        page.waitForEvent("filechooser"),
        page.getByText(/Tap to add (more|supporting documents)/).click(),
      ]);
      await chooser.setFiles(batch);
      await settle(page, 600);
    }
    await scrollTo(page, "Supporting documents", 120, true);
    await shoot(page, "employee", "claim-supporting");
    await page.getByRole("button", { name: "Close" }).filter({ visible: true }).first().click().catch(() => {});
  },
  // The details form after "Skip AI and fill manually" with no receipt: the
  // optional "Tap to add the main receipt" box.
  "claim-add-receipt": async (page) => {
    await page.route("**/claims/receipts/analyze", (route) => route.abort());
    await openClaimReceiptStep(page);
    await page.getByRole("button", { name: "Skip AI and fill manually" }).click();
    await page.getByText("Tap to add the main receipt (optional)").waitFor({ timeout: 15000 });
    await scrollTo(page, "Main receipt", 200, true);
    await shoot(page, "employee", "claim-add-receipt");
    await page.getByRole("button", { name: "Close" }).filter({ visible: true }).first().click().catch(() => {});
  },
  // Same form once a receipt is attached: the Main receipt card with Replace.
  "claim-main-receipt": async (page) => {
    await page.route("**/claims/receipts/analyze", (route) => route.abort());
    await openClaimReceiptStep(page);
    await attachDemoReceipt(page);
    await page.getByRole("button", { name: "Skip AI and fill manually" }).click();
    await page.getByRole("button", { name: "Replace" }).waitFor({ timeout: 15000 });
    await scrollTo(page, "Main receipt", 200, true);
    await shoot(page, "employee", "claim-main-receipt");
    await page.getByRole("button", { name: "Close" }).filter({ visible: true }).first().click().catch(() => {});
  },
  // Editing a pending claim. The demo employee has none pending, so GET /claims
  // and the stored files are stubbed in the browser (nothing is changed on the server).
  "claim-edit-attached": async (page) => {
    const receipt = await demoReceiptPng(page);
    await page.route(/\/claims\/receipts\/demo-/, (route) =>
      route.fulfill({ status: 200, contentType: "image/png", body: receipt }));
    await page.route(/\/claims$/, (route) => {
      if (route.request().method() !== "GET") return route.continue();
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([{
          id: "demo-claim-1", claimNumber: "CLM-0412", title: "Client lunch", description: "Lunch with a client",
          category: "Meals", amount: 28, currency: "MYR", status: "PENDING", xeroSyncStatus: "NOT_SYNCED",
          settlement: "NONE", currentStep: 0, updatedAt: new Date().toISOString(), claimType: "EXPENSE",
          paymentType: "OWN_MONEY", employeeId: "demo", spentAt: new Date().toISOString(),
          submittedAt: new Date().toISOString(), exceedsLimit: false,
          receiptUrl: "/claims/receipts/demo-receipt.png",
          supportingDocumentUrls: ["/claims/receipts/demo-invoice-0412.pdf", "/claims/receipts/demo-approval-note.pdf"],
        }]),
      });
    });
    await go(page, "claims");
    await page.getByText("Client lunch").filter({ visible: true }).first().click();
    await page.getByRole("button", { name: "Edit claim" }).click();
    await page.getByText("Attached", { exact: true }).first().waitFor({ timeout: 15000 });
    await scrollTo(page, "Supporting documents", 120, true);
    await shoot(page, "employee", "claim-edit-attached");
    await page.getByRole("button", { name: "Close" }).filter({ visible: true }).first().click().catch(() => {});
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

// ─── Limited admin (desktop) ─────────────────────────────────────────────
const exec = {
  "limited-settings": async (page) => {
    await go(page, "settings/settings-organization");
    await page.getByText(/View only/).first().waitFor({ timeout: 15000 });
    await settle(page);
    await shoot(page, "admin", "limited-settings");
  },
  // System Settings → Policies as an admin limited to one policy: only that
  // policy, no New policy button, and the note about what only the owner can do.
  "policies-limited": async (page) => {
    await go(page, "settings/settings-policies");
    await page.getByText(/Only the owner can create/).first().waitFor({ timeout: 15000 });
    await settle(page);
    await shoot(page, "admin", "policies-limited");
  },
  "limited-payroll-runs": async (page) => {
    await go(page, "payroll");
    await clickText(page, "Payroll runs");
    await page.getByText(/view runs but not start one|view only/i).first().waitFor({ timeout: 15000 });
    await settle(page);
    await shoot(page, "admin", "limited-payroll-runs");
  },
};

const browser = await chromium.launch();
const failures = [];
for (const [group, email, device, extra, steps] of [
  ["admin", "admin@altomate.com", DESKTOP, {}, admin],
  ["evan", "employee@altomate.com", PHONE, { geolocation: SITE_A, permissions: ["geolocation"] }, evan],
  ["exec", "exec.demo@altomate.com", DESKTOP, {}, exec],
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

// Demo data a screenshot needs that the seed doesn't create. LOCAL demo stack
// only (./start-local-demo.sh): it refuses any API that isn't localhost.
//
// Usage: node setup.mjs <step> [...]      e.g. node setup.mjs work-permits
// Steps are safe to rerun where noted; the others add a row each time.
//
// Everything here is made-up demo data (the repo is public): demo sign-ins
// from backend/Data/DbSeeder.cs, invented permit numbers, no real people.

import { existsSync, readFileSync, writeFileSync, statSync } from "node:fs";

const API = "http://localhost:5001";
const PASSWORD = "password123"; // backend/Data/DbSeeder.cs
if (!/^http:\/\/(localhost|127\.0\.0\.1)[:/]/.test(API)) throw new Error("local demo stack only");

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Login is rate limited (5 a minute): back off on 429, and cache tokens for
// most of their 15-minute life so reruns don't log in again.
async function login(email) {
  for (let attempt = 0; attempt < 6; attempt++) {
    const r = await fetch(`${API}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password: PASSWORD }),
    });
    if (r.status === 429) { await sleep(15_000); continue; }
    if (!r.ok) throw new Error(`login ${email}: ${r.status}`);
    return (await r.json()).token;
  }
  throw new Error(`login ${email}: still rate limited`);
}

async function call(token, method, path, body, form) {
  const r = await fetch(`${API}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(form ? {} : { "Content-Type": "application/json" }),
    },
    body: form ?? (body === undefined ? undefined : JSON.stringify(body)),
  });
  const text = await r.text();
  let data;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  return { status: r.status, data };
}

const CACHE = new URL("./tokens.json", import.meta.url);
const fresh = existsSync(CACHE) && Date.now() - statSync(CACHE).mtimeMs < 12 * 60_000;
const tokens = fresh ? JSON.parse(readFileSync(CACHE, "utf8")) : {};
for (const email of ["admin@altomate.com", "employee@altomate.com", "supervisor@altomate.com"]) {
  tokens[email] ??= await login(email);
}
if (!fresh) writeFileSync(CACHE, JSON.stringify(tokens));

const admin = tokens["admin@altomate.com"];
const evan = tokens["employee@altomate.com"];
const sara = tokens["supervisor@altomate.com"];

// Malaysia "today" + n days, as yyyy-MM-dd.
function mytDate(n = 0) {
  const d = new Date(Date.now() + 8 * 3600_000 + n * 86400_000);
  return d.toISOString().slice(0, 10);
}

async function profileIdOf(name) {
  const rows = (await call(admin, "GET", "/payroll/employees")).data ?? [];
  const row = rows.find((r) => r.name === name);
  if (!row) throw new Error(`no payroll profile for ${name}`);
  return row.employeeProfileId;
}

const steps = {
  // Two foreign workers with permits for the Executive Overview "Work permits"
  // card and the profile's work permit fields: one expired, one due soon.
  // Rerunnable (sets the same values each time, relative to today).
  "work-permits": async () => {
    // Gender, birth date, passport and marital status are made up too, so the
    // profile screen doesn't show red "Required" outlines in the guide shot.
    for (const [userId, nationality, number, days, gender, passport] of [
      ["usr-demo-arjun", "Indian", "PLKS-DEMO-0001", 24, "MALE", "X0000001"],
      ["usr-demo-priya", "Indonesian", "PLKS-DEMO-0002", -6, "FEMALE", "X0000002"],
    ]) {
      const got = await call(admin, "GET", `/employees/${userId}/profile`);
      if (got.status !== 200) { console.log(userId, "profile:", got.status, got.data); continue; }
      const p = got.data;
      const r = await call(admin, "PUT", `/employees/${userId}/profile`, {
        ...p,
        nationality,
        gender,
        dateOfBirth: "1990-01-15",
        idType: "PASSPORT",
        idNumber: passport,
        maritalStatus: "SINGLE",
        hasPr: false,
        isResident: false,
        workPermitNumber: number,
        workPermitExpiry: mytDate(days),
      });
      console.log("work permit", userId, nationality, mytDate(days), "→", r.status, r.status === 200 ? "" : JSON.stringify(r.data));
    }
  },

  // Approve Evan's pending 7–8 Oct leave (approved, not started) — for the
  // admin and employee leave-cancellation shots.
  leave: async () => {
    const mine = await call(evan, "GET", "/leave");
    const app = mine.data.find((a) => a.startDate?.startsWith("2026-10-07") && a.status === "PENDING");
    if (!app) { console.log("no pending 7 Oct leave", mine.data.map((a) => [a.startDate, a.status])); return; }
    for (const [who, token] of [["supervisor", sara], ["admin", admin]]) {
      const r = await call(token, "POST", `/leave/${app.id}/approve`);
      console.log(`approve as ${who}:`, r.status, r.data?.status ?? r.data?.message ?? "");
      const now = (await call(evan, "GET", "/leave")).data.find((a) => a.id === app.id);
      if (now.status === "APPROVED") { console.log("approved:", app.id); break; }
    }
  },

  // A loan for Evan that started in a filed month (Aug), paused from November.
  // Adds a loan each run.
  loan: async () => {
    const created = await call(admin, "POST", "/payroll/loans", {
      employeeProfileId: await profileIdOf("Evan Employee"),
      principalAmount: 3000,
      mode: "FIXED",
      installmentCount: 10,
      startYear: 2026,
      startMonth: 8,
      notes: "Laptop advance",
    });
    console.log("create loan:", created.status, created.data?.id ?? created.data);
    if (created.status === 200) {
      const paused = await call(admin, "POST", `/payroll/loans/${created.data.id}/pause`, { fromYear: 2026, fromMonth: 11 });
      console.log("pause:", paused.status, paused.data?.status ?? paused.data);
    }
  },

  // A small demo chart of accounts (existing codes are refused, so rerunnable).
  accounts: async () => {
    for (const [code, name] of [
      ["5100", "Site Materials"], ["5200", "Subcontractor Labour"],
      ["6100", "Travel Expenses"], ["6200", "Meals & Entertainment"],
      ["6300", "Telephone & Internet"], ["6400", "Office Rent"],
      ["6900", "Depreciation – Site Equipment"],
    ]) {
      const r = await call(admin, "POST", "/accounts", { code, name, type: "EXPENSE", isSelectable: true });
      console.log("account", code, r.status, r.data?.message ?? "");
    }
  },

  // Evan's overtime with two before-work files and one after-work file.
  // Adds a request each run.
  overtime: async () => {
    const projects = (await call(admin, "GET", "/projects")).data ?? [];
    const project = projects.find((p) => /site/i.test(p.name)) ?? projects[0];
    if (!project) throw new Error("no demo project");
    const png = Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
      "base64",
    );
    const upload = async (name) => {
      const form = new FormData();
      form.append("photo", new Blob([png], { type: "image/png" }), name);
      const up = await call(evan, "POST", "/overtime/photo", undefined, form);
      return { url: up.data.photoUrl, fileName: name };
    };
    const day = mytDate(-2);
    const r = await call(evan, "POST", "/overtime", {
      projectId: project.id,
      workDate: `${day}T00:00:00`,
      startAt: `${day}T10:00:00Z`,
      endAt: `${day}T13:00:00Z`,
      reason: "Finishing the cable tray installation before handover",
      beforeAttachments: [await upload("site-before-1.png"), await upload("site-before-2.png")],
    });
    console.log("submit overtime:", r.status, r.data?.id ?? r.data);
    if (r.status === 200) {
      const after = await call(evan, "POST", `/overtime/${r.data.id}/after-photo`, {
        attachments: [await upload("site-after-1.png")],
      });
      console.log("after file:", after.status, after.data?.message ?? "");
    }
  },
};

const wanted = process.argv.slice(2);
if (wanted.length === 0) { console.log("steps:", Object.keys(steps).join(", ")); process.exit(0); }
for (const name of wanted) {
  if (!steps[name]) { console.log(`unknown step ${name} — steps: ${Object.keys(steps).join(", ")}`); process.exit(1); }
  await steps[name]();
}

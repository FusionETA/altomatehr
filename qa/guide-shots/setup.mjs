// Demo data a screenshot needs that the seed doesn't create. LOCAL demo stack
// only (./start-local-demo.sh): it refuses any API that isn't localhost.
//
// Usage: node setup.mjs <step> [...]      e.g. node setup.mjs work-permits
// Steps are safe to rerun where noted; the others add a row each time.
//
// Everything here is made-up demo data (the repo is public): demo sign-ins
// from backend/Data/DbSeeder.cs, invented permit numbers, no real people.

import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync, statSync } from "node:fs";

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

// Demo Admin belongs to several companies on this machine and sign-in can land
// on any of them: switch to the demo company before touching payroll.
async function demoAdmin() {
  const r = await call(admin, "POST", "/auth/switch-org/org-altomate");
  if (r.status !== 200 || !r.data?.token) throw new Error(`switch to AltomateHR Demo Co: ${r.status}`);
  return r.data.token;
}

// A token for "AB Test Co A", a local company the demo admin also owns.
async function abToken() {
  const orgs = (await call(admin, "GET", "/auth/orgs")).data;
  const org = (Array.isArray(orgs) ? orgs : orgs.organizations).find((o) => o.name === "AB Test Co A");
  if (!org) throw new Error("no AB Test Co A on this machine");
  const r = await call(admin, "POST", `/auth/switch-org/${org.organizationId}`);
  return { t: r.data.token };
}

// Malaysia "today" + n days, as yyyy-MM-dd.
function mytDate(n = 0) {
  const d = new Date(Date.now() + 8 * 3600_000 + n * 86400_000);
  return d.toISOString().slice(0, 10);
}

async function profileIdOf(name) {
  const rows = (await call(admin, "GET", "/payroll/employees")).data ?? [];
  const row = rows.find((r) => r.name === name);
  if (!row?.employeeProfileId) throw new Error(`no saved payroll profile for ${name} — save one on their Employment tab first`);
  return row.employeeProfileId;
}

// Sets (or, with "", clears) an employee's nickname in the demo company. The
// update replaces role/policy/shift, so they are sent back as they were.
async function setNickname(userId, nickname) {
  const t = await demoAdmin();
  const list = (await call(t, "GET", "/employees")).data ?? [];
  const e = list.find((x) => x.id === userId);
  if (!e) throw new Error(`no ${userId} in the demo company`);
  const r = await call(t, "PUT", `/employees/${userId}`, {
    role: e.role, policyId: e.policyId, shiftId: e.shiftId, modules: e.modules, nickname,
  });
  console.log("nickname", userId, JSON.stringify(nickname), "→", r.status);
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

  // For the payroll "Needs attention" shot: blank one demo employee's (Aisyah's)
  // ID number so the January 2027 draft lists "IC number" under "fields the
  // statutory files need". (The app refuses a placeholder EPF number when
  // saving, so the ID number is the gap we can make.) The original is kept in
  // .out/id-gap.json; run `payroll-gap-restore` afterwards.
  "payroll-gap": async () => {
    const adminToken = await demoAdmin();
    const list = (await call(adminToken, "GET", "/employees")).data ?? [];
    const evanRow = list.find((e) => e.email === "aisyah.rahman@altomate.com");
    if (!evanRow) throw new Error("no Aisyah in the demo company");
    const got = await call(adminToken, "GET", `/employees/${evanRow.id}/profile`);
    if (got.status !== 200) throw new Error(`profile: ${got.status}`);
    mkdirSync(new URL("./.out/", import.meta.url), { recursive: true });
    const saved = new URL("./.out/id-gap.json", import.meta.url);
    if (!existsSync(saved)) writeFileSync(saved, JSON.stringify({ userId: evanRow.id, idNumber: got.data.idNumber ?? null }));
    const r = await call(adminToken, "PUT", `/employees/${evanRow.id}/profile`, { ...got.data, idNumber: "" });
    console.log("ID number cleared on Aisyah:", r.status, r.status === 200 ? "" : JSON.stringify(r.data));
  },

  "payroll-gap-restore": async () => {
    const saved = new URL("./.out/id-gap.json", import.meta.url);
    if (!existsSync(saved)) { console.log("nothing to restore"); return; }
    const { userId, idNumber } = JSON.parse(readFileSync(saved, "utf8"));
    const adminToken = await demoAdmin();
    const got = await call(adminToken, "GET", `/employees/${userId}/profile`);
    const r = await call(adminToken, "PUT", `/employees/${userId}/profile`, { ...got.data, idNumber });
    console.log("ID number restored:", r.status);
    if (r.status === 200) rmSync(saved);
  },

  // Approve one of Evan's future leaves (approved, not started) — for the
  // admin and employee leave-cancellation shots. Prefers the seeded lv-demo-5
  // (dates are relative to the day the seed ran), else his next pending leave,
  // else files a new one. Adds a leave when it has to.
  // Writes its start date to .out/leave.json for the `leave-cancel` recipe.
  leave: async () => {
    const today = mytDate();
    const mine = (await call(evan, "GET", "/leave")).data ?? [];
    // Future, and no cancellation already asked for (the `leave-request-cancel`
    // recipe sends one, after which the admin's cancel button changes).
    const future = (a) => (a.startDate ?? "") > today && !a.cancellationStatus;
    rmSync(new URL("./.out/leave.json", import.meta.url), { force: true });
    let app =
      mine.find((a) => a.id === "lv-demo-5" && future(a) && ["PENDING", "APPROVED"].includes(a.status)) ??
      mine.filter((a) => future(a) && a.status === "PENDING").sort((a, b) => a.startDate.localeCompare(b.startDate))[0];
    // None left (each run of the cancellation recipes uses one up): Evan files
    // a fresh two-day annual leave on the first free weekdays 3+ weeks out.
    const leaveTypeId = mine.find((a) => a.id === "lv-demo-5")?.leaveTypeId ?? mine[0]?.leaveTypeId;
    for (let n = 21; !app && leaveTypeId && n < 60; n++) {
      const start = mytDate(n), end = mytDate(n + 1);
      const dow = new Date(start + "T00:00:00Z").getUTCDay();
      if (dow === 0 || dow === 5 || dow === 6) continue;   // Mon–Thu start, so both days are weekdays
      const r = await call(evan, "POST", "/leave", {
        leaveTypeId, startDate: `${start}T00:00:00`, endDate: `${end}T00:00:00`,
        duration: "FULL_DAY", reason: "Cousin's wedding in Ipoh.",
      });
      if (r.status === 200 || r.status === 201) { app = r.data; console.log("filed leave", start, "→", end); }
    }
    if (!app) { console.log("couldn't find or file a future leave for Evan", mine.map((a) => [a.id, a.startDate, a.status])); process.exit(1); }
    for (const [who, token] of [["supervisor", sara], ["admin", admin]]) {
      const now = (await call(evan, "GET", "/leave")).data.find((a) => a.id === app.id);
      if (now.status === "APPROVED") break;
      const r = await call(token, "POST", `/leave/${app.id}/approve`);
      console.log(`approve as ${who}:`, r.status, r.data?.status ?? r.data?.message ?? "");
    }
    mkdirSync(new URL("./.out/", import.meta.url), { recursive: true });
    writeFileSync(new URL("./.out/leave.json", import.meta.url), JSON.stringify({ id: app.id, startDate: app.startDate.slice(0, 10) }));
    console.log("leave for the cancellation shots:", app.id, app.startDate.slice(0, 10));
  },

  // An approved, not-yet-started two-day annual leave for Priya Devi in the demo
  // company (org-altomate), filed by the admin, for the admin "Edit leave" shots.
  // (Evan's default sign-in can land in another company where he's a former
  // employee, so the admin files it instead.) Adds a leave each run; writes its
  // id to .out/leave-edit.json; `leave-edit-remove` cancels it again.
  "leave-edit": async () => {
    const adminT = await demoAdmin();
    const types = (await call(adminT, "GET", "/leave-types")).data ?? [];
    const annual = types.find((t) => /annual/i.test(t.name) && !t.isArchived);
    if (!annual) { console.log("no Annual Leave type", types.map((t) => t.name)); process.exit(1); }
    // Mon-Tue, about four weeks out, so both days are working days.
    let start;
    for (let n = 25; n < 60 && !start; n++) {
      const d = mytDate(n);
      if (new Date(d + "T00:00:00Z").getUTCDay() === 1) start = d;
    }
    const end = new Date(new Date(start + "T00:00:00Z").getTime() + 86400_000).toISOString().slice(0, 10);
    const r = await call(adminT, "POST", "/leave/on-behalf/usr-demo-priya", {
      leaveTypeId: annual.id, startDate: `${start}T00:00:00`, endDate: `${end}T00:00:00`,
      duration: "FULL_DAY", reason: "Family trip to Penang.",
    });
    if (r.status !== 200 && r.status !== 201) { console.log("couldn't file leave:", r.status, JSON.stringify(r.data)); process.exit(1); }
    mkdirSync(new URL("./.out/", import.meta.url), { recursive: true });
    writeFileSync(new URL("./.out/leave-edit.json", import.meta.url), JSON.stringify({ id: r.data.id, startDate: start, endDate: end }));
    console.log("leave for the edit shots:", r.data.id, r.data.status, start, end);
  },

  // Cancels the leave `leave-edit` made, so the demo company is left as it was
  // (apart from one cancelled entry in Priya's history).
  "leave-edit-remove": async () => {
    const file = new URL("./.out/leave-edit.json", import.meta.url);
    if (!existsSync(file)) { console.log("no leave-edit.json"); return; }
    const { id } = JSON.parse(readFileSync(file, "utf8"));
    const r = await call(await demoAdmin(), "POST", `/leave/${id}/admin-cancel`, { reason: "Demo data cleaned up" });
    console.log("cancel demo leave:", r.status, r.data?.status ?? r.data?.message ?? "");
    rmSync(file, { force: true });
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

  // A January 2027 draft, worked out, for the "Payroll summary for approval"
  // shot (2026 is already filed). Rerunnable: reuses the draft if there is one.
  "payroll-draft": async () => {
    const admin = await demoAdmin();
    const runs = (await call(admin, "GET", "/payroll/runs")).data ?? [];
    let run = runs.find((r) => r.periodYear === 2027 && r.periodMonth === 1);
    if (!run) {
      const c = await call(admin, "POST", "/payroll/runs", { periodYear: 2027, periodMonth: 1 });
      console.log("create draft:", c.status, c.data?.id ?? JSON.stringify(c.data));
      run = c.data;
    }
    if (!run?.id) process.exit(1);
    const g = await call(admin, "POST", `/payroll/runs/${run.id}/generate`);
    console.log("generate:", g.status, g.status === 200 ? "ok" : JSON.stringify(g.data));
  },

  // Put the demo back as it was: delete the January 2027 draft.
  "payroll-draft-remove": async () => {
    const admin = await demoAdmin();
    const runs = (await call(admin, "GET", "/payroll/runs")).data ?? [];
    const run = runs.find((r) => r.periodYear === 2027 && r.periodMonth === 1 && r.status === "DRAFT");
    if (!run) { console.log("no January 2027 draft"); return; }
    const d = await call(admin, "DELETE", `/payroll/runs/${run.id}`);
    console.log("delete draft:", d.status);
  },

  // AB Test Co A (local only): a second, named run for November 2026 beside the
  // existing draft, holding whoever isn't in it yet, worked out. Not rerunnable
  // while it exists; undo with `split-run-remove`.
  "split-run": async () => {
    const { t } = await abToken();
    const picker = (await call(t, "GET", "/payroll/runs/picker?year=2026&month=11")).data;
    const policyIds = picker.policies.filter((p) => p.members.some((m) => !m.takenByRunId)).map((p) => p.id);
    const c = await call(t, "POST", "/payroll/runs", { periodYear: 2026, periodMonth: 11, name: "Workers", policyIds, excludedEmployeeProfileIds: [] });
    console.log("create Workers:", c.status, c.data?.id ?? JSON.stringify(c.data));
    if (c.status >= 300) process.exit(1);
    const g = await call(t, "POST", `/payroll/runs/${c.data.id}/generate`);
    console.log("generate:", g.status, g.status === 200 ? "ok" : JSON.stringify(g.data));
  },

  "split-run-remove": async () => {
    const { t } = await abToken();
    const runs = (await call(t, "GET", "/payroll/runs")).data ?? [];
    const run = runs.find((r) => r.periodYear === 2026 && r.periodMonth === 11 && r.name === "Workers" && r.status === "DRAFT");
    if (!run) { console.log("no Workers draft"); return; }
    console.log("delete Workers:", (await call(t, "DELETE", `/payroll/runs/${run.id}`)).status);
  },

  // Maternity (Female only) and Paternity (Male only) in the demo company, next
  // to the seeded Annual / Medical / Unpaid. Existing codes are skipped, so
  // rerunnable. (Not /leave-types/defaults: that would also add a second
  // Medical and Unpaid next to the seed's MC and UL.)
  "leave-types": async () => {
    const token = await demoAdmin();
    const have = new Set(((await call(token, "GET", "/leave-types")).data ?? []).map((t) => t.code));
    for (const [code, name, days, eligibleGender] of [
      ["MATERNITY", "Maternity Leave", 98, "FEMALE"],
      ["PATERNITY", "Paternity Leave", 7, "MALE"],
    ]) {
      if (have.has(code)) { console.log(code, "already there"); continue; }
      const r = await call(token, "POST", "/leave-types", { code, name, paid: true, defaultDays: days, eligibleGender });
      console.log("leave type", code, r.status, r.data?.message ?? "");
    }
    for (const t of (await call(token, "GET", "/leave-types")).data ?? [])
      console.log(" ", t.code, t.eligibleGender ?? "everyone");
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

  // A transfer queued for two weeks' time on Priya Devi (Demo Co → the second
  // demo company) for the pending-transfer banner and the list tag. Needs a
  // second company the demo admin also owns ("Demo Transfer Co"). Rerunnable.
  transfers: async () => {
    const opts = await call(admin, "GET", "/employees/usr-demo-priya/transfer");
    if (opts.data?.pending) { console.log("transfer already queued for Priya"); return; }
    const target = opts.data?.targets?.find((t) => !t.employeeActiveHere);
    if (!target) { console.log("no second company to transfer into", JSON.stringify(opts.data)); process.exit(1); }
    const r = await call(admin, "POST", "/employees/usr-demo-priya/transfer", {
      targetOrganizationId: target.id,
      targetPolicyId: target.policies[0].id,
      effectiveDate: `${mytDate(14)}T00:00:00`,
      copyPayrollInfo: true,
      notes: "Moving to the Penang office",
    });
    console.log("queue transfer:", r.status, r.data?.transfer?.status ?? JSON.stringify(r.data));
  },

  // A nickname on two demo employees so the employee list and the record show
  // "Full name (Nickname)". Rerunnable; `nicknames-remove` clears them again.
  "nicknames": async () => {
    for (const [id, nick] of [["usr-demo-mei", "Mei"], ["usr-demo-aisyah", "Ica"], ["usr-demo-arjun", "Arj"]]) await setNickname(id, nick);
  },
  "nicknames-remove": async () => {
    for (const id of ["usr-demo-mei", "usr-demo-aisyah", "usr-demo-arjun"]) await setNickname(id, "");
  },

  // Cancels the queued demo transfer again (after the shots).
  "transfers-cancel": async () => {
    const opts = await call(admin, "GET", "/employees/usr-demo-priya/transfer");
    if (!opts.data?.pending) return;
    const r = await call(admin, "DELETE", `/employees/usr-demo-priya/transfer/${opts.data.pending.id}`);
    console.log("cancel transfer:", r.status);
  },

  // Nadia Demo: joined Demo Co, then moved to the second demo company today, so
  // in Demo Co she is a FORMER employee (payslips only) and her current company
  // is the other one. Sign in as nadia.demo@altomate.com. Rerunnable.
  "former-employee": async () => {
    const email = "nadia.demo@altomate.com";
    const list = (await call(admin, "GET", "/employees")).data ?? [];
    let nadia = list.find((e) => e.email === email);
    if (!nadia) {
      const r = await call(admin, "POST", "/employees", {
        email, name: "Nadia Demo", employeeNumber: "D-900", jobTitle: "Admin Executive",
        joinDate: "2025-03-03T00:00:00", dateOfBirth: "1994-05-17T00:00:00", role: "Employee",
      });
      console.log("create Nadia:", r.status, r.data?.id ?? JSON.stringify(r.data));
      nadia = r.data;
    }
    if (!nadia?.id) process.exit(1);
    const opts = await call(admin, "GET", `/employees/${nadia.id}/transfer`);
    const target = opts.data?.targets?.find((t) => !t.employeeActiveHere);
    if (!target) { console.log("already moved (or no target)"); return; }
    const r = await call(admin, "POST", `/employees/${nadia.id}/transfer`, {
      targetOrganizationId: target.id,
      targetPolicyId: target.policies[0].id,
      effectiveDate: `${mytDate(0)}T00:00:00`,
      copyPayrollInfo: false,
    });
    console.log("transfer Nadia out:", r.status, r.data?.executedImmediately);
  },
};

const wanted = process.argv.slice(2);
if (wanted.length === 0) { console.log("steps:", Object.keys(steps).join(", ")); process.exit(0); }
for (const name of wanted) {
  if (!steps[name]) { console.log(`unknown step ${name} — steps: ${Object.keys(steps).join(", ")}`); process.exit(1); }
  await steps[name]();
}

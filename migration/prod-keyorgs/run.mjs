// Runs 00 -> 01 -> 02 -> 03 on ONE connection, in one transaction.
//   node run.mjs            dry run: everything executes, then ROLLBACK (the scratch
//                                   map table is dropped, so nothing persists)
//   node run.mjs --commit   same, then COMMIT — only if 01 and the scoped 03 checks are all 0
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
const V1DIR = "/Users/chenzirong/Documents/globe-engineering-claim";
const DIR = new URL(".", import.meta.url).pathname;
const require = createRequire(`${V1DIR}/package.json`);
const mysql = require("mysql2/promise");
const COMMIT = process.argv.includes("--commit");
const BACKUPS = `${process.env.HOME}/altomatehr-migration-backups`;
const env = {};
for (const line of readFileSync(`${V1DIR}/.env`, "utf8").split("\n")) {
  const m = /^\s*(?:export\s+)?([A-Za-z0-9_]+)\s*=\s*(.*)$/.exec(line);
  if (m) env[m[1].toLowerCase()] = m[2].trim().replace(/^["']|["']$/g, "");
}
const ssl = { rejectUnauthorized: true, ca: readFileSync(env.prod_ssl_ca, "utf8") };
const secrets = JSON.parse(readFileSync(
  `${process.env.HOME}/.microsoft/usersecrets/8cb008ed-1150-4dd7-960e-dcff92d0e59f/secrets.json`, "utf8")
  .replace(/^﻿/, ""))["ConnectionStrings:Default"];
const pick = (k) => (new RegExp(`${k}=([^;]*)`, "i").exec(secrets) ?? [])[1];
const db = await mysql.createConnection({
  host: pick("server") ?? pick("host"), port: Number(pick("port") ?? 3306),
  user: pick("user id") ?? pick("uid") ?? pick("user"),
  password: pick("password") ?? pick("pwd"), database: pick("database"), ssl, multipleStatements: true });

// Dry run: every script is pointed at a scratch map, dropped at the end.
const MAP = COMMIT ? "_mig_orgmap_keyorgs" : "_mig_orgmap_keyorgs_dryrun";
const read = (f) => readFileSync(`${DIR}/${f}`, "utf8").replace(/_mig_orgmap_keyorgs/g, MAP);
const sets = (r) => (Array.isArray(r[0]) ? r : [r]); // multi-statement -> array of result sets
const selects = (res) => sets(res).filter((x) => Array.isArray(x)).flat();

try {
  // 00 is DDL (implicit commit), so it runs outside the transaction. In a dry run the
  // map is TEMPORARY: it shadows the real name for this session only and vanishes after.
  if (!COMMIT) await db.query(`DROP TABLE IF EXISTS altomatehr.${MAP}`);
  await db.query(read("00-orgmap.sql"));
  const [[{ n }]] = await db.query(`SELECT COUNT(*) n FROM altomatehr.${MAP}`);
  console.log(`scope: ${n} orgs (map ${MAP})`);

  const [pre] = await db.query(read("01-preflight.sql"));
  const preBad = pre.filter((r) => Number(r.bad) !== 0);
  console.log("01 preflight:", preBad.length ? preBad : "all 0");
  if (preBad.length) throw new Error("preflight failed — nothing written");

  const before = {};
  const T = ["Organizations","LeaveTypes","Projects","Teams","EmployeePolicies","PolicyLeaveEntitlements","PayrollSettings","PayrollCompanyInfos"];
  for (const t of T) before[t] = (await db.query(`SELECT COUNT(*) n FROM altomatehr.${t}`))[0][0].n;

  if (COMMIT) {
    // Full snapshot of every table 02 writes to, taken before it does. Written OUTSIDE
    // the repo: PayrollCompanyInfos holds declarant IC numbers.
    const snap = {};
    for (const t of T) snap[t] = (await db.query(`SELECT * FROM altomatehr.${t}`))[0];
    mkdirSync(BACKUPS, { recursive: true });
    const file = `${BACKUPS}/altomatehr-pre-keyorgs-${new Date().toISOString().replace(/[:.]/g, "")}.json`;
    writeFileSync(file, JSON.stringify(snap));
    console.log("backup:", file, Object.fromEntries(T.map((t) => [t, snap[t].length])));
  }

  await db.beginTransaction();
  const [w] = await db.query(read("02-settings.sql"));
  const affected = sets(w).filter((x) => !Array.isArray(x)).map((x) => x.affectedRows);
  console.log("02 affectedRows per statement:", affected.join(", "));

  const after = {};
  for (const t of T) after[t] = (await db.query(`SELECT COUNT(*) n FROM altomatehr.${t}`))[0][0].n;
  console.table(T.map((t) => ({ table: t, before: before[t], after: after[t], added: after[t] - before[t] })));

  const [v] = await db.query(read("03-verify.sql"));
  const rows = selects(v);
  const KNOWN_DEBRIS = new Set(["orphan PayrollSettings.OrganizationId", "orphan PayrollCompanyInfos.OrganizationId"]);
  const bad = rows.filter((r) => Number(r.bad) !== 0);
  console.log("03 verify non-zero:", bad.length ? bad : "none");
  const blocking = bad.filter((r) => !(KNOWN_DEBRIS.has(r.chk) && Number(r.bad) === 1));
  if (blocking.length) throw new Error("verify failed — rolling back");

  if (COMMIT) { await db.commit(); console.log("COMMITTED"); }
  else { await db.rollback(); console.log("DRY RUN — rolled back, nothing persisted"); }
} catch (e) {
  try { await db.rollback(); } catch {}
  console.error("ABORTED:", e.message);
  process.exitCode = 1;
} finally {
  if (!COMMIT) await db.query(`DROP TABLE IF EXISTS altomatehr.${MAP}`);
  await db.end();
}

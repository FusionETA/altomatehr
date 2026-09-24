// hr_prod -> altomatehr : copy v1's "offer this bank for company-paid claims" ticks.
//
//   node copy-ticks.mjs            dry run: prints what would change, per company
//   node copy-ticks.mjs --commit   writes it, after a JSON backup of the rows it touches
//
// v1 keeps that tick in ChartOfAccount.isBankAccount; v2 keeps it in IsSelectable on a
// BANK row. The 2026-09-18 chart-of-accounts migration (../prod-coa) copied v1's
// isSelectable instead — which v1 only uses for expense accounts, so every bank landed
// unticked. Until the release that makes v2 honour the tick, that did not matter: v2
// offered every bank. With it, it would empty every company's bank list.
//
// Only BANK rows that exist in both (same id — ../prod-coa kept v1's ids) are touched.
// Banks that only exist in v2 keep whatever they have, which is v1's own behaviour for
// a bank Xero adds later: it arrives unticked and an admin ticks it.
//
// Re-runnable: it only writes rows whose value differs. Run it again straight after the
// release goes out, since a Xero account sync on the OLD code clears bank ticks.
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";

const V1DIR = "/Users/chenzirong/Documents/globe-engineering-claim";
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

const v1 = await mysql.createConnection({
  host: env.prod_host, port: Number(env.prod_port ?? 3306), user: env.prod_username,
  password: env.prod_password, database: env.prod_database, ssl });
const v2 = await mysql.createConnection({
  host: pick("server") ?? pick("host"), port: Number(pick("port") ?? 3306),
  user: pick("user id") ?? pick("uid") ?? pick("user"),
  password: pick("password") ?? pick("pwd"), database: pick("database"), ssl });

try {
  const [src] = await v1.query("SELECT id, isBankAccount FROM ChartOfAccount WHERE type = 'BANK'");
  const tick = new Map(src.map((r) => [r.id, Number(r.isBankAccount) === 1]));

  const [rows] = await v2.query(
    `SELECT c.Id, c.IsSelectable, c.IsArchived, o.Name org
     FROM ChartOfAccounts c JOIN Organizations o ON o.Id = c.OrganizationId
     WHERE c.Type = 'BANK'`);
  const changes = rows.filter((r) => tick.has(r.Id) && tick.get(r.Id) !== (Number(r.IsSelectable) === 1));

  // What each company's claim-form bank list holds afterwards (active banks only).
  const perOrg = new Map();
  for (const r of rows) {
    if (r.IsArchived) continue;
    const o = perOrg.get(r.org) ?? { company: r.org, activeBanks: 0, offeredNow: 0, offeredAfter: 0, changing: 0 };
    const after = tick.has(r.Id) ? tick.get(r.Id) : Number(r.IsSelectable) === 1;
    o.activeBanks++;
    if (Number(r.IsSelectable) === 1) o.offeredNow++;
    if (after) o.offeredAfter++;
    if (changes.includes(r)) o.changing++;
    perOrg.set(r.org, o);
  }
  console.log(`v2 bank rows: ${rows.length}, also in v1: ${rows.filter((r) => tick.has(r.Id)).length}, to change: ${changes.length}`,
    `(tick on: ${changes.filter((r) => tick.get(r.Id)).length}, tick off: ${changes.filter((r) => !tick.get(r.Id)).length})`);
  console.table([...perOrg.values()].sort((a, b) => b.activeBanks - a.activeBanks));

  if (!COMMIT || changes.length === 0) {
    console.log(COMMIT ? "Nothing to change." : "DRY RUN — nothing written.");
  } else {
    mkdirSync(BACKUPS, { recursive: true });
    const file = `${BACKUPS}/altomatehr-pre-bank-ticks-${new Date().toISOString().replace(/[:.]/g, "")}.json`;
    writeFileSync(file, JSON.stringify(changes.map((r) => ({ Id: r.Id, IsSelectable: Number(r.IsSelectable) }))));
    console.log("backup:", file);

    await v2.beginTransaction();
    let written = 0;
    for (const r of changes) {
      const [res] = await v2.query(
        "UPDATE ChartOfAccounts SET IsSelectable = ? WHERE Id = ? AND Type = 'BANK'",
        [tick.get(r.Id) ? 1 : 0, r.Id]);
      written += res.affectedRows;
    }
    if (written !== changes.length) throw new Error(`expected ${changes.length} rows, wrote ${written}`);
    await v2.commit();
    console.log(`COMMITTED — ${written} bank rows updated`);
  }
} catch (e) {
  try { await v2.rollback(); } catch {}
  console.error("ABORTED:", e.message);
  process.exitCode = 1;
} finally { await v1.end(); await v2.end(); }

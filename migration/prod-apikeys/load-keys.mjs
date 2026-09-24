// hr_prod -> altomatehr : copy every v1 API key and master key into v2.
//
//   node load-keys.mjs            dry run: inserts in a transaction, prints, ROLLBACK
//   node load-keys.mjs --commit   same, then COMMIT
//
// The raw tokens are unrecoverable, but v1 and v2 both store SHA-256 lowercase hex
// of the full token string, so copying the HASH makes every existing wp_live_ /
// wp_master_ token work against v2 unchanged — New-Altomate only has to change its
// base URL.
//
// ORDER MATTERS. A wp_live_ key acts as an Admin of its company and scopes only
// narrow endpoints that opt in. Until 1dd959c (payroll:read/write on every payroll
// endpoint) is deployed, a copied key without payroll scopes could still approve or
// delete a payroll run. Run this only after that release is live, and after
// ../prod-keyorgs, which creates the 26 companies these keys point at.
//
// Reads secrets at runtime from the v1 .env (prod_*) and the backend's user-secrets;
// nothing sensitive is written to disk or printed — only token PREFIXES.
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";

const V1DIR = "/Users/chenzirong/Documents/globe-engineering-claim";
const require = createRequire(`${V1DIR}/package.json`);
const mysql = require("mysql2/promise");
const COMMIT = process.argv.includes("--commit");

// Mirrors backend/Modules/ApiKeys/ApiScopes.cs. Anything a mapping produces that is
// not in here aborts the run rather than writing a scope v2 would ignore.
const V2_SCOPES = new Set([
  "employees:read", "employees:write", "claims:read", "claims:write",
  "leave:read", "leave:write", "attendance:read", "attendance:write",
  "overtime:read", "overtime:write", "projects:read", "projects:write",
  "teams:read", "teams:write", "accounts:read", "accounts:write",
  "policies:read", "policies:write", "organizations:read", "organizations:write",
  "notifications:write", "sso:write", "payroll:read", "payroll:write",
]);

// v1 name -> v2 name. null = v2 has no such scope: approvals:write is covered by
// claims:write / leave:write, which every key holding it also holds.
const RENAME = {
  "chart-of-accounts:read": "accounts:read",
  "chart-of-accounts:write": "accounts:write",
  "settings:read": "organizations:read",
  "settings:write": "organizations:write",
  "approvals:write": null,
};

// Only New-Altomate's keys get SSO: that is the one partner that signs its users
// into AltomateHR (/sso/ticket). v1 had no such scope because v1 SSO was not
// scope-gated.
const SSO_PARTNER = "Altomate Corporate Services Sdn Bhd";

export function mapScopes(v1Scopes, issuedBy) {
  const out = new Set();
  for (const s of v1Scopes) {
    const v = s in RENAME ? RENAME[s] : s;
    if (v === null) continue;
    if (!V2_SCOPES.has(v)) throw new Error(`no v2 scope for v1 "${s}"`);
    out.add(v);
  }
  if (issuedBy === SSO_PARTNER) out.add("sso:write");
  return [...out].sort();
}

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

const HASH = /^[0-9a-f]{64}$/;

try {
  const [masters] = await v1.query(
    "SELECT id, partnerName, tokenHash, tokenPrefix, active, createdAt, lastUsedAt FROM MasterApiKey");
  const [keys] = await v1.query(
    `SELECT a.id, a.name, a.tokenHash, a.tokenPrefix, a.organizationId, CAST(a.scopes AS CHAR) scopes,
            a.active, a.createdAt, a.lastUsedAt, o.name orgName, mk.partnerName issuedBy
     FROM ApiIntegration a JOIN Organization o ON o.id = a.organizationId
     LEFT JOIN MasterApiKey mk ON mk.id = a.issuedByMasterKeyId`);

  // ---- preflight: everything must land cleanly, or nothing is written
  const problems = [];
  const [v2orgs] = await v2.query("SELECT Id, Name FROM Organizations");
  const orgName = new Map(v2orgs.map((o) => [o.Id, o.Name]));
  for (const k of keys) {
    if (!orgName.has(k.organizationId)) problems.push(`org missing in v2: ${k.orgName}`);
    else if (orgName.get(k.organizationId) !== k.orgName)
      problems.push(`org id ${k.organizationId} is "${orgName.get(k.organizationId)}" in v2, "${k.orgName}" in v1`);
  }
  for (const r of [...keys, ...masters]) if (!HASH.test(r.tokenHash)) problems.push(`not a SHA-256 hex hash: ${r.tokenPrefix}`);
  const allHashes = [...keys, ...masters].map((r) => r.tokenHash);
  const allIds = [...keys, ...masters].map((r) => r.id);
  const [[dup]] = await v2.query(
    `SELECT (SELECT COUNT(*) FROM ApiKeys WHERE TokenHash IN (?) OR Id IN (?)) a,
            (SELECT COUNT(*) FROM MasterKeys WHERE TokenHash IN (?) OR Id IN (?)) m`,
    [allHashes, allIds, allHashes, allIds]);
  if (Number(dup.a) || Number(dup.m)) problems.push(`already in v2: ${dup.a} ApiKeys, ${dup.m} MasterKeys (re-run?)`);

  const rows = keys.map((k) => ({ ...k, v2Scopes: mapScopes(JSON.parse(k.scopes), k.issuedBy) }));
  for (const r of rows) if (r.v2Scopes.join(",").length > 500) problems.push(`scopes too long: ${r.tokenPrefix}`);

  const sets = new Map();
  for (const r of rows) {
    const k = `${r.issuedBy ?? "(manual)"} -> ${r.v2Scopes.join(",")}`;
    sets.set(k, (sets.get(k) ?? 0) + 1);
  }
  console.log(`master keys: ${masters.length}`, masters.map((m) => `${m.partnerName} (${m.active ? "active" : "inactive"})`));
  console.log(`company keys: ${rows.length}, active ${rows.filter((r) => r.active).length},`,
    `with sso:write ${rows.filter((r) => r.v2Scopes.includes("sso:write")).length},`,
    `with payroll:write ${rows.filter((r) => r.v2Scopes.includes("payroll:write")).length}`);
  console.log("scope sets (issuer -> v2 scopes : keys):");
  for (const [k, n] of sets) console.log(`  ${n.toString().padStart(3)}  ${k}`);

  if (problems.length) {
    const counts = problems.reduce((m, p) => m.set(p.split(":")[0], (m.get(p.split(":")[0]) ?? 0) + 1), new Map());
    console.log("PREFLIGHT FAILED:", Object.fromEntries(counts));
    console.log(problems.slice(0, 30).join("\n"));
    process.exitCode = 1;
  } else {
    await v2.beginTransaction();
    for (const m of masters)
      await v2.query(
        "INSERT INTO MasterKeys (Id, Name, TokenHash, TokenPrefix, Active, CreatedAt, LastUsedAt) VALUES (?,?,?,?,?,?,?)",
        [m.id, m.partnerName, m.tokenHash, m.tokenPrefix, m.active, m.createdAt, m.lastUsedAt]);
    for (const r of rows)
      await v2.query(
        `INSERT INTO ApiKeys (Id, OrganizationId, Name, TokenHash, TokenPrefix, Scopes, Active, CreatedAt, LastUsedAt)
         VALUES (?,?,?,?,?,?,?,?,?)`,
        [r.id, r.organizationId, r.name, r.tokenHash, r.tokenPrefix, r.v2Scopes.join(","), r.active, r.createdAt, r.lastUsedAt]);

    const [[after]] = await v2.query("SELECT (SELECT COUNT(*) FROM ApiKeys) a, (SELECT COUNT(*) FROM MasterKeys) m");
    console.log("v2 now (inside txn):", after);
    if (COMMIT) { await v2.commit(); console.log("COMMITTED"); }
    else { await v2.rollback(); console.log("DRY RUN — rolled back, nothing persisted"); }
  }
} catch (e) {
  try { await v2.rollback(); } catch {}
  console.error("ABORTED:", e.message);
  process.exitCode = 1;
} finally { await v1.end(); await v2.end(); }

#!/usr/bin/env node
/**
 * Verifies that SchemaTools generated the expected outputs after building
 * the example project. Run from the schema-tools-example directory.
 *
 * Usage: node verify-build.mjs
 */

import { readFileSync, existsSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";

const root = resolve(import.meta.dirname);
let failures = 0;

function assert(condition, message) {
  if (!condition) {
    console.error(`  FAIL: ${message}`);
    failures++;
  } else {
    console.log(`  OK:   ${message}`);
  }
}

// ---------------------------------------------------------------------------
// 1. Generated SQL files - single _generated directory
// ---------------------------------------------------------------------------
console.log("\n--- Generated SQL ---");

const generatedDir = join(root, "Schema", "_generated");
const analysisPath = join(root, "analysis.json");
assert(existsSync(generatedDir), "Schema/_generated/ directory exists");

const expectedFiles = [
  // Triggers (11)
  "trg_countries_cascade_soft_delete.sql",
  "trg_country_dialling_codes_reactivation_guard.sql",
  "trg_orders_cascade_soft_delete.sql",
  "trg_orders_reactivation_guard.sql",
  "trg_order_items_reactivation_guard.sql",
  "trg_organisations_restrict_soft_delete.sql",
  "trg_organisation_members_cascade_soft_delete.sql",
  "trg_organisation_members_reactivation_guard.sql",
  "trg_users_cascade_reactivation.sql",
  "trg_users_cascade_soft_delete.sql",
  "trg_user_profiles_reactivation_guard.sql",
  // Procedure (1)
  "usp_purge_soft_deleted.sql",
  // Views (9)
  "vw_comments.sql",
  "vw_countries.sql",
  "vw_country_dialling_codes.sql",
  "vw_orders.sql",
  "vw_order_items.sql",
  "vw_organisations.sql",
  "vw_organisation_members.sql",
  "vw_users.sql",
  "vw_user_profiles.sql",
];

if (existsSync(generatedDir)) {
  const actual = readdirSync(generatedDir).sort();
  for (const file of expectedFiles) {
    assert(actual.includes(file), `${file} present`);
  }
  assert(
    actual.length === expectedFiles.length,
    `Expected ${expectedFiles.length} files, found ${actual.length}`
  );
}

// ---------------------------------------------------------------------------
// 2. Column naming - generated SQL should use record_ prefixed columns
// ---------------------------------------------------------------------------
console.log("\n--- Column Naming ---");

const triggerFile = join(
  generatedDir,
  "trg_users_cascade_soft_delete.sql"
);
if (existsSync(triggerFile)) {
  const content = readFileSync(triggerFile, "utf8");
  assert(content.includes("record_active"), "Trigger references record_active");
  assert(
    content.includes("record_updated_by"),
    "Trigger references record_updated_by"
  );
}

// ---------------------------------------------------------------------------
// 3. Post-build metadata - Build/schema.json
// ---------------------------------------------------------------------------
console.log("\n--- Post-Build Metadata ---");

const metadataPath = join(root, "Build", "schema.json");
assert(existsSync(metadataPath), "Build/schema.json exists");

if (existsSync(metadataPath)) {
  const raw = readFileSync(metadataPath, "utf8").replace(/^\uFEFF/, "");
  const metadata = JSON.parse(raw);
  assert(metadata.$schema === "./schema.schema.json", "$schema value correct");
  assert(Array.isArray(metadata.tables), "tables array present");
  assert(metadata.tables.length > 0, "At least one table in metadata");
  assert(metadata.statistics != null, "statistics object present");
}

// ---------------------------------------------------------------------------
// 4. Documentation - Docs/SCHEMA.md
// ---------------------------------------------------------------------------
console.log("\n--- Documentation ---");

const docsPath = join(root, "Docs", "SCHEMA.md");
assert(existsSync(docsPath), "Docs/SCHEMA.md exists");

if (existsSync(docsPath)) {
  const content = readFileSync(docsPath, "utf8");
  assert(content.length > 500, "SCHEMA.md has substantial content");
  assert(content.includes("## "), "SCHEMA.md has Markdown headings");
}

// ---------------------------------------------------------------------------
// 5. Primary Key correctness - non-id and composite PK tables
// ---------------------------------------------------------------------------
console.log("\n--- PK Correctness (ALTER TABLE constraints) ---");

// 5a. Purge procedure must reference iso_alpha2 for countries, not id
const purgeFile = join(generatedDir, "usp_purge_soft_deleted.sql");
if (existsSync(purgeFile)) {
  const purge = readFileSync(purgeFile, "utf8");

  // countries uses iso_alpha2 PK
  assert(
    purge.includes("t.iso_alpha2 = h.iso_alpha2"),
    "Purge proc joins countries on iso_alpha2 (not id)"
  );
  assert(
    !purge.match(/\bcountries\b[\s\S]{0,200}\bt\.id\b/),
    "Purge proc does NOT reference t.id for countries"
  );

  // country_dialling_codes uses composite PK
  assert(
    purge.includes("t.country_code = h.country_code")
      && purge.includes("t.dialling_code = h.dialling_code"),
    "Purge proc joins country_dialling_codes on composite PK"
  );
}

// 5b. Cascade trigger for countries must use iso_alpha2 in PK join
const countriesTrigger = join(
  generatedDir,
  "trg_countries_cascade_soft_delete.sql"
);
if (existsSync(countriesTrigger)) {
  const content = readFileSync(countriesTrigger, "utf8");
  assert(
    content.includes("i.iso_alpha2 = d.iso_alpha2"),
    "Countries cascade trigger joins on iso_alpha2"
  );
  assert(
    !content.includes("i.id = d.id"),
    "Countries cascade trigger does NOT use id"
  );
}

// 5c. Reactivation guard for country_dialling_codes must use composite PK
const diallingGuard = join(
  generatedDir,
  "trg_country_dialling_codes_reactivation_guard.sql"
);
if (existsSync(diallingGuard)) {
  const content = readFileSync(diallingGuard, "utf8");
  assert(
    content.includes("i.country_code = d.country_code")
      && content.includes("i.dialling_code = d.dialling_code"),
    "Dialling codes guard trigger joins on composite PK"
  );
  assert(
    !content.includes("i.id = d.id"),
    "Dialling codes guard trigger does NOT use id"
  );
}

// 5d. analysis.json should have correct PKs for the new tables
if (existsSync(analysisPath)) {
  const raw = readFileSync(analysisPath, "utf8").replace(/^\uFEFF/, "");
  const analysis = JSON.parse(raw);
  const countriesEntry = analysis.tables.find((t) => t.name === "countries");
  const diallingEntry = analysis.tables.find(
    (t) => t.name === "country_dialling_codes"
  );

  assert(
    countriesEntry != null,
    "analysis.json contains countries table"
  );
  if (countriesEntry) {
    assert(
      JSON.stringify(countriesEntry.primaryKeyColumns) ===
        JSON.stringify(["iso_alpha2"]),
      `countries PK = [iso_alpha2] (got ${JSON.stringify(countriesEntry.primaryKeyColumns)})`
    );
  }

  assert(
    diallingEntry != null,
    "analysis.json contains country_dialling_codes table"
  );
  if (diallingEntry) {
    assert(
      JSON.stringify(diallingEntry.primaryKeyColumns) ===
        JSON.stringify(["country_code", "dialling_code"]),
      `country_dialling_codes PK = [country_code, dialling_code] (got ${JSON.stringify(diallingEntry.primaryKeyColumns)})`
    );
  }
}

// ---------------------------------------------------------------------------
// 6. Stale directories should not exist
// ---------------------------------------------------------------------------
console.log("\n--- No Stale Directories ---");

const staleDirs = [
  join(root, "Schema", "Triggers", "_generated"),
  join(root, "Schema", "Views", "_generated"),
  join(root, "Schema", "Procedures", "_generated"),
];
for (const dir of staleDirs) {
  const shortName = dir.replace(root, "").replace(/\\/g, "/");
  assert(!existsSync(dir), `${shortName} does not exist (consolidated)`);
}

// ---------------------------------------------------------------------------
// 7. Category bridging from analysis.json
// ---------------------------------------------------------------------------
console.log("\n--- Category Bridging ---");

if (existsSync(metadataPath)) {
  const raw = readFileSync(metadataPath, "utf8").replace(/^\uFEFF/, "");
  const metadata = JSON.parse(raw);
  const table = (name) => metadata.tables.find((t) => t.name === name);

  // Every source table should have its category from analysis.json
  const expectedCategories = {
    users: "core",
    organisations: "core",
    countries: "core",
    orders: "commerce",
    order_items: "commerce",
    comments: "social",
    audit_log: "audit",
    event_stream: "audit",
  };

  for (const [name, cat] of Object.entries(expectedCategories)) {
    const t = table(name);
    assert(
      t != null && t.category === cat,
      `${name}.category = "${cat}" (got "${t?.category}")`
    );
  }

  // History tables should not have a category
  assert(
    table("users_history")?.category == null,
    "History table users_history has no category"
  );
}

// ---------------------------------------------------------------------------
// 8. Structured type decomposition (maxLength, precision, scale, isMaxLength)
// ---------------------------------------------------------------------------
console.log("\n--- Structured Type Properties ---");

if (existsSync(metadataPath)) {
  const raw = readFileSync(metadataPath, "utf8").replace(/^\uFEFF/, "");
  const metadata = JSON.parse(raw);
  const col = (tableName, colName) =>
    metadata.tables.find((t) => t.name === tableName)
      ?.columns.find((c) => c.name === colName);

  // VARCHAR(MAX) - isMaxLength = true, maxLength absent
  const payload = col("audit_log", "payload");
  assert(payload?.type === "varchar(max)", "audit_log.payload type = varchar(max)");
  assert(payload?.isMaxLength === true, "audit_log.payload.isMaxLength = true");
  assert(
    payload?.maxLength == null,
    "audit_log.payload.maxLength is null (MAX has no numeric length)"
  );

  // DECIMAL(18,2) - precision and scale
  const amount = col("orders", "total_amount");
  assert(amount?.type === "decimal(18,2)", "orders.total_amount type = decimal(18,2)");
  assert(amount?.precision === 18, "orders.total_amount.precision = 18");
  assert(amount?.scale === 2, "orders.total_amount.scale = 2");
  assert(amount?.maxLength == null, "orders.total_amount.maxLength is null");

  // VARCHAR(320) - maxLength set, isMaxLength false
  const email = col("users", "email");
  assert(email?.type === "varchar(320)", "users.email type = varchar(320)");
  assert(email?.maxLength === 320, "users.email.maxLength = 320");
  assert(email?.isMaxLength === false, "users.email.isMaxLength = false");

  // UNIQUEIDENTIFIER - no structured type properties
  const userId = col("users", "id");
  assert(userId?.maxLength == null, "users.id.maxLength is null (no sizing)");
  assert(userId?.precision == null, "users.id.precision is null");
  assert(userId?.scale == null, "users.id.scale is null");
  assert(userId?.isMaxLength === false, "users.id.isMaxLength = false");
}

// ---------------------------------------------------------------------------
// 9. Polymorphic FK column marking
// ---------------------------------------------------------------------------
console.log("\n--- Polymorphic FK Columns ---");

if (existsSync(metadataPath)) {
  const raw = readFileSync(metadataPath, "utf8").replace(/^\uFEFF/, "");
  const metadata = JSON.parse(raw);
  const comments = metadata.tables.find((t) => t.name === "comments");
  const col = (name) => comments?.columns.find((c) => c.name === name);

  assert(comments?.isPolymorphic === true, "comments.isPolymorphic = true");
  assert(
    col("owner_type")?.isPolymorphicForeignKey === true,
    "comments.owner_type.isPolymorphicForeignKey = true"
  );
  assert(
    col("owner_id")?.isPolymorphicForeignKey === true,
    "comments.owner_id.isPolymorphicForeignKey = true"
  );
  assert(
    col("body")?.isPolymorphicForeignKey === false,
    "comments.body.isPolymorphicForeignKey = false (non-polymorphic column)"
  );
}

// ---------------------------------------------------------------------------
// 10. Composite FK column-level metadata
// ---------------------------------------------------------------------------
console.log("\n--- Composite FK Columns ---");

if (existsSync(metadataPath)) {
  const raw = readFileSync(metadataPath, "utf8").replace(/^\uFEFF/, "");
  const metadata = JSON.parse(raw);
  const mp = metadata.tables.find((t) => t.name === "member_permissions");
  const col = (name) => mp?.columns.find((c) => c.name === name);

  // Composite FK columns should be flagged
  const orgId = col("organisation_id");
  assert(
    orgId?.isCompositeFK === true,
    "member_permissions.organisation_id.isCompositeFK = true"
  );
  assert(
    orgId?.foreignKey?.table === "organisation_members",
    "member_permissions.organisation_id FK references organisation_members"
  );
  assert(
    orgId?.foreignKey?.column === "organisation_id",
    "member_permissions.organisation_id FK column = organisation_id"
  );

  const uid = col("user_id");
  assert(
    uid?.isCompositeFK === true,
    "member_permissions.user_id.isCompositeFK = true"
  );
  assert(
    uid?.foreignKey?.table === "organisation_members",
    "member_permissions.user_id FK references organisation_members"
  );
  assert(
    uid?.foreignKey?.column === "user_id",
    "member_permissions.user_id FK column = user_id"
  );

  // Single-column FK should NOT be marked composite
  const orders = metadata.tables.find((t) => t.name === "orders");
  const orderUserId = orders?.columns.find((c) => c.name === "user_id");
  assert(
    orderUserId?.isCompositeFK === false,
    "orders.user_id.isCompositeFK = false (single-column FK)"
  );
  assert(
    orderUserId?.foreignKey?.table === "users",
    "orders.user_id FK references users"
  );
}

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------
console.log("\n" + "=".repeat(60));
if (failures > 0) {
  console.error(`FAILED: ${failures} check(s) failed`);
  process.exit(1);
} else {
  console.log("ALL CHECKS PASSED");
}

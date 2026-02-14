# SchemaTools

MSBuild tasks for SQL Server schema metadata generation, validation, and documentation.

[![NuGet](https://img.shields.io/nuget/v/SchemaTools.svg)](https://www.nuget.org/packages/SchemaTools/)
[![Licence: MIT](https://img.shields.io/badge/Licence-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- **Pattern detection** - soft-delete, polymorphic relationships, append-only tables, temporal versioning; all column names are configurable
- **Cascade soft-delete triggers** - parent tables automatically propagate soft-delete to children via generated `AFTER UPDATE` triggers
- **Active-record views** - generated views filter to `active = 1`, eliminating manual `WHERE` clauses in application queries
- **Deferred purge procedure** - FK-safe hard-deletion in topological order via `usp_purge_soft_deleted` stored procedure with configurable grace period
- **Schema validation** - FK referential integrity, circular FK detection, `snake_case` naming conventions, temporal structure, audit columns, polymorphic structure, primary key presence
- **Documentation** - Markdown with Mermaid ER diagrams, category grouping, statistics, constraints
- **Pre-build/post-build pipeline** - triggers, procedures, and views generated pre-build (included in dacpac); validation and docs generated post-build from compiled dacpac

## Architecture Overview

SchemaTools operates in two distinct phases:

### Pre-Build Phase (Source -> Dacpac)

1. **SchemaToolsAnalyse** - Parses `@(Build)` SQL files via ScriptDom, builds FK dependency graph, detects soft-delete patterns
2. **SchemaToolsGenerateTriggers** - Generates CASCADE soft-delete and reactivation guard triggers
3. **SchemaToolsGenerateProcedures** - Generates `usp_purge_soft_deleted` stored procedure
4. **SchemaToolsGenerateViews** - Generates active-record views for soft-delete tables
5. **SchemaToolsIncludeGenerated** - Adds generated files to `@(Build)` for dacpac compilation

### Post-Build Phase (Dacpac -> Artifacts)

6. **SchemaToolsExtractMetadata** - Extracts authoritative metadata from compiled `.dacpac` via DacFx/TSqlModel
6. **SchemaToolsValidate** - Validates the compiled schema
7. **SchemaToolsGenerateDocs** - Generates Markdown documentation

This design ensures:

- Generated triggers are **semantically validated** by SqlBuild
- FK relationships are **authoritatively resolved** from the compiled model
- Validation and documentation reflect the **actual deployable schema**

## Getting Started

### 1. Install

```bash
dotnet add package SchemaTools
```

### 2. Configure

SchemaTools uses two configuration layers:

| Layer                  | Purpose                                           | Location            |
| ---------------------- | ------------------------------------------------- | ------------------- |
| **MSBuild Properties** | Build integration, output paths, feature toggles  | `.sqlproj` file     |
| **JSON Configuration** | Schema semantics, column naming, validation rules | `schema-tools.json` |

#### MSBuild Properties (.sqlproj)

Add to your `.sqlproj` to control build behaviour:

```xml
<PropertyGroup>
  <!-- Master enable/disable -->
  <SchemaToolsEnabled>true</SchemaToolsEnabled>

  <!-- Output strategy: Source (committed) vs Intermediate (transient) -->
  <SchemaToolsOutputStrategy>Source</SchemaToolsOutputStrategy>

  <!-- Feature toggles -->
  <SchemaToolsGenerateTriggers>true</SchemaToolsGenerateTriggers>
  <SchemaToolsGenerateProcedures>true</SchemaToolsGenerateProcedures>
  <SchemaToolsGenerateViews>true</SchemaToolsGenerateViews>
  <SchemaToolsValidate>true</SchemaToolsValidate>
  <SchemaToolsGenerateDocs>true</SchemaToolsGenerateDocs>
</PropertyGroup>
```

**Output Strategy:**

| Strategy       | Description                  | Generated Files Location                | Use Case                              |
| -------------- | ---------------------------- | --------------------------------------- | ------------------------------------- |
| `Source`       | Files in project directory   | `Schema/Triggers/_generated/`, etc.     | Code review, commit to source control |
| `Intermediate` | Files in obj/bin directories | `$(IntermediateOutputPath)SchemaTools/` | CI/CD, no source pollution            |

Output strategy defaults by artifact:

| Artifact      | Source Strategy                 | Intermediate Strategy             |
| ------------- | ------------------------------- | --------------------------------- |
| Triggers      | `Schema/Triggers/_generated/`   | `obj/.../SchemaTools/Triggers/`   |
| Procedures    | `Schema/Procedures/_generated/` | `obj/.../SchemaTools/Procedures/` |
| Views         | `Schema/Views/_generated/`      | `obj/.../SchemaTools/Views/`      |
| Metadata JSON | `Build/schema-metadata.json`    | `obj/.../schema-metadata.json`    |
| Docs          | `Docs/SCHEMA.md`                | `bin/.../SCHEMA.md`               |

All paths can be explicitly overridden via MSBuild properties:

```xml
<PropertyGroup>
  <SchemaToolsTriggersOutput>$(MSBuildProjectDirectory)\Triggers</SchemaToolsTriggersOutput>
  <SchemaToolsDocsOutput>$(OutputPath)docs\SCHEMA.md</SchemaToolsDocsOutput>
</PropertyGroup>
```

#### Schema Configuration (schema-tools.json)

Create `schema-tools.json` in the project root. Contains schema semantics only (no paths):

```json
{
  "database": "MyDatabase",
  "defaultSchema": "dbo",
  "sqlServerVersion": "Sql170",

  "features": {
    "enableSoftDelete": true,
    "enableTemporalVersioning": true,
    "detectPolymorphicPatterns": true,
    "detectAppendOnlyTables": true,
    "generateReactivationGuards": true,
    "softDeleteMode": "cascade"
  },

  "purge": {
    "enabled": true,
    "procedureName": "usp_purge_soft_deleted",
    "defaultGracePeriodDays": 90,
    "defaultBatchSize": 1000
  },

  "validation": {
    "validateForeignKeys": true,
    "validatePolymorphic": true,
    "validateTemporal": true,
    "validateAuditColumns": true,
    "enforceNamingConventions": true,
    "treatWarningsAsErrors": false
  },

  "documentation": {
    "enabled": true,
    "includeErDiagrams": true,
    "includeStatistics": true,
    "includeConstraints": true,
    "includeIndexes": false
  },

  "categories": {
    "core": "Core entities",
    "reference": "Reference data"
  },

  "columns": {
    "active": "active",
    "activeValue": "1",
    "inactiveValue": "0",
    "createdAt": "created_at",
    "createdBy": "created_by",
    "updatedBy": "updated_by",
    "updatedByType": "UNIQUEIDENTIFIER",
    "validFrom": "valid_from",
    "validTo": "valid_to",
    "auditForeignKeyTable": "",
    "polymorphicPatterns": []
  },

  "overrides": {
    "audit_log": {
      "validation": { "validateAuditColumns": false }
    },
    "category:reference": {
      "features": { "enableSoftDelete": false }
    }
  }
}
```

### 3. Build

```bash
dotnet build MyDatabase.sqlproj
```

## Soft-Delete Architecture

SchemaTools implements a **cascade soft-delete + reactivation guard + deferred purge** pattern:

### Design Principles

1. **No immediate hard-delete** - Setting `active = 0` never immediately deletes data
2. **Cascade to children first** - Parent soft-delete automatically propagates to FK children
3. **Reactivation guards** - Children cannot be reactivated while their parent is inactive
4. **Deferred purge** - Hard-deletion happens via stored procedure after a configurable grace period
5. **FK-safe deletion order** - Purge deletes leaf tables first, parents last (topological order)
6. **Full recoverability** - Temporal history preserves all state changes
7. **Multi-column key support** - Composite primary keys and foreign keys are fully supported

### Soft-Delete Modes

The `softDeleteMode` setting controls trigger behaviour per table:

| Mode       | Trigger Type         | behaviour                                                                         |
| ---------- | -------------------- | --------------------------------------------------------------------------------- |
| `cascade`  | Cascade soft-delete  | Automatically propagates `active=0` to all FK children (default)                  |
| `restrict` | Restrict soft-delete | Blocks soft-delete if any active children exist; requires explicit child handling |
| `ignore`   | None                 | No triggers generated; table excluded from soft-delete trigger handling           |

Configure per-table via overrides:

```json
{
  "features": {
    "softDeleteMode": "cascade"
  },
  "overrides": {
    "users": {
      "features": { "softDeleteMode": "cascade" }
    },
    "products": {
      "features": { "softDeleteMode": "restrict" }
    },
    "audit_log": {
      "features": { "softDeleteMode": "ignore" }
    }
  }
}
```

**Restrict mode** generates a trigger that checks for active children before allowing soft-delete:

```sql
IF EXISTS (
    SELECT 1 FROM [dbo].[orders] c
    JOIN inserted i ON c.user_id = i.id
    JOIN deleted d ON i.id = d.id
    WHERE i.active = 0 AND d.active = 1
    AND c.active = 1
)
BEGIN
    RAISERROR('Cannot soft-delete [users]: Active children exist in [orders]. Delete children first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END
```

### Trigger Types

SchemaTools generates triggers based on the configured `softDeleteMode`:

| Trigger                     | Target        | Mode                 | Purpose                                              |
| --------------------------- | ------------- | -------------------- | ---------------------------------------------------- |
| **Cascade soft-delete**     | Parent tables | `cascade`            | Propagates `active=0` to all FK children             |
| **Restrict soft-delete**    | Parent tables | `restrict`           | Blocks `active=0` if any active children exist       |
| **Reactivation guard**      | Child tables  | `cascade`/`restrict` | Blocks `active=0->1` if any parent is inactive       |
| **Reactivation cascade**    | Parent tables | per-table opt-in     | Auto-reactivates children when parent is reactivated |

### Parent Tables (CASCADE Triggers)

Tables with FK children receive cascade soft-delete triggers:

```sql
CREATE TRIGGER [dbo].[trg_users_cascade_soft_delete]
ON [dbo].[users]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT UPDATE(active)
        RETURN;

    -- Only proceed if rows were actually soft-deleted (active: 1 -> 0)
    IF NOT EXISTS (
        SELECT 1 FROM inserted i
        JOIN deleted d ON i.id = d.id
        WHERE i.active = 0 AND d.active = 1
    )
        RETURN;

    -- Cascade to [dbo].[orders]
    UPDATE [dbo].[orders]
    SET active = 0, updated_by = (SELECT TOP 1 updated_by FROM inserted)
    WHERE user_id IN (
        SELECT i.id FROM inserted i
        JOIN deleted d ON i.id = d.id
        WHERE i.active = 0 AND d.active = 1
    )
    AND active = 1;
END;
GO
```

### Child Tables (Reactivation Guard Triggers)

Tables with FK references to soft-delete parent tables receive reactivation guard triggers. These prevent reactivating a child record when its parent is still inactive:

```sql
CREATE TRIGGER [dbo].[trg_orders_reactivation_guard]
ON [dbo].[orders]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT UPDATE(active)
        RETURN;

    -- Check if any rows are being reactivated (active: 0 -> 1)
    IF NOT EXISTS (
        SELECT 1 FROM inserted i
        JOIN deleted d ON i.id = d.id
        WHERE i.active = 1 AND d.active = 0
    )
        RETURN;

    -- Check parent: [dbo].[users]
    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON i.id = d.id
        JOIN [dbo].[users] p ON i.user_id = p.id
        WHERE i.active = 1 AND d.active = 0
        AND p.active = 0
    )
    BEGIN
        RAISERROR('Cannot reactivate [orders]: Parent [users] is inactive. Reactivate parent first.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO
```

This ensures that the soft-delete hierarchy is always consistent: you can only reactivate a child after reactivating its parent.

### Reactivation Cascade (Opt-in)

For 1:1 relationships (e.g., `user` -> `user_profile`), you may want to auto-reactivate children when the parent is reactivated. Enable per-table via overrides:

```json
{
  "overrides": {
    "users": {
      "features": { "reactivationCascade": true }
    }
  }
}
```

The generated trigger only reactivates children that were soft-deleted **at the same time** as the parent (within 2 seconds based on `valid_to` timestamp):

```sql
CREATE TRIGGER [dbo].[trg_users_cascade_reactivation]
ON [dbo].[users]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT UPDATE(active)
        RETURN;

    -- Only proceed if rows were reactivated (active: 0 -> 1)
    IF NOT EXISTS (
        SELECT 1 FROM inserted i
        JOIN deleted d ON i.id = d.id
        WHERE i.active = 1 AND d.active = 0
    )
        RETURN;

    -- Cascade reactivation to [dbo].[user_profile]
    -- Only reactivate children deleted at the same time (within 2 seconds)
    UPDATE c
    SET c.active = 1, c.updated_by = @updated_by
    FROM [dbo].[user_profile] c
    WHERE EXISTS (
        SELECT 1 FROM inserted i
        JOIN deleted d ON i.id = d.id
        WHERE i.active = 1 AND d.active = 0
        AND c.user_id = i.id
        AND ABS(DATEDIFF(SECOND, c.valid_to, d.valid_to)) <= 2
    )
    AND c.active = 0;
END;
GO
```

**Design rationale**: The timestamp matching prevents unintended resurrection of records that were explicitly deleted before the parent. For 1:many relationships (e.g., `user` -> `addresses`), leave this disabled and handle reactivation at the application layer.

### Leaf Tables

Tables with no FK children do **not** receive cascade triggers - there's nothing to cascade. However, if they have FK references to soft-delete parents, they still receive reactivation guard triggers.

### Composite Key Support

Triggers automatically handle composite primary keys and foreign keys:

```sql
-- For composite PK: tenant_id + user_id
JOIN deleted d ON i.tenant_id = d.tenant_id AND i.user_id = d.user_id

-- For composite FK
WHERE EXISTS (
    SELECT 1 FROM inserted i
    JOIN deleted d ON i.id = d.id
    WHERE i.active = 0 AND d.active = 1
    AND c.tenant_id = i.tenant_id AND c.user_id = i.user_id
)
```

### Purge Procedure

Hard-deletion is performed by a generated stored procedure:

```sql
EXEC [dbo].[usp_purge_soft_deleted]
    @grace_period_days = 90,  -- Days since soft-delete before purge
    @batch_size = 1000,       -- Records per table (0 = unlimited)
    @dry_run = 1;             -- Preview only
```

The procedure:

- Deletes in **FK-safe topological order** (leaves first)
- Runs in a **single transaction** for consistency
- Reports affected tables and counts
- Supports **dry-run mode** for verification

## Configuration Reference

### MSBuild Properties

These properties are set in your `.sqlproj` file and control build integration.

| Property                              | Default                                        | Description                                                          |
| ------------------------------------- | ---------------------------------------------- | -------------------------------------------------------------------- |
| `SchemaToolsEnabled`                  | `true`                                         | Master enable/disable for all SchemaTools functionality              |
| `SchemaToolsOutputStrategy`           | `Source`                                       | `Source` (committed to VCS) or `Intermediate` (transient in obj/bin) |
| `SchemaToolsGenerateTriggers`         | `true`                                         | Generate soft-delete triggers                                        |
| `SchemaToolsGenerateViews`            | `true`                                         | Generate active-record views for soft-delete tables                  |
| `SchemaToolsGenerateProcedures`       | `true`                                         | Generate purge stored procedure                                      |
| `SchemaToolsValidate`                 | `true`                                         | Run schema validation post-build                                     |
| `SchemaToolsGenerateDocs`             | `true`                                         | Generate Markdown documentation                                      |
| `SchemaToolsExtractPostBuildMetadata` | `true`                                         | Extract metadata from compiled dacpac                                |
| `SchemaToolsConfig`                   | `$(MSBuildProjectDirectory)\schema-tools.json` | Path to JSON configuration file                                      |

**Path overrides** (optional - defaults based on `SchemaToolsOutputStrategy`):

| Property                      | Source Default                 | Intermediate Default                              |
| ----------------------------- | ------------------------------ | ------------------------------------------------- |
| `SchemaToolsTriggersOutput`   | `Schema\Triggers\_generated`   | `$(IntermediateOutputPath)SchemaTools\Triggers`   |
| `SchemaToolsViewsOutput`      | `Schema\Views\_generated`      | `$(IntermediateOutputPath)SchemaTools\Views`      |
| `SchemaToolsProceduresOutput` | `Schema\Procedures\_generated` | `$(IntermediateOutputPath)SchemaTools\Procedures` |
| `SchemaToolsMetadataOutput`   | `Build\schema-metadata.json`   | `$(IntermediateOutputPath)schema-metadata.json`   |
| `SchemaToolsDocsOutput`       | `Docs\SCHEMA.md`               | `$(OutputPath)SCHEMA.md`                          |

### Features (JSON)

| Setting                      | Default     | Description                                                                                                        |
| ---------------------------- | ----------- | ------------------------------------------------------------------------------------------------------------------ |
| `enableSoftDelete`           | `true`      | Detect soft-delete pattern (temporal + `active` column)                                                            |
| `enableTemporalVersioning`   | `true`      | Detect and validate temporal tables                                                                                |
| `detectPolymorphicPatterns`  | `true`      | Detect polymorphic relationships (`owner_type`/`owner_id`)                                                         |
| `detectAppendOnlyTables`     | `true`      | Detect append-only tables (has `created_at`, no `updated_by`, non-temporal)                                        |
| `generateReactivationGuards` | `true`      | Generate reactivation guard triggers for child tables                                                              |
| `reactivationCascade`        | `false`     | Auto-reactivate children when parent is reactivated (per-table via overrides)                                      |
| `softDeleteMode`             | `"cascade"` | Trigger behaviour: `cascade` (propagate to children), `restrict` (block if children exist), `ignore` (no triggers) |

### Purge

Settings for the centralized purge procedure that handles hard-deletion.

| Setting                  | Default                    | Description                                                    |
| ------------------------ | -------------------------- | -------------------------------------------------------------- |
| `enabled`                | `true`                     | Generate the `usp_purge_soft_deleted` stored procedure         |
| `procedureName`          | `"usp_purge_soft_deleted"` | Name of the generated purge procedure                          |
| `defaultGracePeriodDays` | `90`                       | Default grace period before soft-deleted records can be purged |
| `defaultBatchSize`       | `1000`                     | Default batch size for deletion operations                     |

### Views

Settings for auto-generated views that filter to active records, eliminating manual `WHERE active = 1` clauses.

| Setting                    | Default                 | Description                                                                |
| -------------------------- | ----------------------- | -------------------------------------------------------------------------- |
| `enabled`                  | `true`                  | Generate views for soft-delete tables                                      |
| `namingPattern`            | `"vw_{table}"`          | View name pattern (`{table}` replaced with table name)                     |
| `includeDeletedViews`      | `false`                 | Also generate views for deleted records (`active = 0`)                     |
| `deletedViewNamingPattern` | `"vw_{table}_deleted"`  | Deleted view name pattern                                                  |

**Explicit-wins policy**: If you define a view matching the naming pattern (e.g., `vw_users`), SchemaTools will not generate a conflicting view for that table.

**Example output** for a table `dbo.users` with soft-delete:

```sql
CREATE VIEW [dbo].[vw_users]
AS
SELECT *
FROM [dbo].[users]
WHERE [active] = 1;
GO
```

### Validation

| Setting                    | Default | Description                                                           |
| -------------------------- | ------- | --------------------------------------------------------------------- |
| `validateForeignKeys`      | `true`  | Validate FK references exist across all tables                        |
| `validatePolymorphic`      | `true`  | Validate polymorphic table structure and metadata                     |
| `validateTemporal`         | `true`  | Validate temporal columns (`valid_from`/`valid_to`) and history table |
| `validateAuditColumns`     | `true`  | Validate `created_by`/`updated_by` presence                           |
| `enforceNamingConventions` | `true`  | Enforce `snake_case` naming for tables, columns, FKs, and PKs         |
| `treatWarningsAsErrors`    | `false` | Treat validation warnings as build errors                             |

The following validations always run: primary key presence, circular FK detection, soft-delete consistency, and unique constraint column validity.

### Documentation

| Setting              | Default | Description                              |
| -------------------- | ------- | ---------------------------------------- |
| `enabled`            | `true`  | Generate Markdown documentation          |
| `includeErDiagrams`  | `true`  | Include Mermaid ER diagrams per category |
| `includeStatistics`  | `true`  | Include schema statistics summary        |
| `includeConstraints` | `true`  | Include constraint details per table     |
| `includeIndexes`     | `false` | Include index details per table          |

### Columns

Column names used for pattern detection. All comparisons are case-insensitive.

| Setting                | Default              | Description                                                                                   |
| ---------------------- | -------------------- | --------------------------------------------------------------------------------------------- |
| `active`               | `"active"`           | Soft-delete flag column                                                                       |
| `activeValue`          | `"1"`                | SQL literal representing active state                                                         |
| `inactiveValue`        | `"0"`                | SQL literal representing inactive/soft-deleted state                                          |
| `createdAt`            | `"created_at"`       | Append-only timestamp column                                                                  |
| `createdBy`            | `"created_by"`       | Audit column: creator                                                                         |
| `updatedBy`            | `"updated_by"`       | Audit column: last updater                                                                    |
| `updatedByType`        | `"UNIQUEIDENTIFIER"` | SQL data type for `updatedBy` column in triggers                                              |
| `validFrom`            | `"valid_from"`       | Temporal period start column                                                                  |
| `validTo`              | `"valid_to"`         | Temporal period end column                                                                    |
| `auditForeignKeyTable` | `""`                 | Table that audit columns (`createdBy`/`updatedBy`) reference as FK. Empty = no FK validation. |
| `polymorphicPatterns`  | `[]`                 | Array of type/ID column pairs for polymorphic detection                                       |

To configure polymorphic patterns:

```json
{
  "polymorphicPatterns": [
    { "typeColumn": "owner_type", "idColumn": "owner_id" },
    { "typeColumn": "account_type", "idColumn": "account_id" }
  ]
}
```

### Overrides

Per-table or per-category feature and validation overrides. Keys can be:

- **Exact table name** - `"audit_log"`
- **Category prefix** - `"category:reference"`
- **Glob pattern** - `"staging_*"`

Only non-null properties take effect; everything else inherits from the global config. Both `features` and `validation` blocks are supported, with the same property names as the global sections but as nullable booleans.

## SQL Annotations

Annotate table files with SQL comments to set category and description. **Only `@category` and `@description` are supported.** Any other annotations are silently ignored.

| Annotation     | Purpose                                                                 | Example                               |
| -------------- | ----------------------------------------------------------------------- | ------------------------------------- |
| `@category`    | Assigns the table to a named category for grouping in docs and metadata | `-- @category core`                   |
| `@description` | Free-text description included in metadata and documentation            | `-- @description User accounts table` |

Annotations must appear as line comments (`--`) before the `CREATE TABLE` statement:

```sql
-- @category core
-- @description User accounts table

CREATE TABLE [dbo].[users]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [username] VARCHAR(100) NOT NULL,
    [active] BIT NOT NULL DEFAULT 1,
    [created_by] UNIQUEIDENTIFIER NOT NULL,
    [updated_by] UNIQUEIDENTIFIER NOT NULL,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[users_history]));
GO
```

## Pattern Detection

### Soft Delete

Detected when a table has both temporal versioning (`SYSTEM_VERSIONING = ON`) and the configured active column (default: `active`).

**For parent tables** (tables referenced by other tables via FK), a cascade soft-delete trigger is generated.

**For leaf tables** (no FK children), no trigger is generated - the cascade originates from their parent.

Setting `active = 0` on a parent cascades deactivation to all children. To hard-delete, use the purge procedure after the grace period:

```sql
-- Soft-delete a user (cascades to orders, preferences, etc.)
UPDATE users SET active = 0, updated_by = @user_id WHERE id = @target_id;

-- Later, purge after 90-day grace period
EXEC usp_purge_soft_deleted @grace_period_days = 90;
```

To query soft-deleted records from temporal history:

```sql
SELECT * FROM users FOR SYSTEM_TIME ALL
WHERE id = @id AND active = 0
  AND NOT EXISTS (SELECT 1 FROM users WHERE id = @id)
```

### Polymorphic

Detected when a table has a type discriminator column and a corresponding ID column matching one of the configured `polymorphicPatterns` (default: `owner_type`/`owner_id` and `account_type`/`account_id`), plus a CHECK constraint on the type column:

```sql
CREATE TABLE [dbo].[addresses]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [owner_type] VARCHAR(20) NOT NULL
        CONSTRAINT [ck_addresses_owner_type]
        CHECK ([owner_type] IN ('user', 'company')),
    [owner_id] UNIQUEIDENTIFIER NOT NULL,
    [street] VARCHAR(200) NOT NULL
);
```

### Append-Only

Detected when a non-temporal table has the configured `createdAt` column (default: `created_at`) but no `updatedBy` column (default: `updated_by`) - indicating immutable audit-style records.

## MSBuild Properties

> **Important:** The SchemaTools NuGet package automatically registers all MSBuild targets and tasks. Do **not** add manual `<UsingTask>` or `<Target>` elements for SchemaTools in your project file - doing so will cause duplicate-task errors at build time. Only use the `<PropertyGroup>` overrides documented below.

### Default Paths

| Property                      | Default                                                   | Description                       |
| ----------------------------- | --------------------------------------------------------- | --------------------------------- |
| `SchemaToolsConfig`           | `$(MSBuildProjectDirectory)\schema-tools.json`            | Configuration file                |
| `SchemaToolsAnalysisOutput`   | `$(IntermediateOutputPath)schema-analysis.json`           | Pre-build analysis (intermediate) |
| `SchemaToolsMetadataOutput`   | `$(MSBuildProjectDirectory)\Build\schema-metadata.json`   | Post-build metadata from dacpac   |
| `SchemaToolsDocsOutput`       | `$(MSBuildProjectDirectory)\Docs\SCHEMA.md`               | Generated documentation           |
| `SchemaToolsTriggersOutput`   | `$(MSBuildProjectDirectory)\Schema\Triggers\_generated`   | Generated triggers                |
| `SchemaToolsViewsOutput`      | `$(MSBuildProjectDirectory)\Schema\Views\_generated`      | Generated views                   |
| `SchemaToolsProceduresOutput` | `$(MSBuildProjectDirectory)\Schema\Procedures\_generated` | Generated procedures              |

### Feature Flags

Disable individual pipeline stages:

```xml
<PropertyGroup>
  <SchemaToolsEnabled>false</SchemaToolsEnabled>                      <!-- All stages -->
  <SchemaToolsGenerateTriggers>false</SchemaToolsGenerateTriggers>    <!-- Cascade triggers -->
  <SchemaToolsGenerateViews>false</SchemaToolsGenerateViews>          <!-- Active-record views -->
  <SchemaToolsGenerateProcedures>false</SchemaToolsGenerateProcedures><!-- Purge procedure -->
  <SchemaToolsValidate>false</SchemaToolsValidate>                    <!-- Post-build validation -->
  <SchemaToolsGenerateDocs>false</SchemaToolsGenerateDocs>            <!-- Documentation -->
  <SchemaToolsExtractPostBuildMetadata>false</SchemaToolsExtractPostBuildMetadata>
</PropertyGroup>
```

### Custom Output Paths

```xml
<PropertyGroup>
  <SchemaToolsMetadataOutput>$(MSBuildProjectDirectory)\custom\metadata.json</SchemaToolsMetadataOutput>
  <SchemaToolsDocsOutput>$(MSBuildProjectDirectory)\custom\docs.md</SchemaToolsDocsOutput>
  <SchemaToolsTriggersOutput>$(MSBuildProjectDirectory)\custom\triggers</SchemaToolsTriggersOutput>
  <SchemaToolsViewsOutput>$(MSBuildProjectDirectory)\custom\views</SchemaToolsViewsOutput>
  <SchemaToolsProceduresOutput>$(MSBuildProjectDirectory)\custom\procedures</SchemaToolsProceduresOutput>
</PropertyGroup>
```

Validation settings are controlled exclusively through `schema-tools.json`.

## Generated Code Management

SchemaTools follows an **explicit-wins** policy: user-defined triggers and views always take precedence over generated ones.

### How It Works

During the analysis phase, SchemaTools scans all `@(Build)` items for existing `CREATE TRIGGER` and `CREATE VIEW` statements. If a trigger or view with the same name as one we would generate is found **outside** the `_generated/` directory, generation is skipped and the source location is logged:

```
- Skipped [dbo].[trg_users_cascade_soft_delete]: Explicit definition found
  Source: Schema/Triggers/my_triggers.sql
```

This means you can:

- Define triggers alongside your tables (common DBA practice)
- Put triggers anywhere in your project structure
- Override generated triggers simply by defining your own

### Directory Structure

```
Schema/
  Tables/
    users.sql
    orders.sql
  Triggers/
    _generated/                              <- Auto-generated (safe to commit)
      trg_users_cascade_soft_delete.sql      <- Cascade trigger for parent
      trg_orders_reactivation_guard.sql      <- Guard trigger for child
    my_custom_triggers.sql                   <- Your triggers (takes precedence)
  Views/
    _generated/                              <- Auto-generated active-record views
      vw_users.sql                           <- SELECT * WHERE active = 1
      vw_orders.sql
    my_custom_views.sql                      <- Your views (takes precedence)
  Procedures/
    _generated/                              <- Auto-generated purge procedure
      usp_purge_soft_deleted.sql
Build/
  schema-metadata.json                       <- Post-build extracted metadata
Docs/
  SCHEMA.md                                  <- Generated documentation
```

### Overriding a Generated Trigger

**Option 1: Define your own anywhere in the project**

```sql
-- Schema/Triggers/users_triggers.sql
CREATE TRIGGER [dbo].[trg_users_cascade_soft_delete]
ON [dbo].[users]
AFTER UPDATE
AS
BEGIN
    -- Your custom implementation
END;
GO
```

SchemaTools will detect this and skip generation automatically.

**Option 2: Copy and modify**

1. Copy `_generated/trg_users_cascade_soft_delete.sql` to `Schema/Triggers/`
2. Modify as needed
3. Delete the original from `_generated/`
4. Next build will detect your version and skip generation

### What Gets Generated vs Skipped

| Trigger Exists In     | `_generated/` File | Action                       |
| --------------------- | ------------------ | ---------------------------- |
| Nowhere               | Does not exist     | **Generate**                 |
| `_generated/` only    | Exists             | **Skip** (already generated) |
| Outside `_generated/` | May exist          | **Skip** (explicit wins)     |
| Outside `_generated/` | Does not exist     | **Skip** (explicit wins)     |

### Regeneration

Generated files include a `-- DO NOT EDIT MANUALLY` header. To force regeneration:

```bash
dotnet build /p:Force=true
```

This regenerates files in `_generated/` but still respects explicit triggers elsewhere.

## Generated Metadata

Post-build, `Build/schema-metadata.json` is extracted from the compiled `.dacpac`:

```json
{
  "$schema": "./schema-metadata.schema.json",
  "version": "1.0.0",
  "generatedAt": "...",
  "generatedBy": "SchemaMetadataExtractor (DacFx)",
  "database": "MyDatabase",
  "defaultSchema": "dbo",
  "sqlServerVersion": "Sql170",
  "tables": [
    {
      "name": "users",
      "schema": "dbo",
      "category": "core",
      "description": "User accounts table",
      "hasTemporalVersioning": true,
      "hasActiveColumn": true,
      "hasSoftDelete": true,
      "isAppendOnly": false,
      "isPolymorphic": false,
      "primaryKey": "id",
      "historyTable": "[dbo].[users_history]",
      "columns": [ ... ],
      "constraints": { ... }
    }
  ],
  "statistics": {
    "totalTables": 10,
    "temporalTables": 8,
    "softDeleteTables": 8,
    "parentTablesWithCascade": 3,
    "leafTables": 5
  }
}
```

This metadata is authoritative - it reflects the actual compiled schema including all resolved references.

## Supported SQL Server Versions

| Version         | `sqlServerVersion` |
| --------------- | ------------------ |
| SQL Server 2016 | `Sql130`           |
| SQL Server 2017 | `Sql140`           |
| SQL Server 2019 | `Sql150`           |
| SQL Server 2022 | `Sql160`           |
| SQL Server 2025 | `Sql170` (default) |

## Roadmap

### Near-Term

- **Drift detection** - detect when generated files differ from expected output; warn or fail build
- **Incremental generation** - skip unchanged tables to improve build times
- **JSON Schema generation** - emit `schema-metadata.schema.json` alongside metadata
- **Extended SQL annotations**:
  - `@suppress` - silence specific validation warnings per table
  - `@column <name> <text>` - per-column descriptions
  - `@trigger <name> <description>` - document custom triggers

### Mid-Term

- **Static documentation site** - generate a browsable site (using Laika or similar) with:
  - Navigable FK relationships (click from child to parent and vice versa)
  - Full-text search across table/column names and descriptions
  - Visual dependency graphs at the category and schema level
  - Dark/light theme support
- **Mermaid relationship navigation** - interactive ER diagrams with clickable links
- **Change history reports** - compare schema versions and report additions/removals/modifications

### Long-Term

- **dacpac diff integration** - compare two dacpacs and generate migration impact reports
- **Policy enforcement** - define and enforce schema policies (e.g., "all tables must have PK", "FK columns must end with \_id")
- **IDE integration** - VS Code extension for inline schema documentation and validation warnings

## Licence

MIT

## Contributing

Issues and pull requests welcome at: https://github.com/arashi01/schema-tools


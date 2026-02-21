using System.Text.Json;
using SchemaTools.Configuration;
using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class SchemaSourceAnalyserTests : IDisposable
{
  private readonly string _tempDir;
  private readonly string _tablesDir;
  private readonly string _outputFile;
  private readonly MockBuildEngine _buildEngine;

  public SchemaSourceAnalyserTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"analyser-tests-{Guid.NewGuid():N}");
    _tablesDir = Path.Combine(_tempDir, "Tables");
    _outputFile = Path.Combine(_tempDir, "analysis.json");
    Directory.CreateDirectory(_tablesDir);
    _buildEngine = new MockBuildEngine();
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
    {
      Directory.Delete(_tempDir, recursive: true);
    }
    GC.SuppressFinalize(this);
  }

  private SchemaSourceAnalyser CreateAnalyser(SchemaToolsConfig? config = null) => new()
  {
    TablesDirectory = _tablesDir,
    AnalysisOutput = _outputFile,
    GeneratedTriggersDirectory = Path.Combine(_tempDir, "_generated"),
    TestConfig = config ?? new SchemaToolsConfig
    {
      DefaultSchema = "dbo",
      SqlServerVersion = Models.SqlServerVersion.Sql170,
      Features = new FeatureConfig { EnableSoftDelete = true }
    },
    BuildEngine = _buildEngine
  };

  private void CreateTableFile(string fileName, string content)
  {
    File.WriteAllText(Path.Combine(_tablesDir, fileName), content);
  }

  private SourceAnalysisResult? ReadAnalysisResult()
  {
    if (!File.Exists(_outputFile))
      return null;

    string json = File.ReadAllText(_outputFile);
    return JsonSerializer.Deserialize<SourceAnalysisResult>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });
  }

  // --- Basic table analysis -------------------------------------------------

  [Fact]
  public void Execute_EmptyDirectory_ReturnsWarning()
  {
    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue();
    _buildEngine.Warnings.Should().Contain(w => w.Contains("No SQL files"));
  }

  [Fact]
  public void Execute_SimpleTable_AnalysesCorrectly()
  {
    CreateTableFile("users.sql", @"
CREATE TABLE [dbo].[users]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [name] NVARCHAR(100) NOT NULL
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis.Should().NotBeNull();
    analysis!.Tables.Should().ContainSingle();
    analysis.Tables[0].Name.Should().Be("users");
    analysis.Tables[0].Schema.Should().Be("dbo");
    analysis.Tables[0].HasSoftDelete.Should().BeFalse();
  }

  // --- Soft-delete detection ------------------------------------------------

  [Fact]
  public void Execute_SoftDeleteTable_DetectsPattern()
  {
    CreateTableFile("documents.sql", @"
-- @category core
CREATE TABLE [dbo].[documents]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [title] NVARCHAR(500) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[documents_history]));
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis.Should().NotBeNull();

    TableAnalysis table = analysis!.Tables.Single();
    table.HasSoftDelete.Should().BeTrue();
    table.HasActiveColumn.Should().BeTrue();
    table.HasTemporalVersioning.Should().BeTrue();
    table.ActiveColumnName.Should().Be("record_active");
    table.HistoryTable.Should().Be("[dbo].[documents_history]");
    table.Category.Should().Be("core");
  }

  [Fact]
  public void Execute_ActiveWithoutTemporal_NotSoftDelete()
  {
    CreateTableFile("partial.sql", @"
CREATE TABLE [dbo].[partial]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [record_active] BIT NOT NULL DEFAULT 1
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    TableAnalysis table = analysis!.Tables.Single();

    table.HasActiveColumn.Should().BeTrue();
    table.HasTemporalVersioning.Should().BeFalse();
    table.HasSoftDelete.Should().BeFalse("requires both active column AND temporal versioning");
  }

  [Fact]
  public void Execute_TemporalWithoutActive_NotSoftDelete()
  {
    CreateTableFile("temporal.sql", @"
CREATE TABLE [dbo].[temporal]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[temporal_history]));
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    TableAnalysis table = analysis!.Tables.Single();

    table.HasTemporalVersioning.Should().BeTrue();
    table.HasActiveColumn.Should().BeFalse();
    table.HasSoftDelete.Should().BeFalse("requires active column for soft-delete");
  }

  // --- Primary key extraction -----------------------------------------------

  [Fact]
  public void Execute_TableConstraintPK_ExtractsPrimaryKey()
  {
    CreateTableFile("pk_constraint.sql", @"
CREATE TABLE [dbo].[pk_constraint]
(
    [id] UNIQUEIDENTIFIER NOT NULL,
    [name] NVARCHAR(100),
    CONSTRAINT [pk_pk_constraint] PRIMARY KEY ([id])
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Single().PrimaryKeyColumns.Should().BeEquivalentTo(["id"]);
  }

  [Fact]
  public void Execute_InlinePK_ExtractsPrimaryKey()
  {
    CreateTableFile("inline_pk.sql", @"
CREATE TABLE [dbo].[inline_pk]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [name] NVARCHAR(100)
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Single().PrimaryKeyColumns.Should().BeEquivalentTo(["id"]);
  }

  [Fact]
  public void Execute_CompositePK_ExtractsAllColumns()
  {
    CreateTableFile("composite_pk.sql", @"
CREATE TABLE [dbo].[composite_pk]
(
    [tenant_id] UNIQUEIDENTIFIER NOT NULL,
    [entity_id] UNIQUEIDENTIFIER NOT NULL,
    [data] NVARCHAR(MAX),
    CONSTRAINT [pk_composite] PRIMARY KEY ([tenant_id], [entity_id])
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Single().PrimaryKeyColumns.Should().BeEquivalentTo(["tenant_id", "entity_id"]);
  }

  // --- Foreign key graph ----------------------------------------------------

  [Fact]
  public void Execute_ForeignKey_BuildsDependencyGraph()
  {
    CreateTableFile("orders.sql", @"
CREATE TABLE [dbo].[orders]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [customer_id] UNIQUEIDENTIFIER NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[orders_history]));
");

    CreateTableFile("order_items.sql", @"
CREATE TABLE [dbo].[order_items]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [order_id] UNIQUEIDENTIFIER NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),
    CONSTRAINT [fk_items_order] FOREIGN KEY ([order_id]) REFERENCES [dbo].[orders]([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[order_items_history]));
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();

    TableAnalysis orders = analysis!.Tables.Single(t => t.Name == "orders");
    TableAnalysis items = analysis.Tables.Single(t => t.Name == "order_items");

    // orders should know about its children
    orders.ChildTables.Should().Contain("order_items");

    // order_items should be a leaf
    items.IsLeafTable.Should().BeTrue();
    items.ChildTables.Should().BeEmpty();

    // order_items should have FK reference
    items.ForeignKeyReferences.Should().ContainSingle();
    items.ForeignKeyReferences[0].ReferencedTable.Should().Be("orders");
  }

  // --- Leaf table detection -------------------------------------------------

  [Fact]
  public void Execute_TableWithNoChildren_IsLeafTable()
  {
    CreateTableFile("leaf.sql", @"
CREATE TABLE [dbo].[leaf]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[leaf_history]));
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Single().IsLeafTable.Should().BeTrue();
  }

  // --- Custom active column name --------------------------------------------

  [Fact]
  public void Execute_CustomActiveColumn_DetectsWithConfig()
  {
    CreateTableFile("custom_active.sql", @"
CREATE TABLE [dbo].[custom_active]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [is_enabled] BIT NOT NULL DEFAULT 1,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[custom_active_history]));
");

    var config = new SchemaToolsConfig
    {
      DefaultSchema = "dbo",
      Features = new FeatureConfig { EnableSoftDelete = true },
      Columns = new ColumnNamingConfig { Active = "is_enabled" }
    };

    SchemaSourceAnalyser analyser = CreateAnalyser(config);
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    TableAnalysis table = analysis!.Tables.Single();

    table.HasSoftDelete.Should().BeTrue();
    table.ActiveColumnName.Should().Be("is_enabled");
  }

  // --- Soft-delete disabled -------------------------------------------------

  [Fact]
  public void Execute_SoftDeleteDisabled_DoesNotMarkTables()
  {
    CreateTableFile("soft_delete.sql", @"
CREATE TABLE [dbo].[soft_delete]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[soft_delete_history]));
");

    var config = new SchemaToolsConfig
    {
      DefaultSchema = "dbo",
      Features = new FeatureConfig { EnableSoftDelete = false }
    };

    SchemaSourceAnalyser analyser = CreateAnalyser(config);
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    TableAnalysis table = analysis!.Tables.Single();

    table.HasActiveColumn.Should().BeTrue("column detection still works");
    table.HasTemporalVersioning.Should().BeTrue("temporal detection still works");
    table.HasSoftDelete.Should().BeFalse("soft-delete disabled in config");
  }

  // --- Column config propagation --------------------------------------------

  [Fact]
  public void Execute_ColumnConfig_PropagatedToOutput()
  {
    CreateTableFile("dummy.sql", @"
CREATE TABLE [dbo].[dummy]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");

    var config = new SchemaToolsConfig
    {
      DefaultSchema = "dbo",
      Columns = new ColumnNamingConfig
      {
        Active = "is_active",
        ActiveValue = "'Y'",
        InactiveValue = "'N'",
        UpdatedBy = "modifier_id",
        UpdatedByType = "INT"
      }
    };

    SchemaSourceAnalyser analyser = CreateAnalyser(config);
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();

    analysis!.Columns.Active.Should().Be("is_active");
    analysis.Columns.ActiveValue.Should().Be("'Y'");
    analysis.Columns.InactiveValue.Should().Be("'N'");
    analysis.Columns.UpdatedBy.Should().Be("modifier_id");
    analysis.Columns.UpdatedByType.Should().Be("INT");
  }

  // --- Feature flags propagation --------------------------------------------

  [Fact]
  public void Execute_FeatureFlags_PropagatedToOutput()
  {
    CreateTableFile("dummy.sql", @"
CREATE TABLE [dbo].[dummy]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");

    var config = new SchemaToolsConfig
    {
      DefaultSchema = "dbo",
      Features = new FeatureConfig { GenerateReactivationGuards = false }
    };

    SchemaSourceAnalyser analyser = CreateAnalyser(config);
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Features.GenerateReactivationGuards.Should().BeFalse();
  }

  // --- Category extraction --------------------------------------------------

  [Fact]
  public void Execute_CategoryAnnotation_Extracted()
  {
    CreateTableFile("annotated.sql", @"
-- @category reference
-- @description Reference data table
CREATE TABLE [dbo].[annotated]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Single().Category.Should().Be("reference");
  }

  // --- Multiple tables ------------------------------------------------------

  [Fact]
  public void Execute_MultipleTables_AnalysesAll()
  {
    CreateTableFile("table1.sql", @"
CREATE TABLE [dbo].[table1]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");
    CreateTableFile("table2.sql", @"
CREATE TABLE [dbo].[table2]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");
    CreateTableFile("table3.sql", @"
CREATE TABLE [dbo].[table3]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Should().HaveCount(3);
    analysis.Tables.Select(t => t.Name).Should().BeEquivalentTo(["table1", "table2", "table3"]);
  }

  // --- Soft-delete mode propagation -----------------------------------------

  [Fact]
  public void Execute_SoftDeleteMode_PropagatedFromConfig()
  {
    CreateTableFile("cascade.sql", @"
CREATE TABLE [dbo].[cascade]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[cascade_history]));
");

    var config = new SchemaToolsConfig
    {
      DefaultSchema = "dbo",
      Features = new FeatureConfig
      {
        EnableSoftDelete = true,
        SoftDeleteMode = SoftDeleteMode.Restrict
      }
    };

    SchemaSourceAnalyser analyser = CreateAnalyser(config);
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Single().SoftDeleteMode.Should().Be(SoftDeleteMode.Restrict);
  }

  // --- Version and metadata -------------------------------------------------

  [Fact]
  public void Execute_OutputContainsMetadata()
  {
    CreateTableFile("meta.sql", @"
CREATE TABLE [dbo].[meta]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();

    analysis!.ToolVersion.Should().NotBeNullOrEmpty();
    analysis.SqlServerVersion.Should().Be(Models.SqlServerVersion.Sql170);
    analysis.DefaultSchema.Should().Be("dbo");
    analysis.AnalysedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
  }

  // --- Parse error handling -------------------------------------------------

  [Fact]
  public void Execute_InvalidSql_WarnsAndContinues()
  {
    CreateTableFile("invalid.sql", @"
CREATE TABL [dbo].[invalid]  -- typo: TABL instead of TABLE
(
    [id] UNIQUEIDENTIFIER PRIMARY KEY
);
");
    CreateTableFile("valid.sql", @"
CREATE TABLE [dbo].[valid]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue("should continue despite parse errors");

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Should().ContainSingle(t => t.Name == "valid");
  }

  // --- Temp table filtering -------------------------------------------------

  [Fact]
  public void Execute_TempTableInScript_ExcludedFromResults()
  {
    // Stored procedure scripts may contain CREATE TABLE #temp statements
    // that should not appear in the analysis output
    CreateTableFile("proc_with_temp.sql", @"
CREATE PROCEDURE [dbo].[usp_purge_soft_deleted]
AS
BEGIN
    CREATE TABLE #purge_results
    (
        [table_name] NVARCHAR(128),
        [rows_deleted] INT
    );
END;
");
    CreateTableFile("real_table.sql", @"
CREATE TABLE [dbo].[real_table]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Should().ContainSingle();
    analysis.Tables[0].Name.Should().Be("real_table");
  }

  [Fact]
  public void Execute_GlobalTempTable_ExcludedFromResults()
  {
    CreateTableFile("global_temp.sql", @"
CREATE TABLE ##global_temp
(
    [id] INT NOT NULL
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis!.Tables.Should().BeEmpty();
  }

  // --- FK action normalisation ----------------------------------------------

  [Fact]
  public void Execute_ForeignKeyWithCascade_OutputsCascade()
  {
    CreateTableFile("parent.sql", @"
CREATE TABLE [dbo].[parent]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");
    CreateTableFile("child.sql", @"
CREATE TABLE [dbo].[child]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [parent_id] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [fk_child_parent] FOREIGN KEY ([parent_id])
        REFERENCES [dbo].[parent]([id]) ON DELETE CASCADE
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    TableAnalysis child = analysis!.Tables.Single(t => t.Name == "child");
    child.ForeignKeyReferences.Should().ContainSingle()
      .Which.OnDelete.Should().Be(ForeignKeyAction.Cascade);
  }

  [Fact]
  public void Execute_ForeignKeyWithNoAction_OutputsNoAction()
  {
    CreateTableFile("parent.sql", @"
CREATE TABLE [dbo].[parent]([id] UNIQUEIDENTIFIER PRIMARY KEY);
");
    CreateTableFile("child.sql", @"
CREATE TABLE [dbo].[child]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [parent_id] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [fk_child_parent] FOREIGN KEY ([parent_id])
        REFERENCES [dbo].[parent]([id])
);
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    analyser.Execute();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    TableAnalysis child = analysis!.Tables.Single(t => t.Name == "child");
    child.ForeignKeyReferences.Should().ContainSingle()
      .Which.OnDelete.Should().Be(ForeignKeyAction.NoAction,
        "FKs without explicit action should normalise to NoAction, not NotSpecified");
  }

  // --- ALTER TABLE constraint extraction ------------------------------------

  [Fact]
  public void Execute_AlterTablePrimaryKey_ExtractsPkColumns()
  {
    CreateTableFile("countries.sql", @"
CREATE TABLE [dbo].[countries]
(
    [iso_alpha2] CHAR(2) NOT NULL,
    [name] NVARCHAR(200) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[hist_countries]));
GO

ALTER TABLE [dbo].[countries]
    ADD CONSTRAINT [pk_countries]
    PRIMARY KEY CLUSTERED ([iso_alpha2] ASC);
GO
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis.Should().NotBeNull();
    TableAnalysis table = analysis!.Tables.Single(t => t.Name == "countries");
    table.PrimaryKeyColumns.Should().BeEquivalentTo(new[] { "iso_alpha2" });
  }

  [Fact]
  public void Execute_AlterTableCompositePrimaryKey_ExtractsAllPkColumns()
  {
    CreateTableFile("country_dialling_codes.sql", @"
CREATE TABLE [dbo].[country_dialling_codes]
(
    [country_code] CHAR(2) NOT NULL,
    [dialling_code] VARCHAR(10) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[hist_country_dialling_codes]));
GO

ALTER TABLE [dbo].[country_dialling_codes]
    ADD CONSTRAINT [pk_country_dialling_codes]
    PRIMARY KEY CLUSTERED ([country_code] ASC, [dialling_code] ASC);
GO
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis.Should().NotBeNull();
    TableAnalysis table = analysis!.Tables.Single(t => t.Name == "country_dialling_codes");
    table.PrimaryKeyColumns.Should().BeEquivalentTo(new[] { "country_code", "dialling_code" });
  }

  [Fact]
  public void Execute_AlterTableForeignKey_ExtractsFkReferences()
  {
    CreateTableFile("countries.sql", @"
CREATE TABLE [dbo].[countries]
(
    [iso_alpha2] CHAR(2) NOT NULL,
    [name] NVARCHAR(200) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[hist_countries]));
GO

ALTER TABLE [dbo].[countries]
    ADD CONSTRAINT [pk_countries]
    PRIMARY KEY CLUSTERED ([iso_alpha2] ASC);
GO
");

    CreateTableFile("dialling_codes.sql", @"
CREATE TABLE [dbo].[dialling_codes]
(
    [country_code] CHAR(2) NOT NULL,
    [dialling_code] VARCHAR(10) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[hist_dialling_codes]));
GO

ALTER TABLE [dbo].[dialling_codes]
    ADD CONSTRAINT [pk_dialling_codes]
    PRIMARY KEY CLUSTERED ([country_code] ASC, [dialling_code] ASC);
GO

ALTER TABLE [dbo].[dialling_codes]
    ADD CONSTRAINT [fk_dialling_codes_countries]
    FOREIGN KEY ([country_code])
    REFERENCES [dbo].[countries] ([iso_alpha2]);
GO
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis.Should().NotBeNull();

    TableAnalysis child = analysis!.Tables.Single(t => t.Name == "dialling_codes");
    child.ForeignKeyReferences.Should().ContainSingle();
    child.ForeignKeyReferences[0].ReferencedTable.Should().Be("countries");
    child.ForeignKeyReferences[0].Columns.Should().BeEquivalentTo(new[] { "country_code" });
    child.ForeignKeyReferences[0].ReferencedColumns.Should().BeEquivalentTo(new[] { "iso_alpha2" });

    // Parent should have child listed
    TableAnalysis parent = analysis.Tables.Single(t => t.Name == "countries");
    parent.ChildTables.Should().Contain("dialling_codes");
  }

  [Fact]
  public void Execute_MultipleFksToSameParent_DeduplicatesChildTables()
  {
    CreateTableFile("users.sql", @"
CREATE TABLE [dbo].[users]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [name] NVARCHAR(200) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[users_history]));
GO
");

    CreateTableFile("orders.sql", @"
CREATE TABLE [dbo].[orders]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [created_by] UNIQUEIDENTIFIER NOT NULL,
    [updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),
    CONSTRAINT [fk_orders_created_by] FOREIGN KEY ([created_by]) REFERENCES [dbo].[users] ([id]),
    CONSTRAINT [fk_orders_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [dbo].[users] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[orders_history]));
GO
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis.Should().NotBeNull();

    TableAnalysis parent = analysis!.Tables.Single(t => t.Name == "users");
    parent.ChildTables.Should().ContainSingle("orders",
      "a child with multiple FKs to the same parent should appear only once");
  }

  [Fact]
  public void Execute_SelfReferencingForeignKey_AppearsInChildTables()
  {
    CreateTableFile("groups.sql", @"
CREATE TABLE [dbo].[groups]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [parent_group_id] UNIQUEIDENTIFIER NULL,
    [name] NVARCHAR(200) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),
    CONSTRAINT [fk_groups_parent] FOREIGN KEY ([parent_group_id]) REFERENCES [dbo].[groups] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[groups_history]));
GO
");

    SchemaSourceAnalyser analyser = CreateAnalyser();
    bool result = analyser.Execute();

    result.Should().BeTrue();

    SourceAnalysisResult? analysis = ReadAnalysisResult();
    analysis.Should().NotBeNull();

    TableAnalysis table = analysis!.Tables.Single(t => t.Name == "groups");
    table.ChildTables.Should().ContainSingle("groups",
      "self-referencing FK should list the table as its own child exactly once");
    table.IsLeafTable.Should().BeFalse("a self-referencing table has children");
  }
}



using System.Text.Json;
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
      SqlServerVersion = "Sql160",
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
    [active] BIT NOT NULL DEFAULT 1,
    [created_by] UNIQUEIDENTIFIER NOT NULL,
    [updated_by] UNIQUEIDENTIFIER NOT NULL,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
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
    table.ActiveColumnName.Should().Be("active");
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
    [active] BIT NOT NULL DEFAULT 1
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
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
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
    [active] BIT NOT NULL DEFAULT 1,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[orders_history]));
");

    CreateTableFile("order_items.sql", @"
CREATE TABLE [dbo].[order_items]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [order_id] UNIQUEIDENTIFIER NOT NULL,
    [active] BIT NOT NULL DEFAULT 1,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to]),
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
    [active] BIT NOT NULL DEFAULT 1,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
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
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
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
    [active] BIT NOT NULL DEFAULT 1,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
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
    [active] BIT NOT NULL DEFAULT 1,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
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

    analysis!.Version.Should().NotBeNullOrEmpty();
    analysis.SqlServerVersion.Should().Be("Sql160");
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
}



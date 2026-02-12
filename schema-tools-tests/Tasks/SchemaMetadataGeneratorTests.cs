using System.Text.Json;
using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class SchemaMetadataGeneratorTests : IDisposable
{
  private readonly string _testOutputPath;
  private readonly string _testTablesPath;

  private static SchemaToolsConfig CreateTestConfig() => new()
  {
    Database = "TestDB",
    DefaultSchema = "test",
    SqlServerVersion = "Sql160",
    Features = new FeatureConfig
    {
      EnableSoftDelete = true,
      EnableTemporalVersioning = true,
      GenerateHardDeleteTriggers = true,
      DetectPolymorphicPatterns = true,
      DetectAppendOnlyTables = true
    }
  };

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public SchemaMetadataGeneratorTests()
  {
    _testOutputPath = Path.Combine(Path.GetTempPath(), "schema-tools-tests", Guid.NewGuid().ToString());
    _testTablesPath = Path.Combine("Fixtures", "TestTables");
    Directory.CreateDirectory(_testOutputPath);
  }

  private SchemaMetadata RunGenerator(SchemaToolsConfig? config = null)
  {
    string outputFile = Path.Combine(_testOutputPath, $"{Guid.NewGuid()}.json");
    var task = new SchemaMetadataGenerator
    {
      TablesDirectory = _testTablesPath,
      OutputFile = outputFile,
      TestConfig = config ?? CreateTestConfig(),
      BuildEngine = new MockBuildEngine()
    };

    bool result = task.Execute();
    result.Should().BeTrue("the generator should succeed with valid SQL files");

    string json = File.ReadAllText(outputFile);
    SchemaMetadata? metadata = JsonSerializer.Deserialize<SchemaMetadata>(json, JsonOptions);
    metadata.Should().NotBeNull();
    return metadata!;
  }

  // ─── Error handling ──────────────────────────────────────────────

  [Fact]
  public void Execute_WithMissingDirectory_ReturnsFalse()
  {
    var engine = new MockBuildEngine();
    var task = new SchemaMetadataGenerator
    {
      TablesDirectory = Path.Combine(_testOutputPath, "nonexistent"),
      OutputFile = Path.Combine(_testOutputPath, "out.json"),
      TestConfig = CreateTestConfig(),
      BuildEngine = engine
    };

    task.Execute().Should().BeFalse();
    engine.Errors.Should().ContainMatch("*not found*");
  }

  [Fact]
  public void Execute_WithEmptyDirectory_ReturnsFalse()
  {
    string emptyDir = Path.Combine(_testOutputPath, "empty");
    Directory.CreateDirectory(emptyDir);

    var engine = new MockBuildEngine();
    var task = new SchemaMetadataGenerator
    {
      TablesDirectory = emptyDir,
      OutputFile = Path.Combine(_testOutputPath, "out.json"),
      TestConfig = CreateTestConfig(),
      BuildEngine = engine
    };

    task.Execute().Should().BeFalse();
    engine.Errors.Should().ContainMatch("*No SQL files*");
  }

  // ─── Schema-level output ─────────────────────────────────────────

  [Fact]
  public void Execute_WritesCorrectSchemaMetadata()
  {
    SchemaMetadata metadata = RunGenerator();

    metadata.Database.Should().Be("TestDB");
    metadata.DefaultSchema.Should().Be("test");
    metadata.SqlServerVersion.Should().Be("Sql160");
    metadata.Version.Should().NotBeNullOrEmpty();
    metadata.GeneratedBy.Should().NotBeNullOrEmpty();
    metadata.Tables.Should().HaveCountGreaterThanOrEqualTo(4);
  }

  [Fact]
  public void Execute_ComputesAccurateStatistics()
  {
    SchemaMetadata metadata = RunGenerator();

    metadata.Statistics.TotalTables.Should().Be(metadata.Tables.Count);
    metadata.Statistics.TemporalTables.Should().BeGreaterThanOrEqualTo(3,
        "soft_delete_table, temporal_table, and polymorphic_table all have SYSTEM_VERSIONING");
    metadata.Statistics.SoftDeleteTables.Should().BeGreaterThanOrEqualTo(1);
    metadata.Statistics.PolymorphicTables.Should().BeGreaterThanOrEqualTo(1);
    metadata.Statistics.TotalColumns.Should().Be(
        metadata.Tables.Sum(t => t.Columns.Count));
  }

  // ─── Comment metadata extraction ────────────────────────────────

  [Fact]
  public void Execute_ExtractsDescriptionAndCategoryFromComments()
  {
    SchemaMetadata metadata = RunGenerator();

    TableMetadata simple = metadata.Tables.First(t => t.Name == "simple_table");
    simple.Description.Should().Be("Simple test table with basic columns");
    simple.Category.Should().Be("test");

    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");
    audit.Description.Should().Be("Append-only audit log with foreign keys");
    audit.Category.Should().Be("audit");
  }

  // ─── Column extraction ──────────────────────────────────────────

  [Fact]
  public void Execute_ExtractsColumnMetadata()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata simple = metadata.Tables.First(t => t.Name == "simple_table");

    simple.Columns.Should().HaveCount(4);
    simple.Schema.Should().Be("test");

    ColumnMetadata id = simple.Columns.First(c => c.Name == "id");
    id.IsPrimaryKey.Should().BeTrue();
    id.Type.Should().Be("UNIQUEIDENTIFIER");
    id.Nullable.Should().BeFalse();

    ColumnMetadata name = simple.Columns.First(c => c.Name == "name");
    name.Type.Should().Be("VARCHAR(200)");
    name.Nullable.Should().BeFalse();

    ColumnMetadata value = simple.Columns.First(c => c.Name == "value");
    value.Type.Should().Be("INT");
    value.Nullable.Should().BeTrue();
  }

  [Fact]
  public void Execute_ExtractsDefaultValueExpression()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata simple = metadata.Tables.First(t => t.Name == "simple_table");

    ColumnMetadata createdAt = simple.Columns.First(c => c.Name == "created_at");
    createdAt.DefaultValue.Should().NotBeNullOrEmpty();
    createdAt.DefaultValue.Should().ContainEquivalentOf("SYSUTCDATETIME");
  }

  [Fact]
  public void Execute_ExtractsIdentityColumn()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    ColumnMetadata id = audit.Columns.First(c => c.Name == "id");
    id.IsIdentity.Should().BeTrue();
    id.Type.Should().Be("BIGINT");
  }

  [Fact]
  public void Execute_ExtractsGeneratedAlwaysColumns()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata temporal = metadata.Tables.First(t => t.Name == "temporal_table");

    ColumnMetadata validFrom = temporal.Columns.First(c => c.Name == "valid_from");
    validFrom.IsGeneratedAlways.Should().BeTrue();
    validFrom.GeneratedAlwaysType.Should().NotBeNullOrEmpty();

    ColumnMetadata validTo = temporal.Columns.First(c => c.Name == "valid_to");
    validTo.IsGeneratedAlways.Should().BeTrue();
  }

  // ─── Temporal versioning ────────────────────────────────────────

  [Fact]
  public void Execute_DetectsTemporalVersioning()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata temporal = metadata.Tables.First(t => t.Name == "temporal_table");

    temporal.HasTemporalVersioning.Should().BeTrue();
    temporal.HistoryTable.Should().Contain("temporal_table_history");
  }

  [Fact]
  public void Execute_TemporalWithoutActiveColumn_IsNotSoftDelete()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata temporal = metadata.Tables.First(t => t.Name == "temporal_table");

    temporal.HasTemporalVersioning.Should().BeTrue();
    temporal.HasActiveColumn.Should().BeFalse();
    temporal.HasSoftDelete.Should().BeFalse("temporal alone is not soft delete — needs active column too");
  }

  // ─── Soft delete pattern ────────────────────────────────────────

  [Fact]
  public void Execute_DetectsSoftDeletePattern()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata table = metadata.Tables.First(t => t.Name == "soft_delete_table");

    table.HasTemporalVersioning.Should().BeTrue();
    table.HasActiveColumn.Should().BeTrue();
    table.HasSoftDelete.Should().BeTrue();
    table.Triggers.HardDelete.Generate.Should().BeTrue();
    table.Triggers.HardDelete.Name.Should().Be("trg_soft_delete_table_hard_delete");
  }

  [Fact]
  public void Execute_WithSoftDeleteDisabled_SkipsSoftDeleteDetection()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Features.EnableSoftDelete = false;

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "soft_delete_table");

    table.HasActiveColumn.Should().BeTrue("active column is still detected");
    table.HasTemporalVersioning.Should().BeTrue("temporal is still detected");
    table.HasSoftDelete.Should().BeFalse("soft delete feature is disabled");
    table.Triggers.HardDelete.Generate.Should().BeFalse();
  }

  [Fact]
  public void Execute_WithTriggersDisabled_SoftDeleteWithoutTrigger()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Features.GenerateHardDeleteTriggers = false;

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "soft_delete_table");

    table.HasSoftDelete.Should().BeTrue("soft delete is still detected");
    table.Triggers.HardDelete.Generate.Should().BeFalse("trigger generation is disabled");
  }

  // ─── Polymorphic pattern ────────────────────────────────────────

  [Fact]
  public void Execute_DetectsPolymorphicPattern()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata table = metadata.Tables.First(t => t.Name == "polymorphic_table");

    table.IsPolymorphic.Should().BeTrue();
    table.PolymorphicOwner.Should().NotBeNull();
    table.PolymorphicOwner!.TypeColumn.Should().Be("owner_type");
    table.PolymorphicOwner.IdColumn.Should().Be("owner_id");
    table.PolymorphicOwner.AllowedTypes.Should().BeEquivalentTo(["individual", "organisation"]);
  }

  [Fact]
  public void Execute_WithPolymorphicDisabled_SkipsPolymorphicDetection()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Features.DetectPolymorphicPatterns = false;

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "polymorphic_table");

    table.IsPolymorphic.Should().BeFalse();
    table.PolymorphicOwner.Should().BeNull();
  }

  [Fact]
  public void Execute_PolymorphicTable_HasCheckConstraintInMetadata()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata table = metadata.Tables.First(t => t.Name == "polymorphic_table");

    table.Constraints.CheckConstraints.Should().ContainSingle(c =>
        c.Name == "ck_polymorphic_table_owner_type");

    CheckConstraint check = table.Constraints.CheckConstraints.First();
    check.Expression.Should().ContainEquivalentOf("owner_type");
    check.Expression.Should().ContainEquivalentOf("individual");
  }

  // ─── Append-only pattern ────────────────────────────────────────

  [Fact]
  public void Execute_DetectsAppendOnlyPattern()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    audit.IsAppendOnly.Should().BeTrue(
        "audit_log has created_at, no updated_by, and no temporal versioning");
    audit.HasSoftDelete.Should().BeFalse();
    audit.Triggers.HardDelete.Generate.Should().BeFalse();
  }

  [Fact]
  public void Execute_WithAppendOnlyDisabled_SkipsDetection()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Features.DetectAppendOnlyTables = false;

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    audit.IsAppendOnly.Should().BeFalse("append-only detection is disabled");
  }

  // ─── Foreign key constraints ────────────────────────────────────

  [Fact]
  public void Execute_ExtractsForeignKeyConstraints()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    audit.Constraints.ForeignKeys.Should().ContainSingle();

    ForeignKeyConstraint fk = audit.Constraints.ForeignKeys.First();
    fk.Name.Should().Be("fk_audit_log_entity");
    fk.Columns.Should().Equal(["entity_id"]);
    fk.ReferencedTable.Should().Be("simple_table");
    fk.ReferencedColumns.Should().Equal(["id"]);
  }

  [Fact]
  public void Execute_MarksForeignKeyOnColumn()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    ColumnMetadata entityId = audit.Columns.First(c => c.Name == "entity_id");
    entityId.ForeignKey.Should().NotBeNull();
    entityId.ForeignKey!.Table.Should().Be("simple_table");
    entityId.ForeignKey.Column.Should().Be("id");
  }

  // ─── Audit column convention ────────────────────────────────────

  [Fact]
  public void Execute_InjectsConventionalForeignKeysForAuditColumns()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata temporal = metadata.Tables.First(t => t.Name == "temporal_table");

    ColumnMetadata createdBy = temporal.Columns.First(c => c.Name == "created_by");
    createdBy.ForeignKey.Should().NotBeNull();
    createdBy.ForeignKey!.Table.Should().Be("individuals");
    createdBy.ForeignKey.Column.Should().Be("id");

    ColumnMetadata updatedBy = temporal.Columns.First(c => c.Name == "updated_by");
    updatedBy.ForeignKey.Should().NotBeNull();
    updatedBy.ForeignKey!.Table.Should().Be("individuals");
  }

  // ─── Primary key constraint metadata ────────────────────────────

  [Fact]
  public void Execute_ExtractsPrimaryKeyConstraint()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    audit.PrimaryKey.Should().Be("id");
    audit.Constraints.PrimaryKey.Should().NotBeNull();
    audit.Constraints.PrimaryKey!.Name.Should().Be("pk_audit_log");
    audit.Constraints.PrimaryKey.Columns.Should().Equal(["id"]);
    audit.Constraints.PrimaryKey.IsClustered.Should().BeTrue();
  }

  // ─── Check constraint on non-polymorphic column ────────────────

  [Fact]
  public void Execute_ExtractsCheckConstraintOnNonPolymorphicColumn()
  {
    SchemaMetadata metadata = RunGenerator();
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    audit.Constraints.CheckConstraints.Should().ContainSingle(c =>
        c.Name == "ck_audit_log_action");
  }

  // ─── Configurable column names ──────────────────────────────────

  [Fact]
  public void Execute_CustomActiveColumn_DetectsSoftDelete()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Columns.Active = "is_enabled";

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "custom_columns_table");

    table.HasActiveColumn.Should().BeTrue("is_enabled matches configured active column");
    table.HasTemporalVersioning.Should().BeTrue();
    table.HasSoftDelete.Should().BeTrue();
    table.Triggers.HardDelete.ActiveColumnName.Should().Be("is_enabled");
  }

  [Fact]
  public void Execute_CustomActiveColumn_DefaultActiveNotDetected()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Columns.Active = "is_enabled";

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "soft_delete_table");

    table.HasActiveColumn.Should().BeFalse(
        "standard 'active' column does not match configured 'is_enabled'");
    table.HasSoftDelete.Should().BeFalse();
  }

  [Fact]
  public void Execute_CustomAuditColumns_WiresForeignKeys()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Columns.CreatedBy = "author";
    config.Columns.UpdatedBy = "editor";
    config.Columns.AuditForeignKeyTable = "users";

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "custom_columns_table");

    ColumnMetadata author = table.Columns.First(c => c.Name == "author");
    author.ForeignKey.Should().NotBeNull();
    author.ForeignKey!.Table.Should().Be("users");

    ColumnMetadata editor = table.Columns.First(c => c.Name == "editor");
    editor.ForeignKey.Should().NotBeNull();
    editor.ForeignKey!.Table.Should().Be("users");
  }

  [Fact]
  public void Execute_CustomAuditColumns_DefaultColumnsNotWired()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Columns.CreatedBy = "author";
    config.Columns.UpdatedBy = "editor";

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata temporal = metadata.Tables.First(t => t.Name == "temporal_table");

    ColumnMetadata createdBy = temporal.Columns.First(c => c.Name == "created_by");
    createdBy.ForeignKey.Should().BeNull(
        "standard 'created_by' does not match configured 'author'");
  }

  [Fact]
  public void Execute_CustomPolymorphicPatterns_DetectsCustomColumns()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Columns.PolymorphicPatterns =
    [
        new PolymorphicPatternConfig { TypeColumn = "owner_type", IdColumn = "owner_id" }
    ];

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "polymorphic_table");

    table.IsPolymorphic.Should().BeTrue();
    table.PolymorphicOwner!.TypeColumn.Should().Be("owner_type");
  }

  [Fact]
  public void Execute_EmptyPolymorphicPatterns_SkipsDetection()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Columns.PolymorphicPatterns = [];

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata table = metadata.Tables.First(t => t.Name == "polymorphic_table");

    table.IsPolymorphic.Should().BeFalse(
        "no polymorphic patterns configured, so no columns match");
  }

  [Fact]
  public void Execute_CustomAppendOnlyColumns_DetectsPattern()
  {
    SchemaToolsConfig config = CreateTestConfig();
    // audit_log has created_at but not updated_by → append-only with defaults
    // Changing updatedBy to something else should still detect append-only
    // (no "editor" column in audit_log, so !hasUpdatedBy remains true)
    config.Columns.UpdatedBy = "editor";

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    audit.IsAppendOnly.Should().BeTrue(
        "audit_log has created_at and no 'editor' column, and no temporal versioning");
  }

  // ─── TableFiles (ITaskItem[]) ───────────────────────────────────

  [Fact]
  public void Execute_WithTableFiles_ProcessesSpecifiedFiles()
  {
    string outputFile = Path.Combine(_testOutputPath, $"{Guid.NewGuid()}.json");
    var task = new SchemaMetadataGenerator
    {
      TableFiles =
      [
          new MockTaskItem(Path.Combine(_testTablesPath, "simple_table.sql")),
          new MockTaskItem(Path.Combine(_testTablesPath, "temporal_table.sql"))
      ],
      OutputFile = outputFile,
      TestConfig = CreateTestConfig(),
      BuildEngine = new MockBuildEngine()
    };

    task.Execute().Should().BeTrue();

    string json = File.ReadAllText(outputFile);
    SchemaMetadata? metadata = JsonSerializer.Deserialize<SchemaMetadata>(json, JsonOptions);
    metadata.Should().NotBeNull();
    metadata!.Tables.Should().HaveCount(2);
    metadata.Tables.Select(t => t.Name).Should().BeEquivalentTo(["simple_table", "temporal_table"]);
  }

  [Fact]
  public void Execute_WithTableFiles_IgnoresTablesDirectory()
  {
    string outputFile = Path.Combine(_testOutputPath, $"{Guid.NewGuid()}.json");
    var task = new SchemaMetadataGenerator
    {
      TablesDirectory = _testTablesPath,
      TableFiles =
      [
          new MockTaskItem(Path.Combine(_testTablesPath, "simple_table.sql"))
      ],
      OutputFile = outputFile,
      TestConfig = CreateTestConfig(),
      BuildEngine = new MockBuildEngine()
    };

    task.Execute().Should().BeTrue();

    string json = File.ReadAllText(outputFile);
    SchemaMetadata? metadata = JsonSerializer.Deserialize<SchemaMetadata>(json, JsonOptions);
    metadata!.Tables.Should().HaveCount(1, "TableFiles takes precedence over TablesDirectory");
  }

  [Fact]
  public void Execute_WithNonexistentTableFiles_ReturnsFalse()
  {
    var engine = new MockBuildEngine();
    var task = new SchemaMetadataGenerator
    {
      TableFiles =
      [
          new MockTaskItem(Path.Combine(_testOutputPath, "does_not_exist.sql"))
      ],
      OutputFile = Path.Combine(_testOutputPath, "out.json"),
      TestConfig = CreateTestConfig(),
      BuildEngine = engine
    };

    task.Execute().Should().BeFalse();
    engine.Errors.Should().ContainMatch("*none of the paths exist*");
  }

  [Fact]
  public void Execute_WithNoTableFilesOrDirectory_ReturnsFalse()
  {
    var engine = new MockBuildEngine();
    var task = new SchemaMetadataGenerator
    {
      TablesDirectory = string.Empty,
      OutputFile = Path.Combine(_testOutputPath, "out.json"),
      TestConfig = CreateTestConfig(),
      BuildEngine = engine
    };

    task.Execute().Should().BeFalse();
    engine.Errors.Should().ContainMatch("*No TableFiles or TablesDirectory*");
  }

  // ─── Per-table overrides ────────────────────────────────────────

  [Fact]
  public void Execute_PerTableOverride_DisablesSoftDeleteForSpecificTable()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Overrides["soft_delete_table"] = new TableOverrideConfig
    {
      Features = new FeatureOverrideConfig { EnableSoftDelete = false }
    };

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata softDelete = metadata.Tables.First(t => t.Name == "soft_delete_table");

    softDelete.HasActiveColumn.Should().BeTrue("active column is still detected");
    softDelete.HasSoftDelete.Should().BeFalse("soft delete is disabled for this table");
    softDelete.Triggers.HardDelete.Generate.Should().BeFalse();
  }

  [Fact]
  public void Execute_PerTableOverride_OtherTablesUnaffected()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Overrides["soft_delete_table"] = new TableOverrideConfig
    {
      Features = new FeatureOverrideConfig { EnableSoftDelete = false }
    };

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata poly = metadata.Tables.First(t => t.Name == "polymorphic_table");

    poly.HasSoftDelete.Should().BeTrue("polymorphic_table has soft delete and is not overridden");
  }

  [Fact]
  public void Execute_CategoryOverride_DisablesAppendOnlyForCategory()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Overrides["category:audit"] = new TableOverrideConfig
    {
      Features = new FeatureOverrideConfig { DetectAppendOnlyTables = false }
    };

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata audit = metadata.Tables.First(t => t.Name == "audit_log");

    audit.IsAppendOnly.Should().BeFalse("append-only detection disabled for audit category");
  }

  [Fact]
  public void Execute_GlobOverride_DisablesPolymorphicForMatchingTables()
  {
    SchemaToolsConfig config = CreateTestConfig();
    config.Overrides["polymorphic_*"] = new TableOverrideConfig
    {
      Features = new FeatureOverrideConfig { DetectPolymorphicPatterns = false }
    };

    SchemaMetadata metadata = RunGenerator(config);
    TableMetadata poly = metadata.Tables.First(t => t.Name == "polymorphic_table");

    poly.IsPolymorphic.Should().BeFalse("polymorphic detection disabled via glob override");
  }

  public void Dispose()
  {
    if (Directory.Exists(_testOutputPath))
    {
      Directory.Delete(_testOutputPath, recursive: true);
    }
  }
}

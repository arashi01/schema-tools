using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class SqlTriggerGeneratorTests : IDisposable
{
  private readonly string _outputDir;
  private readonly string _customDir;

  public SqlTriggerGeneratorTests()
  {
    string root = Path.Combine(Path.GetTempPath(), "schema-tools-tests", Guid.NewGuid().ToString());
    _outputDir = Path.Combine(root, "generated");
    _customDir = Path.Combine(root, "custom");
    Directory.CreateDirectory(_outputDir);
    Directory.CreateDirectory(_customDir);
  }

  private static SchemaMetadata CreateMetadata(params TableMetadata[] tables) => new()
  {
    Database = "TestDB",
    DefaultSchema = "test",
    Tables = [.. tables]
  };

  private static TableMetadata SoftDeleteTable(string name) => new()
  {
    Name = name,
    Schema = "test",
    PrimaryKey = "id",
    HasSoftDelete = true,
    HasActiveColumn = true,
    HasTemporalVersioning = true,
    Triggers = new TriggerConfiguration
    {
      HardDelete = new HardDeleteTrigger
      {
        Generate = true,
        Name = $"trg_{name}_hard_delete"
      }
    }
  };

  private SqlTriggerGenerator CreateTask(SchemaMetadata metadata) => new()
  {
    MetadataFile = "unused",
    OutputDirectory = _outputDir,
    CustomTriggersDirectory = _customDir,
    TestMetadata = metadata,
    BuildEngine = new MockBuildEngine()
  };

  // ─── Core trigger generation ────────────────────────────────────

  [Fact]
  public void Execute_GeneratesTriggerFileForSoftDeleteTable()
  {
    SchemaMetadata metadata = CreateMetadata(SoftDeleteTable("users"));
    SqlTriggerGenerator task = CreateTask(metadata);

    task.Execute().Should().BeTrue();

    string file = Path.Combine(_outputDir, "trg_users_hard_delete.sql");
    File.Exists(file).Should().BeTrue();

    string sql = File.ReadAllText(file);
    sql.Should().Contain("[test].[trg_users_hard_delete]");
    sql.Should().Contain("ON [test].[users]");
    sql.Should().Contain("AFTER UPDATE");
    sql.Should().Contain("IF UPDATE(active)");
    sql.Should().Contain("WHERE i.active = 0 AND d.active = 1");
  }

  [Fact]
  public void Execute_GeneratesMultipleTriggers()
  {
    SchemaMetadata metadata = CreateMetadata(
        SoftDeleteTable("users"),
        SoftDeleteTable("accounts"));
    SqlTriggerGenerator task = CreateTask(metadata);

    task.Execute().Should().BeTrue();

    File.Exists(Path.Combine(_outputDir, "trg_users_hard_delete.sql")).Should().BeTrue();
    File.Exists(Path.Combine(_outputDir, "trg_accounts_hard_delete.sql")).Should().BeTrue();
  }

  // ─── Skip behaviour ─────────────────────────────────────────────

  [Fact]
  public void Execute_SkipsTablesWithoutTriggerEnabled()
  {
    var nontrigger = new TableMetadata
    {
      Name = "readonly_table",
      Schema = "test",
      Triggers = new TriggerConfiguration
      {
        HardDelete = new HardDeleteTrigger { Generate = false }
      }
    };
    SchemaMetadata metadata = CreateMetadata(nontrigger);
    SqlTriggerGenerator task = CreateTask(metadata);

    task.Execute().Should().BeTrue();
    Directory.GetFiles(_outputDir, "*.sql").Should().BeEmpty();
  }

  [Fact]
  public void Execute_SkipsWhenCustomTriggerExists()
  {
    // Place a custom trigger in the custom directory
    string customFile = Path.Combine(_customDir, "trg_users_hard_delete.sql");
    File.WriteAllText(customFile, "-- custom trigger");

    SchemaMetadata metadata = CreateMetadata(SoftDeleteTable("users"));
    SqlTriggerGenerator task = CreateTask(metadata);

    task.Execute().Should().BeTrue();

    // Should NOT have generated a file
    File.Exists(Path.Combine(_outputDir, "trg_users_hard_delete.sql")).Should().BeFalse();
  }

  [Fact]
  public void Execute_SkipsExistingUnlessForced()
  {
    string existingFile = Path.Combine(_outputDir, "trg_users_hard_delete.sql");
    File.WriteAllText(existingFile, "-- old version");

    SchemaMetadata metadata = CreateMetadata(SoftDeleteTable("users"));
    SqlTriggerGenerator task = CreateTask(metadata);
    task.Force = false;

    task.Execute().Should().BeTrue();

    // File should remain unchanged
    File.ReadAllText(existingFile).Should().Be("-- old version");
  }

  [Fact]
  public void Execute_OverwritesExistingWhenForced()
  {
    string existingFile = Path.Combine(_outputDir, "trg_users_hard_delete.sql");
    File.WriteAllText(existingFile, "-- old version");

    SchemaMetadata metadata = CreateMetadata(SoftDeleteTable("users"));
    SqlTriggerGenerator task = CreateTask(metadata);
    task.Force = true;

    task.Execute().Should().BeTrue();

    string content = File.ReadAllText(existingFile);
    content.Should().Contain("AFTER UPDATE", "file should be regenerated");
    content.Should().NotContain("old version");
  }

  // ─── Custom column names in triggers ──────────────────────────────

  [Fact]
  public void Execute_UsesCustomActiveColumnInTrigger()
  {
    var table = new TableMetadata
    {
      Name = "custom_table",
      Schema = "dbo",
      PrimaryKey = "id",
      HasSoftDelete = true,
      HasActiveColumn = true,
      HasTemporalVersioning = true,
      Triggers = new TriggerConfiguration
      {
        HardDelete = new HardDeleteTrigger
        {
          Generate = true,
          Name = "trg_custom_table_hard_delete",
          ActiveColumnName = "is_enabled"
        }
      }
    };
    SchemaMetadata metadata = CreateMetadata(table);
    SqlTriggerGenerator task = CreateTask(metadata);

    task.Execute().Should().BeTrue();

    string sql = File.ReadAllText(Path.Combine(_outputDir, "trg_custom_table_hard_delete.sql"));
    sql.Should().Contain("IF UPDATE(is_enabled)");
    sql.Should().Contain("WHERE i.is_enabled = 0 AND d.is_enabled = 1");
    sql.Should().NotContain("IF UPDATE(active)");
  }

  [Fact]
  public void Execute_UsesTablePrimaryKeyInTrigger()
  {
    var table = new TableMetadata
    {
      Name = "pk_table",
      Schema = "dbo",
      PrimaryKey = "guid_id",
      HasSoftDelete = true,
      HasActiveColumn = true,
      HasTemporalVersioning = true,
      Triggers = new TriggerConfiguration
      {
        HardDelete = new HardDeleteTrigger
        {
          Generate = true,
          Name = "trg_pk_table_hard_delete"
        }
      }
    };
    SchemaMetadata metadata = CreateMetadata(table);
    SqlTriggerGenerator task = CreateTask(metadata);

    task.Execute().Should().BeTrue();

    string sql = File.ReadAllText(Path.Combine(_outputDir, "trg_pk_table_hard_delete.sql"));
    sql.Should().Contain("WHERE guid_id IN");
    sql.Should().Contain("SELECT i.guid_id");
    sql.Should().Contain("ON i.guid_id = d.guid_id");
    sql.Should().NotContain("WHERE id IN");
  }

  // ─── Error handling ─────────────────────────────────────────────

  [Fact]
  public void Execute_WithNoTablesNeedingTriggers_ReturnsTrue()
  {
    var plain = new TableMetadata
    {
      Name = "plain",
      Schema = "test",
      Triggers = new TriggerConfiguration
      {
        HardDelete = new HardDeleteTrigger { Generate = false }
      }
    };
    SchemaMetadata metadata = CreateMetadata(plain);

    SqlTriggerGenerator task = CreateTask(metadata);
    task.Execute().Should().BeTrue();
  }

  public void Dispose()
  {
    string root = Path.GetDirectoryName(_outputDir)!;
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }
}

using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class SqlProcedureGeneratorTests : IDisposable
{
  private readonly string _tempDir;
  private readonly MockBuildEngine _buildEngine;

  public SqlProcedureGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"procedure-gen-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
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

  private SqlProcedureGenerator CreateGenerator(SourceAnalysisResult analysis) => new()
  {
    AnalysisFile = "unused", // Using TestAnalysis injection
    OutputDirectory = _tempDir,
    ProcedureSchema = "dbo",
    PurgeProcedureName = "usp_purge_soft_deleted",
    DefaultGracePeriodDays = 90,
    Force = true,
    TestAnalysis = analysis,
    BuildEngine = _buildEngine
  };

  // --- No soft-delete tables ------------------------------------------------

  [Fact]
  public void Execute_NoSoftDeleteTables_SkipsProcedureGeneration()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis { Name = "regular_table", HasSoftDelete = false }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();
    Directory.GetFiles(_tempDir, "*.sql").Should().BeEmpty();
    _buildEngine.Messages.Should().Contain(m => m.Contains("no purge procedure needed"));
  }

  // --- Single soft-delete table ---------------------------------------------

  [Fact]
  public void Execute_SingleSoftDeleteTable_GeneratesProcedure()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[users_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();

    string content = File.ReadAllText(files[0]);
    content.Should().Contain("CREATE PROCEDURE [dbo].[usp_purge_soft_deleted]");
    content.Should().NotContain("CREATE OR ALTER PROCEDURE");
    content.Should().Contain("@grace_period_days INT = 90");
    content.Should().Contain("[dbo].[users]");
    content.Should().Contain("[dbo].[users_history]");
  }

  // --- Multiple tables with FK order ----------------------------------------

  [Fact]
  public void Execute_MultipleTables_GeneratesTopologicalOrder()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "orders",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[orders_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"],
          ChildTables = ["order_items"]  // Has children - processed last
        },
        new TableAnalysis
        {
          Name = "order_items",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[order_items_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"],
          IsLeafTable = true,
          ChildTables = []  // Leaf - processed first
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));

    // order_items should appear before orders in the procedure
    int itemsIndex = content.IndexOf("[dbo].[order_items]");
    int ordersIndex = content.IndexOf("[dbo].[orders]");
    itemsIndex.Should().BeLessThan(ordersIndex,
      "child tables should be deleted before parent tables");
  }

  // --- Circular dependency handling -----------------------------------------

  [Fact]
  public void Execute_CircularDependency_WarnsAndContinues()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "table_a",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HasTemporalVersioning = true,
          HistoryTable = "[dbo].[table_a_history]",
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"],
          ChildTables = ["table_b"]
        },
        new TableAnalysis
        {
          Name = "table_b",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HasTemporalVersioning = true,
          HistoryTable = "[dbo].[table_b_history]",
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"],
          ChildTables = ["table_a"]  // Circular back to table_a
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();
    _buildEngine.Warnings.Should().Contain(w => w.Contains("Circular dependency"));
  }

  // --- Tables without history table -----------------------------------------

  [Fact]
  public void Execute_NoHistoryTable_GeneratesFallbackWithWarning()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "legacy_table",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HasTemporalVersioning = false,
          HistoryTable = null,  // No history table
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().Contain("No history table detected");
    content.Should().Contain("Approximation only");
  }

  // --- Custom active column name --------------------------------------------

  [Fact]
  public void Execute_CustomActiveColumn_UsesConfiguredName()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "custom_table",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "is_active",  // Custom column name
          HistoryTable = "[dbo].[custom_table_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().Contain("is_active = 0");
    content.Should().Contain("is_active = 1");
  }

  // --- Composite primary key ------------------------------------------------

  [Fact]
  public void Execute_CompositePrimaryKey_GeneratesCorrectJoinConditions()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "composite_pk",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[composite_pk_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["tenant_id", "entity_id"]  // Composite PK
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().Contain("t.tenant_id = h.tenant_id AND t.entity_id = h.entity_id");
  }

  // --- File already exists without Force ------------------------------------

  [Fact]
  public void Execute_FileExistsWithoutForce_SkipsRegeneration()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "test_table",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[test_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"]
        }
      ]
    };

    // Pre-create the file
    string existingContent = "-- Existing content";
    File.WriteAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"), existingContent);

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.Force = false;
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().Be(existingContent);
    _buildEngine.Messages.Should().Contain(m => m.Contains("Already exists"));
  }

  // --- Custom procedure name and schema -------------------------------------

  [Fact]
  public void Execute_CustomSchemaAndName_UsesConfiguredValues()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "test",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[test_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.ProcedureSchema = "admin";
    generator.PurgeProcedureName = "sp_cleanup_deleted_records";
    generator.Execute();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle().Which.Should().EndWith("sp_cleanup_deleted_records.sql");

    string content = File.ReadAllText(files[0]);
    content.Should().Contain("[admin].[sp_cleanup_deleted_records]");
  }

  // --- Custom grace period --------------------------------------------------

  [Fact]
  public void Execute_CustomGracePeriod_UsesConfiguredValue()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "test",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[test_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.DefaultGracePeriodDays = 30;
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().Contain("@grace_period_days INT = 30");
  }

  // --- Procedure structure validation ---------------------------------------

  [Fact]
  public void Execute_GeneratedProcedure_ContainsRequiredStructure()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "test",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[test_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));

    // Required parameters
    content.Should().Contain("@grace_period_days INT");
    content.Should().Contain("@batch_size INT");
    content.Should().Contain("@dry_run BIT");

    // Transaction handling
    content.Should().Contain("BEGIN TRANSACTION");
    content.Should().Contain("COMMIT TRANSACTION");
    content.Should().Contain("ROLLBACK TRANSACTION");

    // Error handling
    content.Should().Contain("BEGIN TRY");
    content.Should().Contain("BEGIN CATCH");
    content.Should().Contain("RAISERROR");

    // Reporting
    content.Should().Contain("#purge_results");
    content.Should().Contain("Total Records Deleted");

    // Dry run mode
    content.Should().Contain("DRY RUN MODE");
  }

  // --- Batch size for non-root tables ---------------------------------------

  [Fact]
  public void Execute_NonRootTables_UsesBatchSize()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "parent",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[parent_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"],
          ChildTables = ["child"]
        },
        new TableAnalysis
        {
          Name = "child",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[child_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["id"],
          IsLeafTable = true
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));

    // Second table (parent) should have TOP (@batch_size), first (child) should not
    content.Should().Contain("DELETE TOP (@batch_size)");
  }

  // --- Non-id primary key ---------------------------------------------------

  [Fact]
  public void Execute_NonIdPrimaryKey_GeneratesCorrectColumnReferences()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "countries",
          Schema = "directory",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[directory].[hist_countries]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["iso_alpha2"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().Contain("t.iso_alpha2 = h.iso_alpha2");
    content.Should().Contain("COUNT(DISTINCT t.iso_alpha2)");
    content.Should().NotContain("t.id");
  }

  // --- Composite PK uses COUNT(*) -------------------------------------------

  [Fact]
  public void Execute_CompositePrimaryKey_UsesCountStar()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "dialling_codes",
          Schema = "directory",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[directory].[hist_dialling_codes]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = ["country_code", "dialling_code"]
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    generator.Execute();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().Contain("t.country_code = h.country_code AND t.dialling_code = h.dialling_code");
    content.Should().Contain("COUNT(*)");
    content.Should().NotContain("COUNT(DISTINCT");
  }

  // --- Missing PK columns skips table with warning --------------------------

  [Fact]
  public void Execute_MissingPrimaryKey_SkipsTableWithWarning()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis
        {
          Name = "no_pk_table",
          Schema = "dbo",
          HasSoftDelete = true,
          ActiveColumnName = "record_active",
          HistoryTable = "[dbo].[no_pk_history]",
          HasTemporalVersioning = true,
          ValidToColumn = "record_valid_until",
          PrimaryKeyColumns = []  // No PK columns
        }
      ]
    };

    SqlProcedureGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string content = File.ReadAllText(Path.Combine(_tempDir, "usp_purge_soft_deleted.sql"));
    content.Should().NotContain("[dbo].[no_pk_table]");
    _buildEngine.Warnings.Should().Contain(w => w.Contains("no_pk_table") && w.Contains("no primary key"));
  }
}

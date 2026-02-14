using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

/// <summary>
/// Tests for SqlTriggerGenerator - CASCADE soft-delete trigger generation.
/// 
/// The new design generates cascade triggers for PARENT tables (tables with FK children)
/// that propagate soft-delete (active=0) to child tables. No hard-delete triggers are
/// generated - hard deletion is deferred and handled by SqlProcedureGenerator.
/// </summary>
public class SqlTriggerGeneratorTests : IDisposable
{
  private readonly string _outputDir;

  public SqlTriggerGeneratorTests()
  {
    string root = Path.Combine(Path.GetTempPath(), "schema-tools-tests", Guid.NewGuid().ToString());
    _outputDir = Path.Combine(root, "generated");
    Directory.CreateDirectory(_outputDir);
  }

  // --- Test Data Builders --------------------------------------------------

  private static SourceAnalysisResult CreateAnalysis(params TableAnalysis[] tables) => new()
  {
    Version = "1.0.0",
    AnalysedAt = DateTime.UtcNow,
    DefaultSchema = "test",
    Tables = [.. tables]
  };

  /// <summary>
  /// Creates a parent table with soft-delete that has children referencing it.
  /// Parent tables are candidates for cascade soft-delete triggers.
  /// </summary>
  private static TableAnalysis ParentTable(string name, params string[] childTableNames) => new()
  {
    Name = name,
    Schema = "test",
    HasSoftDelete = true,
    HasActiveColumn = true,
    HasTemporalVersioning = true,
    ActiveColumnName = "active",
    PrimaryKeyColumns = ["id"],
    ChildTables = [.. childTableNames],
    IsLeafTable = false
  };

  /// <summary>
  /// Creates a leaf table (no children) with soft-delete.
  /// Leaf tables do NOT get cascade triggers since there's nothing to cascade to.
  /// </summary>
  private static TableAnalysis LeafTable(string name, string parentFkColumn = "parent_id", string parentTable = "parent") => new()
  {
    Name = name,
    Schema = "test",
    HasSoftDelete = true,
    HasActiveColumn = true,
    HasTemporalVersioning = true,
    ActiveColumnName = "active",
    PrimaryKeyColumns = ["id"],
    ChildTables = [],
    IsLeafTable = true,
    ForeignKeyReferences =
    [
      new ForeignKeyRef
      {
        ReferencedTable = parentTable,
        ReferencedSchema = "test",
        Columns = [parentFkColumn],
        ReferencedColumns = ["id"]
      }
    ]
  };

  private SqlTriggerGenerator CreateTask(SourceAnalysisResult analysis) => new()
  {
    AnalysisFile = "unused",
    OutputDirectory = _outputDir,
    TestAnalysis = analysis,
    BuildEngine = new MockBuildEngine()
  };

  // --- Core Cascade Trigger Generation -------------------------------------

  [Fact]
  public void Execute_GeneratesCascadeTriggerForParentTable()
  {
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTable("users", "orders"),
      LeafTable("orders", "user_id", "users")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    string file = Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql");
    File.Exists(file).Should().BeTrue();

    string sql = File.ReadAllText(file);
    sql.Should().Contain("[test].[trg_users_cascade_soft_delete]");
    sql.Should().Contain("ON [test].[users]");
    sql.Should().Contain("AFTER UPDATE");
    sql.Should().Contain("IF NOT UPDATE(active)");  // Guard clause pattern
    // Cascade to children - sets active=0 on child tables
    sql.Should().Contain("UPDATE [test].[orders]");
    sql.Should().Contain("active = 0");
  }

  [Fact]
  public void Execute_GeneratesMultipleCascadeTriggers()
  {
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTable("users", "orders"),
      ParentTable("products", "order_items"),
      LeafTable("orders", "user_id", "users"),
      LeafTable("order_items", "product_id", "products")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeTrue();
    File.Exists(Path.Combine(_outputDir, "trg_products_cascade_soft_delete.sql")).Should().BeTrue();
  }

  [Fact]
  public void Execute_CascadesToMultipleChildren()
  {
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTable("users", "orders", "preferences", "sessions"),
      LeafTable("orders", "user_id", "users"),
      LeafTable("preferences", "user_id", "users"),
      LeafTable("sessions", "user_id", "users")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    string sql = File.ReadAllText(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql"));
    sql.Should().Contain("UPDATE [test].[orders]");
    sql.Should().Contain("UPDATE [test].[preferences]");
    sql.Should().Contain("UPDATE [test].[sessions]");
  }

  // --- Skip Behaviour ------------------------------------------------------

  [Fact]
  public void Execute_SkipsLeafTables()
  {
    // Leaf tables have no children - nothing to cascade
    SourceAnalysisResult analysis = CreateAnalysis(
      LeafTable("orders"),
      LeafTable("products")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();
    Directory.GetFiles(_outputDir, "*.sql").Should().BeEmpty();
  }

  [Fact]
  public void Execute_SkipsTablesWithoutSoftDelete()
  {
    var nonSoftDeleteParent = new TableAnalysis
    {
      Name = "audit_log",
      Schema = "test",
      HasSoftDelete = false,
      ChildTables = ["audit_entries"]
    };
    SourceAnalysisResult analysis = CreateAnalysis(nonSoftDeleteParent);
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();
    Directory.GetFiles(_outputDir, "*.sql").Should().BeEmpty();
  }

  [Fact]
  public void Execute_SkipsWhenExplicitTriggerExists()
  {
    // User has defined the trigger explicitly somewhere in their project
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTable("users", "orders"),
      LeafTable("orders", "user_id", "users")
    );

    // Add an explicit trigger (not in _generated directory)
    analysis.ExistingTriggers.Add(new ExistingTrigger
    {
      Name = "trg_users_cascade_soft_delete",
      Schema = "test",
      TargetTable = "users",
      SourceFile = "Schema/Triggers/my_custom_triggers.sql",
      IsGenerated = false  // Explicit = not in _generated directory
    });

    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // Should NOT have generated a file - explicit wins
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeFalse();
  }

  [Fact]
  public void Execute_SkipsExistingUnlessForced()
  {
    string existingFile = Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql");
    File.WriteAllText(existingFile, "-- old version");

    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTable("users", "orders"),
      LeafTable("orders")
    );
    SqlTriggerGenerator task = CreateTask(analysis);
    task.Force = false;

    task.Execute().Should().BeTrue();

    // File should remain unchanged
    File.ReadAllText(existingFile).Should().Be("-- old version");
  }

  [Fact]
  public void Execute_OverwritesExistingWhenForced()
  {
    string existingFile = Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql");
    File.WriteAllText(existingFile, "-- old version");

    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTable("users", "orders"),
      LeafTable("orders")
    );
    SqlTriggerGenerator task = CreateTask(analysis);
    task.Force = true;

    task.Execute().Should().BeTrue();

    string content = File.ReadAllText(existingFile);
    content.Should().Contain("AFTER UPDATE", "file should be regenerated");
    content.Should().NotContain("old version");
  }

  // --- Custom Active Column ------------------------------------------------

  [Fact]
  public void Execute_UsesCustomActiveColumnInTrigger()
  {
    var parent = new TableAnalysis
    {
      Name = "custom_table",
      Schema = "dbo",
      HasSoftDelete = true,
      HasActiveColumn = true,
      HasTemporalVersioning = true,
      ActiveColumnName = "is_enabled",
      PrimaryKeyColumns = ["id"],
      ChildTables = ["child_table"]
    };
    var child = new TableAnalysis
    {
      Name = "child_table",
      Schema = "dbo",
      HasSoftDelete = true,
      ActiveColumnName = "is_enabled",
      PrimaryKeyColumns = ["id"],
      ChildTables = [],
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "custom_table", Columns = ["parent_id"], ReferencedColumns = ["id"] }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    string sql = File.ReadAllText(Path.Combine(_outputDir, "trg_custom_table_cascade_soft_delete.sql"));
    sql.Should().Contain("IF NOT UPDATE(is_enabled)");
    sql.Should().Contain("is_enabled = 0");
    sql.Should().NotContain("IF NOT UPDATE(active)");
  }

  // --- Error Handling ------------------------------------------------------

  [Fact]
  public void Execute_WithNoParentTables_ReturnsTrue()
  {
    // Only leaf tables - no triggers needed
    SourceAnalysisResult analysis = CreateAnalysis(
      LeafTable("standalone1"),
      LeafTable("standalone2")
    );

    SqlTriggerGenerator task = CreateTask(analysis);
    task.Execute().Should().BeTrue();
  }

  [Fact]
  public void Execute_WithEmptyAnalysis_ReturnsTrue()
  {
    SourceAnalysisResult analysis = CreateAnalysis();

    SqlTriggerGenerator task = CreateTask(analysis);
    task.Execute().Should().BeTrue();
  }

  // --- Multi-Column PK Support -------------------------------------------

  [Fact]
  public void Execute_HandlesCompositePrimaryKey()
  {
    var parent = new TableAnalysis
    {
      Name = "tenant_users",
      Schema = "test",
      HasSoftDelete = true,
      HasActiveColumn = true,
      ActiveColumnName = "active",
      PrimaryKeyColumns = ["tenant_id", "user_id"],  // Composite PK
      ChildTables = ["sessions"]
    };
    var child = new TableAnalysis
    {
      Name = "sessions",
      Schema = "test",
      HasSoftDelete = true,
      ActiveColumnName = "active",
      PrimaryKeyColumns = ["session_id"],
      ChildTables = [],
      ForeignKeyReferences =
      [
        new ForeignKeyRef
        {
          ReferencedTable = "tenant_users",
          Columns = ["tenant_id", "user_id"],
          ReferencedColumns = ["tenant_id", "user_id"]
        }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    string sql = File.ReadAllText(Path.Combine(_outputDir, "trg_tenant_users_cascade_soft_delete.sql"));
    // Should have composite JOIN condition for inserted/deleted comparison
    sql.Should().Contain("i.tenant_id = d.tenant_id");
    sql.Should().Contain("i.user_id = d.user_id");
    // Should use EXISTS for multi-column FK cascade
    sql.Should().Contain("EXISTS");
  }

  // --- Reactivation Guard Triggers ---------------------------------------

  [Fact]
  public void Execute_GeneratesReactivationGuardForChildTable()
  {
    var parent = new TableAnalysis
    {
      Name = "users",
      Schema = "test",
      HasSoftDelete = true,
      HasActiveColumn = true,
      ActiveColumnName = "active",
      PrimaryKeyColumns = ["id"],
      ChildTables = ["orders"]
    };
    var child = new TableAnalysis
    {
      Name = "orders",
      Schema = "test",
      HasSoftDelete = true,
      ActiveColumnName = "active",
      PrimaryKeyColumns = ["id"],
      ChildTables = [],
      ForeignKeyReferences =
      [
        new ForeignKeyRef
        {
          ReferencedTable = "users",
          Columns = ["user_id"],
          ReferencedColumns = ["id"]
        }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // Should generate guard trigger for child
    string guardFile = Path.Combine(_outputDir, "trg_orders_reactivation_guard.sql");
    File.Exists(guardFile).Should().BeTrue();

    string sql = File.ReadAllText(guardFile);
    sql.Should().Contain("[test].[trg_orders_reactivation_guard]");
    sql.Should().Contain("ON [test].[orders]");
    sql.Should().Contain("AFTER UPDATE");
    // Check for reactivation (0 -> 1)
    sql.Should().Contain("i.active = 1 AND d.active = 0");
    // Check if parent is inactive
    sql.Should().Contain("p.active = 0");
    // Should have RAISERROR and ROLLBACK
    sql.Should().Contain("RAISERROR");
    sql.Should().Contain("ROLLBACK TRANSACTION");
  }

  [Fact]
  public void Execute_GeneratesGuardForMultipleParents()
  {
    var parent1 = new TableAnalysis
    {
      Name = "users",
      Schema = "test",
      HasSoftDelete = true,
      ActiveColumnName = "active",
      PrimaryKeyColumns = ["id"],
      ChildTables = ["orders"]
    };
    var parent2 = new TableAnalysis
    {
      Name = "products",
      Schema = "test",
      HasSoftDelete = true,
      ActiveColumnName = "active",
      PrimaryKeyColumns = ["id"],
      ChildTables = ["orders"]
    };
    var child = new TableAnalysis
    {
      Name = "orders",
      Schema = "test",
      HasSoftDelete = true,
      ActiveColumnName = "active",
      PrimaryKeyColumns = ["id"],
      ChildTables = [],
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "users", Columns = ["user_id"], ReferencedColumns = ["id"] },
        new ForeignKeyRef { ReferencedTable = "products", Columns = ["product_id"], ReferencedColumns = ["id"] }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent1, parent2, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    string sql = File.ReadAllText(Path.Combine(_outputDir, "trg_orders_reactivation_guard.sql"));
    // Should check both parents
    sql.Should().Contain("[test].[users]");
    sql.Should().Contain("[test].[products]");
  }

  [Fact]
  public void Execute_SkipsGuardForExplicitDefinition()
  {
    var parent = new TableAnalysis
    {
      Name = "users",
      Schema = "test",
      HasSoftDelete = true,
      PrimaryKeyColumns = ["id"],
      ChildTables = ["orders"]
    };
    var child = new TableAnalysis
    {
      Name = "orders",
      Schema = "test",
      HasSoftDelete = true,
      PrimaryKeyColumns = ["id"],
      ChildTables = [],
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "users", Columns = ["user_id"], ReferencedColumns = ["id"] }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    analysis.ExistingTriggers.Add(new ExistingTrigger
    {
      Name = "trg_orders_reactivation_guard",
      Schema = "test",
      TargetTable = "orders",
      SourceFile = "Schema/Triggers/custom_guards.sql",
      IsGenerated = false
    });

    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // Guard should NOT be generated - explicit wins
    File.Exists(Path.Combine(_outputDir, "trg_orders_reactivation_guard.sql")).Should().BeFalse();
  }

  [Fact]
  public void Execute_NoGuardForChildWithNonSoftDeleteParent()
  {
    var nonSoftDeleteParent = new TableAnalysis
    {
      Name = "audit_log",
      Schema = "test",
      HasSoftDelete = false,  // No soft-delete
      PrimaryKeyColumns = ["id"],
      ChildTables = ["audit_entries"]
    };
    var child = new TableAnalysis
    {
      Name = "audit_entries",
      Schema = "test",
      HasSoftDelete = true,
      PrimaryKeyColumns = ["id"],
      ChildTables = [],
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "audit_log", Columns = ["log_id"], ReferencedColumns = ["id"] }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(nonSoftDeleteParent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // No guard needed - parent doesn't have soft-delete
    File.Exists(Path.Combine(_outputDir, "trg_audit_entries_reactivation_guard.sql")).Should().BeFalse();
  }

  // --- SoftDeleteMode Tests ------------------------------------------------

  /// <summary>
  /// Creates a parent table with a specific SoftDeleteMode.
  /// </summary>
  private static TableAnalysis ParentTableWithMode(string name, SoftDeleteMode mode, params string[] childTableNames) => new()
  {
    Name = name,
    Schema = "test",
    HasSoftDelete = true,
    HasActiveColumn = true,
    HasTemporalVersioning = true,
    ActiveColumnName = "active",
    PrimaryKeyColumns = ["id"],
    ChildTables = [.. childTableNames],
    SoftDeleteMode = mode,
    IsLeafTable = false
  };

  [Fact]
  public void Execute_IgnoreMode_SkipsParentTable()
  {
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTableWithMode("users", SoftDeleteMode.Ignore, "orders"),
      LeafTable("orders", "user_id", "users")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // No cascade trigger for Ignore mode
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeFalse();
    File.Exists(Path.Combine(_outputDir, "trg_users_restrict_soft_delete.sql")).Should().BeFalse();
  }

  [Fact]
  public void Execute_RestrictMode_GeneratesRestrictTrigger()
  {
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTableWithMode("users", SoftDeleteMode.Restrict, "orders"),
      LeafTable("orders", "user_id", "users")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // Restrict trigger generated instead of cascade
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeFalse();
    string file = Path.Combine(_outputDir, "trg_users_restrict_soft_delete.sql");
    File.Exists(file).Should().BeTrue();

    string sql = File.ReadAllText(file);
    sql.Should().Contain("[test].[trg_users_restrict_soft_delete]");
    sql.Should().Contain("ON [test].[users]");
    sql.Should().Contain("AFTER UPDATE");
    sql.Should().Contain("RAISERROR");
    sql.Should().Contain("ROLLBACK TRANSACTION");
    sql.Should().Contain("Active children exist");
  }

  [Fact]
  public void Execute_RestrictMode_ChecksAllChildTables()
  {
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTableWithMode("users", SoftDeleteMode.Restrict, "orders", "preferences"),
      LeafTable("orders", "user_id", "users"),
      LeafTable("preferences", "user_id", "users")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    string sql = File.ReadAllText(Path.Combine(_outputDir, "trg_users_restrict_soft_delete.sql"));
    sql.Should().Contain("[test].[orders]");
    sql.Should().Contain("[test].[preferences]");
  }

  [Fact]
  public void Execute_CascadeMode_GeneratesCascadeTrigger()
  {
    // Explicit Cascade mode (same as default)
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTableWithMode("users", SoftDeleteMode.Cascade, "orders"),
      LeafTable("orders", "user_id", "users")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // Cascade trigger generated
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeTrue();
    File.Exists(Path.Combine(_outputDir, "trg_users_restrict_soft_delete.sql")).Should().BeFalse();
  }

  [Fact]
  public void Execute_MixedModes_GeneratesCorrectTriggers()
  {
    SourceAnalysisResult analysis = CreateAnalysis(
      ParentTableWithMode("users", SoftDeleteMode.Cascade, "orders"),
      ParentTableWithMode("products", SoftDeleteMode.Restrict, "order_items"),
      ParentTableWithMode("categories", SoftDeleteMode.Ignore, "products"),
      LeafTable("orders", "user_id", "users"),
      LeafTable("order_items", "product_id", "products")
    );
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // users = Cascade
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeTrue();
    // products = Restrict
    File.Exists(Path.Combine(_outputDir, "trg_products_restrict_soft_delete.sql")).Should().BeTrue();
    // categories = Ignore
    File.Exists(Path.Combine(_outputDir, "trg_categories_cascade_soft_delete.sql")).Should().BeFalse();
    File.Exists(Path.Combine(_outputDir, "trg_categories_restrict_soft_delete.sql")).Should().BeFalse();
  }

  [Fact]
  public void Execute_IgnoreMode_SkipsReactivationGuard()
  {
    // Child table with Ignore mode should not get reactivation guard
    TableAnalysis parent = ParentTable("users", "orders");
    TableAnalysis child = new()
    {
      Name = "orders",
      Schema = "test",
      HasSoftDelete = true,
      PrimaryKeyColumns = ["id"],
      ChildTables = [],
      SoftDeleteMode = SoftDeleteMode.Ignore,
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "users", Columns = ["user_id"], ReferencedColumns = ["id"] }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    task.Execute().Should().BeTrue();

    // Parent still gets cascade trigger
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeTrue();
    // Child with Ignore mode does not get reactivation guard
    File.Exists(Path.Combine(_outputDir, "trg_orders_reactivation_guard.sql")).Should().BeFalse();
  }

  // --- Reactivation Cascade Trigger Tests ----------------------------------

  [Fact]
  public void Execute_GeneratesReactivationCascadeTrigger_WhenEnabled()
  {
    // Arrange: Parent table with ReactivationCascade enabled
    TableAnalysis parent = new()
    {
      Name = "users",
      Schema = "test",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      ActiveColumnName = "active",
      ValidToColumn = "valid_to",
      PrimaryKeyColumns = ["id"],
      ChildTables = ["orders"],
      ReactivationCascade = true,
      SoftDeleteMode = SoftDeleteMode.Cascade
    };
    TableAnalysis child = LeafTable("orders", "user_id", "users");
    child.ValidToColumn = "valid_to";
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    // Act
    task.Execute().Should().BeTrue();

    // Assert: reactivation cascade trigger generated
    string file = Path.Combine(_outputDir, "trg_users_cascade_reactivation.sql");
    File.Exists(file).Should().BeTrue();
    string content = File.ReadAllText(file);
    content.Should().Contain("trg_users_cascade_reactivation");
    content.Should().Contain("AFTER UPDATE");
    // Verify timestamp matching logic is present
    content.Should().Contain("DATEDIFF(SECOND");
    content.Should().Contain("valid_to");
  }

  [Fact]
  public void Execute_DoesNotGenerateReactivationCascade_WhenDisabled()
  {
    // Arrange: Parent table without ReactivationCascade
    TableAnalysis parent = ParentTable("users", "orders");
    parent.ReactivationCascade = false;
    TableAnalysis child = LeafTable("orders", "user_id", "users");
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    // Act
    task.Execute().Should().BeTrue();

    // Assert: reactivation cascade trigger NOT generated
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_reactivation.sql")).Should().BeFalse();
    // But cascade soft-delete still generated
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_soft_delete.sql")).Should().BeTrue();
  }

  [Fact]
  public void Execute_ReactivationCascade_IncludesTimestampMatchingLogic()
  {
    // Arrange
    TableAnalysis parent = new()
    {
      Name = "accounts",
      Schema = "dbo",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      ActiveColumnName = "active",
      ValidToColumn = "valid_to",
      PrimaryKeyColumns = ["id"],
      ChildTables = ["profiles", "settings"],
      ReactivationCascade = true
    };
    TableAnalysis profile = new()
    {
      Name = "profiles",
      Schema = "dbo",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      ActiveColumnName = "active",
      ValidToColumn = "valid_to",
      PrimaryKeyColumns = ["id"],
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "accounts", Columns = ["account_id"], ReferencedColumns = ["id"] }
      ]
    };
    TableAnalysis settings = new()
    {
      Name = "settings",
      Schema = "dbo",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      ActiveColumnName = "active",
      ValidToColumn = "valid_to",
      PrimaryKeyColumns = ["id"],
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "accounts", Columns = ["account_id"], ReferencedColumns = ["id"] }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, profile, settings);
    SqlTriggerGenerator task = CreateTask(analysis);

    // Act
    task.Execute().Should().BeTrue();

    // Assert
    string file = Path.Combine(_outputDir, "trg_accounts_cascade_reactivation.sql");
    File.Exists(file).Should().BeTrue();
    string content = File.ReadAllText(file);

    // Should reference both child tables
    content.Should().Contain("[dbo].[profiles]");
    content.Should().Contain("[dbo].[settings]");
    // Should check for reactivation (active: 0 -> 1)
    content.Should().Contain("i.active = 1 AND d.active = 0");
    // Should have 2-second tolerance for timestamp matching
    content.Should().Contain("<= 2");
    // Should set children to active
    content.Should().Contain("c.active = 1");
  }

  [Fact]
  public void Execute_ReactivationCascade_RespectedExplicitWins()
  {
    // Arrange: Explicit trigger exists
    TableAnalysis parent = new()
    {
      Name = "users",
      Schema = "test",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      PrimaryKeyColumns = ["id"],
      ChildTables = ["orders"],
      ReactivationCascade = true
    };
    TableAnalysis child = LeafTable("orders", "user_id", "users");
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    analysis.ExistingTriggers.Add(new ExistingTrigger
    {
      Name = "trg_users_cascade_reactivation",
      Schema = "test",
      TargetTable = "users",
      SourceFile = "my_triggers.sql",
      IsGenerated = false
    });
    SqlTriggerGenerator task = CreateTask(analysis);

    // Act
    task.Execute().Should().BeTrue();

    // Assert: trigger not generated due to explicit-wins
    File.Exists(Path.Combine(_outputDir, "trg_users_cascade_reactivation.sql")).Should().BeFalse();
  }

  [Fact]
  public void Execute_ReactivationCascade_SkipsChildWithoutSoftDelete()
  {
    // Arrange: Child without soft-delete
    TableAnalysis parent = new()
    {
      Name = "users",
      Schema = "test",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      PrimaryKeyColumns = ["id"],
      ChildTables = ["audit_log"],
      ReactivationCascade = true
    };
    TableAnalysis child = new()
    {
      Name = "audit_log",
      Schema = "test",
      HasSoftDelete = false, // No soft-delete
      PrimaryKeyColumns = ["id"],
      ForeignKeyReferences =
      [
        new ForeignKeyRef { ReferencedTable = "users", Columns = ["user_id"], ReferencedColumns = ["id"] }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    // Act
    task.Execute().Should().BeTrue();

    // Assert: trigger generated but child is skipped in comments
    string file = Path.Combine(_outputDir, "trg_users_cascade_reactivation.sql");
    File.Exists(file).Should().BeTrue();
    string content = File.ReadAllText(file);
    content.Should().Contain("Skipping audit_log: Does not have soft-delete enabled");
  }

  [Fact]
  public void Execute_ReactivationCascade_SupportsCompositeFK()
  {
    // Arrange: Composite FK relationship
    TableAnalysis parent = new()
    {
      Name = "tenant_users",
      Schema = "test",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      ActiveColumnName = "active",
      ValidToColumn = "valid_to",
      PrimaryKeyColumns = ["tenant_id", "user_id"],
      ChildTables = ["tenant_user_roles"],
      ReactivationCascade = true
    };
    TableAnalysis child = new()
    {
      Name = "tenant_user_roles",
      Schema = "test",
      HasSoftDelete = true,
      HasTemporalVersioning = true,
      ActiveColumnName = "active",
      ValidToColumn = "valid_to",
      PrimaryKeyColumns = ["id"],
      ForeignKeyReferences =
      [
        new ForeignKeyRef
        {
          ReferencedTable = "tenant_users",
          Columns = ["tenant_id", "user_id"],
          ReferencedColumns = ["tenant_id", "user_id"]
        }
      ]
    };
    SourceAnalysisResult analysis = CreateAnalysis(parent, child);
    SqlTriggerGenerator task = CreateTask(analysis);

    // Act
    task.Execute().Should().BeTrue();

    // Assert: trigger uses proper composite FK join
    string file = Path.Combine(_outputDir, "trg_tenant_users_cascade_reactivation.sql");
    File.Exists(file).Should().BeTrue();
    string content = File.ReadAllText(file);
    content.Should().Contain("c.tenant_id = i.tenant_id");
    content.Should().Contain("c.user_id = i.user_id");
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

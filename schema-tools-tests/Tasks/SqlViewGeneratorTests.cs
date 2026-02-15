using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class SqlViewGeneratorTests : IDisposable
{
  private readonly string _tempDir;
  private readonly MockBuildEngine _buildEngine;

  public SqlViewGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"view-gen-tests-{Guid.NewGuid():N}");
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

  private SqlViewGenerator CreateGenerator(SourceAnalysisResult analysis) => new()
  {
    AnalysisFile = "unused", // Using TestAnalysis injection
    OutputDirectory = _tempDir,
    NamingPattern = SchemaToolsDefaults.ViewNamingPattern,
    IncludeDeletedViews = false,
    DeletedViewNamingPattern = SchemaToolsDefaults.DeletedViewNamingPattern,
    Force = true,
    TestAnalysis = analysis,
    BuildEngine = _buildEngine
  };

  // ===========================================================================
  // No soft-delete tables
  // ===========================================================================

  [Fact]
  public void Execute_NoSoftDeleteTables_SkipsViewGeneration()
  {
    var analysis = new SourceAnalysisResult
    {
      Tables =
      [
        new TableAnalysis { Name = "regular_table", HasSoftDelete = false }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();
    Directory.GetFiles(_tempDir, "*.sql").Should().BeEmpty();
    _buildEngine.Messages.Should().Contain(m => m.Contains("no views needed"));
  }

  // ===========================================================================
  // Single soft-delete table
  // ===========================================================================

  [Fact]
  public void Execute_SingleSoftDeleteTable_GeneratesActiveView()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();

    string content = File.ReadAllText(files[0]);
    content.Should().Contain("CREATE VIEW [dbo].[vw_users]");
    content.Should().Contain("SELECT *");
    content.Should().Contain("FROM [dbo].[users]");
    content.Should().Contain("WHERE [record_active] = 1");
  }

  [Fact]
  public void Execute_SingleSoftDeleteTable_ViewFileNameMatchesPattern()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "products",
          Schema = "sales",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();
    Path.GetFileName(files[0]).Should().Be("vw_products.sql");
  }

  // ===========================================================================
  // Custom naming pattern
  // ===========================================================================

  [Fact]
  public void Execute_CustomNamingPattern_UsesPattern()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "is_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "orders",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    generator.NamingPattern = "active_{table}";
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();
    Path.GetFileName(files[0]).Should().Be("active_orders.sql");

    string content = File.ReadAllText(files[0]);
    content.Should().Contain("CREATE VIEW [dbo].[active_orders]");
    content.Should().Contain("WHERE [is_active] = 1");
  }

  // ===========================================================================
  // Custom active column and value
  // ===========================================================================

  [Fact]
  public void Execute_CustomActiveColumnAndValue_GeneratesCorrectFilter()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "status", ActiveValue = "'ACTIVE'" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "accounts",
          Schema = "crm",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string content = File.ReadAllText(Path.Combine(_tempDir, "vw_accounts.sql"));
    content.Should().Contain("CREATE VIEW [crm].[vw_accounts]");
    content.Should().Contain("FROM [crm].[accounts]");
    content.Should().Contain("WHERE [status] = 'ACTIVE'");
  }

  // ===========================================================================
  // Include deleted views
  // ===========================================================================

  [Fact]
  public void Execute_IncludeDeletedViews_GeneratesBothViews()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig
      {
        Active = "record_active",
        ActiveValue = "1",
        InactiveValue = "0"
      },
      Tables =
      [
        new TableAnalysis
        {
          Name = "customers",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    generator.IncludeDeletedViews = true;
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().HaveCount(2);

    // Check active view
    string activeContent = File.ReadAllText(Path.Combine(_tempDir, "vw_customers.sql"));
    activeContent.Should().Contain("CREATE VIEW [dbo].[vw_customers]");
    activeContent.Should().Contain("WHERE [record_active] = 1");

    // Check deleted view
    string deletedContent = File.ReadAllText(Path.Combine(_tempDir, "vw_customers_deleted.sql"));
    deletedContent.Should().Contain("CREATE VIEW [dbo].[vw_customers_deleted]");
    deletedContent.Should().Contain("WHERE [record_active] = 0");
  }

  // ===========================================================================
  // Multiple tables
  // ===========================================================================

  [Fact]
  public void Execute_MultipleSoftDeleteTables_GeneratesViewForEach()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        },
        new TableAnalysis
        {
          Name = "orders",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        },
        new TableAnalysis
        {
          Name = "products",
          Schema = "inventory",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Restrict
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().HaveCount(3);

    var fileNames = files.Select(Path.GetFileName).ToList();
    fileNames.Should().Contain("vw_users.sql");
    fileNames.Should().Contain("vw_orders.sql");
    fileNames.Should().Contain("vw_products.sql");

    // Verify product view uses correct schema
    string productContent = File.ReadAllText(Path.Combine(_tempDir, "vw_products.sql"));
    productContent.Should().Contain("CREATE VIEW [inventory].[vw_products]");
    productContent.Should().Contain("FROM [inventory].[products]");
  }

  // ===========================================================================
  // Ignore mode tables
  // ===========================================================================

  [Fact]
  public void Execute_IgnoreModeTable_SkipsViewGeneration()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        },
        new TableAnalysis
        {
          Name = "audit_log",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Ignore  // Should be skipped
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();  // Only users, not audit_log
    Path.GetFileName(files[0]).Should().Be("vw_users.sql");
  }

  // ===========================================================================
  // Explicit-wins policy
  // ===========================================================================

  [Fact]
  public void Execute_ExplicitViewExists_SkipsGeneration()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        },
        new TableAnalysis
        {
          Name = "orders",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ],
      ExistingViews =
      [
        new ExistingView
        {
          Name = "vw_users",
          Schema = "dbo",
          SourceFile = "/path/to/custom_views.sql",
          IsGenerated = false  // Explicit - should take precedence
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();  // Only orders, users was skipped
    Path.GetFileName(files[0]).Should().Be("vw_orders.sql");

    _buildEngine.Messages.Should().Contain(m =>
      m.Contains("Skipped") && m.Contains("vw_users") && m.Contains("Explicit"));
  }

  [Fact]
  public void Execute_GeneratedViewExists_SkipsRegeneration()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ],
      ExistingViews =
      [
        new ExistingView
        {
          Name = "vw_users",
          Schema = "dbo",
          SourceFile = "/generated/vw_users.sql",
          IsGenerated = true  // Already generated - should be regenerated when Force=true
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    generator.Force = true;  // Force regeneration
    bool result = generator.Execute();

    result.Should().BeTrue();

    // With Force=true, should regenerate
    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();
    Path.GetFileName(files[0]).Should().Be("vw_users.sql");
  }

  // ===========================================================================
  // No force regeneration
  // ===========================================================================

  [Fact]
  public void Execute_FileExistsAndForceIsFalse_SkipsFile()
  {
    // Pre-create a file
    string existingPath = Path.Combine(_tempDir, "vw_users.sql");
    File.WriteAllText(existingPath, "-- Existing content");

    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    generator.Force = false;
    bool result = generator.Execute();

    result.Should().BeTrue();

    // File should not be overwritten
    string content = File.ReadAllText(existingPath);
    content.Should().Be("-- Existing content");
  }

  // ===========================================================================
  // Header generation
  // ===========================================================================

  [Fact]
  public void Execute_GeneratedView_IncludesDoNotEditHeader()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string content = File.ReadAllText(Path.Combine(_tempDir, "vw_users.sql"));
    content.Should().Contain("AUTO-GENERATED BY SCHEMATOOLS");
    content.Should().Contain("DO NOT EDIT MANUALLY");
  }

  [Fact]
  public void Execute_GeneratedView_EndsWithGo()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string content = File.ReadAllText(Path.Combine(_tempDir, "vw_users.sql"));
    content.TrimEnd().Should().EndWith("GO");
  }

  // ===========================================================================
  // Summary output
  // ===========================================================================

  [Fact]
  public void Execute_LogsSummary()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        },
        new TableAnalysis
        {
          Name = "orders",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    _buildEngine.Messages.Should().Contain(m => m.Contains("Active views:") && m.Contains("2"));
    _buildEngine.Messages.Should().Contain(m => m.Contains("Output dir:"));
  }

  // ===========================================================================
  // Mixed scenarios
  // ===========================================================================

  [Fact]
  public void Execute_MixedTables_GeneratesOnlyForSoftDelete()
  {
    var analysis = new SourceAnalysisResult
    {
      Columns = new ColumnConfig { Active = "record_active", ActiveValue = "1" },
      Tables =
      [
        new TableAnalysis
        {
          Name = "users",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Cascade
        },
        new TableAnalysis
        {
          Name = "settings",
          Schema = "dbo",
          HasSoftDelete = false  // No soft-delete - no view
        },
        new TableAnalysis
        {
          Name = "audit_log",
          Schema = "dbo",
          HasSoftDelete = true,
          SoftDeleteMode = SoftDeleteMode.Ignore  // Ignored
        }
      ]
    };

    SqlViewGenerator generator = CreateGenerator(analysis);
    bool result = generator.Execute();

    result.Should().BeTrue();

    string[] files = Directory.GetFiles(_tempDir, "*.sql");
    files.Should().ContainSingle();
    Path.GetFileName(files[0]).Should().Be("vw_users.sql");
  }
}

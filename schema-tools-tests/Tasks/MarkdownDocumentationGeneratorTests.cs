using SchemaTools.Configuration;
using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class MarkdownDocumentationGeneratorTests
{
  // -------------------------------------------------------------------------
  // Test data builders
  // -------------------------------------------------------------------------

  private static SchemaToolsConfig CreateConfig(
      HistoryTableMode historyMode = HistoryTableMode.None,
      bool infrastructureStyling = true,
      bool erDiagramDomainColumnsOnly = true,
      bool includeStatistics = true,
      bool includeErDiagrams = true,
      bool includeConstraints = true,
      bool includeIndexes = false)
  {
    return new SchemaToolsConfig
    {
      Documentation = new DocumentationConfig
      {
        IncludeStatistics = includeStatistics,
        IncludeErDiagrams = includeErDiagrams,
        IncludeConstraints = includeConstraints,
        IncludeIndexes = includeIndexes,
        HistoryTables = historyMode,
        InfrastructureColumnStyling = infrastructureStyling,
        ErDiagramDomainColumnsOnly = erDiagramDomainColumnsOnly
      }
    };
  }

  /// <summary>
  /// Returns two authored tables (users, departments) in the "Core" category
  /// and one history table (users_history). Users has temporal versioning, soft delete,
  /// FK to departments (documented), and FK on record_created_by to individuals (undocumented).
  /// </summary>
  private static SchemaMetadata CreateMetadata(bool includeHistory = true)
  {
    var usersColumns = new List<ColumnMetadata>
    {
      new() { Name = "id", Type = "INT", IsPrimaryKey = true, IsIdentity = true },
      new() { Name = "name", Type = "NVARCHAR(100)" },
      new() { Name = "email", Type = "NVARCHAR(200)", Description = "Contact email" },
      new()
      {
        Name = "department_id", Type = "INT",
        ForeignKey = new ForeignKeyReference { Table = "departments", Column = "id" }
      },
      new() { Name = "record_active", Type = "BIT", DefaultValue = "1" },
      new()
      {
        Name = "record_created_by", Type = "UNIQUEIDENTIFIER",
        ForeignKey = new ForeignKeyReference { Table = "individuals", Column = "id" }
      },
      new()
      {
        Name = "record_valid_from", Type = "DATETIME2",
        IsGeneratedAlways = true, GeneratedAlwaysType = GeneratedAlwaysType.RowStart
      },
      new()
      {
        Name = "record_valid_until", Type = "DATETIME2",
        IsGeneratedAlways = true, GeneratedAlwaysType = GeneratedAlwaysType.RowEnd
      }
    };

    var usersConstraints = new ConstraintsCollection
    {
      PrimaryKey = new PrimaryKeyConstraint { Name = "PK_users", Columns = ["id"] },
      ForeignKeys =
      [
        new ForeignKeyConstraint
        {
          Name = "FK_users_departments",
          Columns = ["department_id"],
          ReferencedTable = "departments",
          ReferencedColumns = ["id"]
        },
        new ForeignKeyConstraint
        {
          Name = "FK_users_individuals",
          Columns = ["record_created_by"],
          ReferencedTable = "individuals",
          ReferencedColumns = ["id"],
          OnDelete = ForeignKeyAction.SetNull
        }
      ]
    };

    var departmentsColumns = new List<ColumnMetadata>
    {
      new() { Name = "id", Type = "INT", IsPrimaryKey = true, IsIdentity = true },
      new() { Name = "name", Type = "NVARCHAR(100)" },
      new() { Name = "budget", Type = "DECIMAL(18,2)", IsComputed = true, ComputedExpression = "[base]*1.2" }
    };

    var departmentsConstraints = new ConstraintsCollection
    {
      PrimaryKey = new PrimaryKeyConstraint { Name = "PK_departments", Columns = ["id"] },
      CheckConstraints =
      [
        new CheckConstraint { Name = "CK_departments_name", Expression = "LEN([name]) > 0" }
      ]
    };

    var tables = new List<TableMetadata>
    {
      new()
      {
        Name = "users",
        Schema = "dbo",
        Category = "Core",
        Description = "Application users",
        HasTemporalVersioning = true,
        HasSoftDelete = true,
        HasActiveColumn = true,
        ActiveColumnName = "record_active",
        HistoryTable = "[dbo].[users_history]",
        Columns = usersColumns,
        Constraints = usersConstraints
      },
      new()
      {
        Name = "departments",
        Schema = "dbo",
        Category = "Core",
        Description = "Organisational departments",
        IsAppendOnly = true,
        Columns = departmentsColumns,
        Constraints = departmentsConstraints
      }
    };

    if (includeHistory)
    {
      tables.Add(new TableMetadata
      {
        Name = "users_history",
        Schema = "dbo",
        Category = "Core",
        IsHistoryTable = true,
        Columns =
        [
          new() { Name = "id", Type = "INT" },
          new() { Name = "name", Type = "NVARCHAR(100)" },
          new() { Name = "email", Type = "NVARCHAR(200)" },
          new() { Name = "department_id", Type = "INT" },
          new() { Name = "record_active", Type = "BIT" },
          new() { Name = "record_created_by", Type = "UNIQUEIDENTIFIER" },
          new() { Name = "record_valid_from", Type = "DATETIME2" },
          new() { Name = "record_valid_until", Type = "DATETIME2" }
        ]
      });
    }

    return new SchemaMetadata
    {
      Database = "TestDb",
      GeneratedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
      Version = "1.0.0",
      Tables = tables,
      Categories = new Dictionary<string, string>
      {
        ["Core"] = "Core domain tables"
      }
    };
  }

  /// <summary>
  /// Adds an orders table in the "Commerce" category with a cross-category
  /// FK to users (in "Core"), for testing cross-category ER stubs.
  /// </summary>
  private static SchemaMetadata CreateCrossCategoryMetadata()
  {
    SchemaMetadata baseMetadata = CreateMetadata();
    var tables = new List<TableMetadata>(baseMetadata.Tables)
    {
      new()
      {
        Name = "orders",
        Schema = "dbo",
        Category = "Commerce",
        Description = "Customer orders",
        Columns =
        [
          new() { Name = "id", Type = "INT", IsPrimaryKey = true, IsIdentity = true },
          new()
          {
            Name = "user_id", Type = "INT",
            ForeignKey = new ForeignKeyReference { Table = "users", Column = "id" }
          },
          new() { Name = "amount", Type = "DECIMAL(18,2)" }
        ],
        Constraints = new ConstraintsCollection
        {
          PrimaryKey = new PrimaryKeyConstraint { Name = "PK_orders", Columns = ["id"] },
          ForeignKeys =
          [
            new ForeignKeyConstraint
            {
              Name = "FK_orders_users",
              Columns = ["user_id"],
              ReferencedTable = "users",
              ReferencedColumns = ["id"],
              OnDelete = ForeignKeyAction.Cascade
            }
          ]
        }
      }
    };

    return baseMetadata with
    {
      Tables = tables,
      Categories = new Dictionary<string, string>
      {
        ["Core"] = "Core domain tables",
        ["Commerce"] = "Commerce tables"
      }
    };
  }

  private static string RunGenerator(SchemaMetadata metadata, SchemaToolsConfig config)
  {
    string tempFile = Path.Combine(Path.GetTempPath(), $"schema-doc-test-{Guid.NewGuid()}.md");
    try
    {
      var task = new MarkdownDocumentationGenerator
      {
        MetadataFile = "test.json",
        OutputFile = tempFile,
        BuildEngine = new MockBuildEngine(),
        TestConfig = config,
        TestMetadata = metadata
      };

      bool result = task.Execute();
      result.Should().BeTrue("generator should execute without errors");

      return File.ReadAllText(tempFile);
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  // -------------------------------------------------------------------------
  // Document structure
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_GeneratesHeaderWithDatabaseInfo()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    md.Should().Contain("# Database Schema Documentation");
    md.Should().Contain("**Database:** TestDb");
    md.Should().Contain("**Generated:** 2024-01-15 10:00:00 UTC");
    md.Should().Contain("**Schema Version:** 1.0.0");
  }

  [Fact]
  public void Execute_GeneratesTableOfContentsWithCategoryGrouping()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    md.Should().Contain("## Table of Contents");
    md.Should().Contain("[Statistics](#statistics)");
    md.Should().Contain("[ER Diagrams](#entity-relationship-diagrams)");
    // Category table format: one row per category with inline links
    md.Should().Contain("| Category | Tables |");
    md.Should().Contain("| [Core](#core) |");
    md.Should().Contain("[departments](#departments)");
    md.Should().Contain("[users](#users)");
  }

  [Fact]
  public void Execute_TableOfContentsExcludesDisabledSections()
  {
    SchemaToolsConfig config = CreateConfig(
        includeStatistics: false, includeErDiagrams: false);
    string md = RunGenerator(CreateMetadata(), config);

    md.Should().NotContain("[Statistics](#statistics)");
    md.Should().NotContain("[ER Diagrams](#entity-relationship-diagrams)");
  }

  [Fact]
  public void Execute_GroupsTablesByCategoryWithDescription()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    md.Should().Contain("<a id=\"core\"></a>");
    md.Should().Contain("## Core");
    md.Should().Contain("*Core domain tables*");
  }

  [Fact]
  public void Execute_RendersTableDescription()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    md.Should().Contain("### users");
    md.Should().Contain("> Application users");
    md.Should().Contain("### departments");
    md.Should().Contain("> Organisational departments");
  }

  [Fact]
  public void Execute_RendersTableProperties()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    md.Should().Contain("**Properties:** `Temporal` | `Soft Delete`");
    md.Should().Contain("**Properties:** `Append-Only`");
  }

  // -------------------------------------------------------------------------
  // Statistics
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_StatisticsComputedFromAuthoredTables()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    // 2 authored tables (users + departments), not 3 (excludes users_history)
    md.Should().Contain("| Tables | 2 |");
    md.Should().Contain("| Temporal | 1 |");
    md.Should().Contain("| History Tables | 1 |");
    md.Should().Contain("| Soft Delete | 1 |");
    md.Should().Contain("| Append-Only | 1 |");
    // 8 columns (users) + 3 columns (departments) = 11
    md.Should().Contain("| Columns | 11 |");
  }

  [Fact]
  public void Execute_StatisticsDisabled_OmitsSection()
  {
    SchemaToolsConfig config = CreateConfig(includeStatistics: false);
    string md = RunGenerator(CreateMetadata(), config);

    md.Should().NotContain("## Statistics");
  }

  // -------------------------------------------------------------------------
  // ER diagrams
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_ErDiagramExcludesInfrastructureColumns()
  {
    SchemaToolsConfig config = CreateConfig(erDiagramDomainColumnsOnly: true);
    string md = RunGenerator(CreateMetadata(), config);

    string erSection = ExtractSection(md, "## Entity Relationship Diagrams", "##");

    // Domain columns should be present
    erSection.Should().Contain("INT id PK");
    erSection.Should().Contain("NVARCHAR(100) name");

    // Infrastructure columns should be excluded
    erSection.Should().NotContain("record_active");
    erSection.Should().NotContain("record_valid_from");
    erSection.Should().NotContain("record_valid_until");
    erSection.Should().NotContain("record_created_by");
  }

  [Fact]
  public void Execute_ErDiagramIncludesAllColumnsWhenDomainOnlyDisabled()
  {
    SchemaToolsConfig config = CreateConfig(erDiagramDomainColumnsOnly: false);
    string md = RunGenerator(CreateMetadata(), config);

    string erSection = ExtractSection(md, "## Entity Relationship Diagrams", "##");

    erSection.Should().Contain("record_active");
    erSection.Should().Contain("record_valid_from");
  }

  [Fact]
  public void Execute_ErDiagramExcludesHistoryTables()
  {
    // Even in Full history mode, ER diagrams use authored tables only
    SchemaToolsConfig config = CreateConfig(historyMode: HistoryTableMode.Full);
    string md = RunGenerator(CreateMetadata(), config);

    string erSection = ExtractSection(md, "## Entity Relationship Diagrams", "##");

    erSection.Should().NotContain("users_history {");
  }

  [Fact]
  public void Execute_ErDiagramRendersCrossCategoryFkStubs()
  {
    SchemaToolsConfig config = CreateConfig(erDiagramDomainColumnsOnly: true);
    string md = RunGenerator(CreateCrossCategoryMetadata(), config);

    // Commerce category should contain orders as a full entity
    // and users as a stub entity (PK only - cross-category FK target)
    string commerceSection = ExtractSectionContaining(md, "### Commerce - ER Diagram", "###");

    commerceSection.Should().Contain("orders {");
    // Stub for users should show PK column
    commerceSection.Should().Contain("users {");
    commerceSection.Should().Contain("INT id PK");
    // Relationship line
    commerceSection.Should().Contain("orders }o--|| users");
  }

  [Fact]
  public void Execute_ErDiagramDisabled_OmitsSection()
  {
    SchemaToolsConfig config = CreateConfig(includeErDiagrams: false);
    string md = RunGenerator(CreateMetadata(), config);

    md.Should().NotContain("## Entity Relationship Diagrams");
    md.Should().NotContain("erDiagram");
  }

  // -------------------------------------------------------------------------
  // Column table - infrastructure grouping
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_InfrastructureStyling_SeparatesColumnsWithEmptyRow()
  {
    SchemaToolsConfig config = CreateConfig(infrastructureStyling: true);
    string md = RunGenerator(CreateMetadata(), config);

    string usersSection = ExtractSection(md, "### users", "###");

    // Separator row between domain and infrastructure columns
    usersSection.Should().Contain("|  |  |  |  |  |");

    // Domain columns appear before separator
    string[] lines = usersSection.Split('\n');
    int separatorIdx = Array.FindIndex(lines, l => l.Contains("|  |  |  |  |  |"));
    int idIdx = Array.FindIndex(lines, l => l.Contains("| `id` |"));
    int activeIdx = Array.FindIndex(lines, l => l.Contains("| *`record_active`* |"));

    separatorIdx.Should().BeGreaterThan(idIdx, "domain columns should precede separator");
    activeIdx.Should().BeGreaterThan(separatorIdx, "infrastructure columns should follow separator");
  }

  [Fact]
  public void Execute_InfrastructureStyling_AddsAutoDescriptions()
  {
    SchemaToolsConfig config = CreateConfig(infrastructureStyling: true);
    string md = RunGenerator(CreateMetadata(), config);

    string usersSection = ExtractSection(md, "### users", "###");

    usersSection.Should().Contain("Soft-delete flag");
    usersSection.Should().Contain("Period start");
    usersSection.Should().Contain("Period end");
  }

  [Fact]
  public void Execute_InfrastructureStylingDisabled_RendersAllColumnsWithoutSeparator()
  {
    SchemaToolsConfig config = CreateConfig(infrastructureStyling: false);
    string md = RunGenerator(CreateMetadata(), config);

    string usersSection = ExtractSection(md, "### users", "###");

    usersSection.Should().NotContain("|  |  |  |  |  |");
    // All columns should still be present
    usersSection.Should().Contain("| `id` |");
    usersSection.Should().Contain("| `record_active` |");
    // No auto-descriptions when infra styling disabled
    usersSection.Should().NotContain("Soft-delete flag");
  }

  [Fact]
  public void Execute_ExplicitDescriptionTakesPrecedenceOverAutoDescription()
  {
    // email column has explicit description "Contact email";
    // infrastructure auto-description should not overwrite user-supplied ones
    SchemaToolsConfig config = CreateConfig(infrastructureStyling: true);
    string md = RunGenerator(CreateMetadata(), config);

    md.Should().Contain("Contact email");
  }

  [Fact]
  public void Execute_GeneratedAlwaysColumn_IncludesConstraintMarker()
  {
    SchemaToolsConfig config = CreateConfig(infrastructureStyling: true);
    string md = RunGenerator(CreateMetadata(), config);

    string usersSection = ExtractSection(md, "### users", "###");

    // record_valid_from should have GENERATED ALWAYS constraint
    string validFromLine = ExtractLineContaining(usersSection, "record_valid_from");
    validFromLine.Should().Contain("GENERATED ALWAYS");
  }

  [Fact]
  public void Execute_ComputedColumn_ShowsExpressionAsDescription()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    string deptSection = ExtractSection(md, "### departments", "###");
    string budgetLine = ExtractLineContaining(deptSection, "budget");

    budgetLine.Should().Contain("COMPUTED");
    budgetLine.Should().Contain("`[base]*1.2`");
  }

  // -------------------------------------------------------------------------
  // FK cross-references
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_FkToDocumentedTable_RendersAsMarkdownLink()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    string usersSection = ExtractSection(md, "### users", "###");

    // department_id FK to departments (documented table) should be a link
    string deptIdLine = ExtractLineContaining(usersSection, "department_id");
    deptIdLine.Should().Contain("FK -> [departments](#departments)");
  }

  [Fact]
  public void Execute_FkToUndocumentedTable_RendersAsPlainText()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    string usersSection = ExtractSection(md, "### users", "###");

    // record_created_by FK to individuals (not a documented table) should be plain text
    string createdByLine = ExtractLineContaining(usersSection, "record_created_by");
    createdByLine.Should().Contain("FK -> individuals");
    createdByLine.Should().NotContain("[individuals]");
  }

  // -------------------------------------------------------------------------
  // Constraints section
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_ForeignKeyConstraint_RendersWithLinksForDocumentedTables()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    // FK to departments (documented) - should be a link
    md.Should().Contain("(department_id) -> [departments](#departments)(id)");
    // FK to individuals (undocumented) - should be plain text
    md.Should().Contain("(record_created_by) -> individuals(id)");
  }

  [Fact]
  public void Execute_ForeignKeyWithActions_RendersActionSuffix()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    // FK_users_individuals has OnDelete = SetNull
    md.Should().Contain("[ON DELETE SET NULL]");
  }

  [Fact]
  public void Execute_CheckConstraint_RendersExpression()
  {
    string md = RunGenerator(CreateMetadata(), CreateConfig());

    md.Should().Contain("`CK_departments_name`: `LEN([name]) > 0`");
  }

  [Fact]
  public void Execute_ConstraintsDisabled_OmitsSection()
  {
    SchemaToolsConfig config = CreateConfig(includeConstraints: false);
    string md = RunGenerator(CreateMetadata(), config);

    md.Should().NotContain("#### Constraints");
    md.Should().NotContain("**Foreign Keys:**");
  }

  [Fact]
  public void Execute_ForeignKeyWithCascade_FormatsAction()
  {
    string md = RunGenerator(CreateCrossCategoryMetadata(), CreateConfig());

    md.Should().Contain("[ON DELETE CASCADE]");
  }

  // -------------------------------------------------------------------------
  // History table modes
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_HistoryModeNone_ExcludesHistoryTablesFromDocument()
  {
    SchemaToolsConfig config = CreateConfig(historyMode: HistoryTableMode.None);
    string md = RunGenerator(CreateMetadata(), config);

    // History table should not appear as a section
    md.Should().NotContain("### users_history");
    // History table reference should be plain text (no link)
    md.Should().Contain("**History Table:** users_history");
    // ToC should not contain history tables link
    md.Should().NotContain("[History Tables](#history-tables)");
  }

  [Fact]
  public void Execute_HistoryModeCompact_GeneratesSummarySection()
  {
    SchemaToolsConfig config = CreateConfig(historyMode: HistoryTableMode.Compact);
    string md = RunGenerator(CreateMetadata(), config);

    // History table should not appear as its own section
    md.Should().NotContain("### users_history");

    // Compact summary section should exist
    md.Should().Contain("## History Tables");
    md.Should().Contain("| History Table | Source Table | Columns |");
    md.Should().Contain("| users_history | [users](#users) |");

    // ToC should include history tables link
    md.Should().Contain("[History Tables](#history-tables)");

    // History table reference from users section should link to compact section
    md.Should().Contain("**History Table:** [users_history](#history-tables)");
  }

  [Fact]
  public void Execute_HistoryModeFull_IncludesHistoryTablesAsSections()
  {
    SchemaToolsConfig config = CreateConfig(historyMode: HistoryTableMode.Full);
    string md = RunGenerator(CreateMetadata(), config);

    // History table should appear as a full section
    md.Should().Contain("### users_history");
    // No compact summary section
    md.Should().NotContain("## History Tables");
    // History table reference should link to the table's own section
    md.Should().Contain("**History Table:** [users_history](#users_history)");
  }

  [Fact]
  public void Execute_HistoryModeNone_StatisticsStillShowHistoryCount()
  {
    SchemaToolsConfig config = CreateConfig(historyMode: HistoryTableMode.None);
    string md = RunGenerator(CreateMetadata(), config);

    // Statistics should show history table count even when excluded from docs
    md.Should().Contain("| History Tables | 1 |");
  }

  // -------------------------------------------------------------------------
  // Cross-category documentation
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_MultipleCategoriesRenderedInOrder()
  {
    string md = RunGenerator(CreateCrossCategoryMetadata(), CreateConfig());

    int commerceIdx = md.IndexOf("## Commerce", StringComparison.Ordinal);
    int coreIdx = md.IndexOf("## Core", StringComparison.Ordinal);

    commerceIdx.Should().BeGreaterThan(0);
    coreIdx.Should().BeGreaterThan(0);
    // Commerce comes before Core alphabetically
    commerceIdx.Should().BeLessThan(coreIdx);
  }

  // -------------------------------------------------------------------------
  // Output file handling
  // -------------------------------------------------------------------------

  [Fact]
  public void Execute_CreatesOutputDirectoryIfMissing()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), $"schema-doc-test-{Guid.NewGuid()}");
    string outputFile = Path.Combine(tempDir, "nested", "SCHEMA.md");

    try
    {
      var task = new MarkdownDocumentationGenerator
      {
        MetadataFile = "test.json",
        OutputFile = outputFile,
        BuildEngine = new MockBuildEngine(),
        TestConfig = CreateConfig(),
        TestMetadata = CreateMetadata()
      };

      bool result = task.Execute();
      result.Should().BeTrue();
      File.Exists(outputFile).Should().BeTrue();
    }
    finally
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, recursive: true);
    }
  }

  // -------------------------------------------------------------------------
  // String extraction helpers
  // -------------------------------------------------------------------------

  /// <summary>
  /// Extracts text from startMarker up to the next occurrence of endMarkerPrefix
  /// at the same or higher heading level.
  /// </summary>
  private static string ExtractSection(string document, string startMarker, string endMarkerPrefix)
  {
    int startIdx = document.IndexOf(startMarker, StringComparison.Ordinal);
    if (startIdx < 0)
      return string.Empty;

    int contentStart = startIdx + startMarker.Length;
    // Append space to avoid matching deeper heading levels (e.g. "##" matching "###")
    int endIdx = document.IndexOf("\n" + endMarkerPrefix + " ", contentStart, StringComparison.Ordinal);

    return endIdx >= 0
        ? document[startIdx..endIdx]
        : document[startIdx..];
  }

  /// <summary>
  /// Extracts a section that contains a specific substring, searching forward
  /// from the marker to the next occurrence of endMarkerPrefix.
  /// </summary>
  private static string ExtractSectionContaining(string document, string startMarker, string endMarkerPrefix)
  {
    int startIdx = document.IndexOf(startMarker, StringComparison.Ordinal);
    if (startIdx < 0)
      return string.Empty;

    int contentStart = startIdx + startMarker.Length;
    // Append space to avoid matching deeper heading levels (e.g. "###" matching "####")
    int endIdx = document.IndexOf("\n" + endMarkerPrefix + " ", contentStart, StringComparison.Ordinal);

    return endIdx >= 0
        ? document[startIdx..endIdx]
        : document[startIdx..];
  }

  /// <summary>
  /// Returns the single line from text that contains the specified substring.
  /// </summary>
  private static string ExtractLineContaining(string text, string substring)
  {
    return text.Split('\n')
        .FirstOrDefault(line => line.Contains(substring, StringComparison.Ordinal))
        ?? string.Empty;
  }
}

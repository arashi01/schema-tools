using SchemaTools.Models;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

public class MarkdownDocumentationGeneratorTests : IDisposable
{
  private readonly string _testOutputPath;

  public MarkdownDocumentationGeneratorTests()
  {
    _testOutputPath = Path.Combine(Path.GetTempPath(), "schema-tools-tests", Guid.NewGuid().ToString());
    Directory.CreateDirectory(_testOutputPath);
  }

  private static SchemaToolsConfig CreateConfig(Action<DocumentationConfig>? configure = null)
  {
    var config = new SchemaToolsConfig
    {
      Database = "TestDB",
      DefaultSchema = "test",
      Documentation = new DocumentationConfig
      {
        Enabled = true,
        IncludeErDiagrams = true,
        IncludeStatistics = true,
        IncludeConstraints = true,
        IncludeIndexes = false
      },
      Categories = new Dictionary<string, string>
      {
        ["core"] = "Core business entities"
      }
    };
    configure?.Invoke(config.Documentation);
    return config;
  }

  private static SchemaMetadata CreateMetadata()
  {
    var users = new TableMetadata
    {
      Name = "users",
      Schema = "test",
      Category = "core",
      Description = "User accounts",
      PrimaryKey = "id",
      PrimaryKeyType = "UNIQUEIDENTIFIER",
      HasTemporalVersioning = true,
      HasActiveColumn = true,
      HasSoftDelete = true,
      HistoryTable = "[test].[users_history]",
      Columns =
        [
            new ColumnMetadata { Name = "id", Type = "UNIQUEIDENTIFIER", IsPrimaryKey = true },
                new ColumnMetadata { Name = "username", Type = "VARCHAR(100)" },
                new ColumnMetadata { Name = "email", Type = "VARCHAR(200)", IsUnique = true },
                new ColumnMetadata
                {
                    Name = "created_by", Type = "UNIQUEIDENTIFIER",
                    ForeignKey = new ForeignKeyReference { Table = "individuals", Column = "id" }
                },
                new ColumnMetadata { Name = "active", Type = "BIT", DefaultValue = "1" }
        ],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = "pk_users", Columns = ["id"] },
        ForeignKeys =
            [
                new ForeignKeyConstraint
                    {
                        Name = "fk_users_department",
                        Columns = ["department_id"],
                        ReferencedTable = "departments",
                        ReferencedColumns = ["id"]
                    }
            ],
        CheckConstraints =
            [
                new CheckConstraint
                    {
                        Name = "ck_users_email",
                        Expression = "[email] LIKE '%@%'"
                    }
            ]
      },
      Triggers = new TriggerConfiguration
      {
        HardDelete = new HardDeleteTrigger
        {
          Generate = true,
          Name = "trg_users_hard_delete"
        }
      }
    };

    var departments = new TableMetadata
    {
      Name = "departments",
      Schema = "test",
      Category = "core",
      Description = "Organisational departments",
      PrimaryKey = "id",
      IsAppendOnly = true,
      Columns =
        [
            new ColumnMetadata { Name = "id", Type = "INT", IsPrimaryKey = true, IsIdentity = true },
                new ColumnMetadata { Name = "name", Type = "VARCHAR(100)" }
        ],
      Constraints = new ConstraintsCollection
      {
        PrimaryKey = new PrimaryKeyConstraint { Name = "pk_departments", Columns = ["id"] }
      }
    };

    return new SchemaMetadata
    {
      Database = "TestDB",
      DefaultSchema = "test",
      GeneratedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
      Version = "1.0.0",
      Tables = [users, departments],
      Statistics = new SchemaStatistics
      {
        TotalTables = 2,
        TemporalTables = 1,
        SoftDeleteTables = 1,
        AppendOnlyTables = 1,
        TotalColumns = 7,
        TotalConstraints = 3,
        TriggersToGenerate = 1
      },
      Categories = new Dictionary<string, string>
      {
        ["core"] = "Core business entities"
      }
    };
  }

  private string RunGenerator(SchemaToolsConfig? config = null, SchemaMetadata? metadata = null)
  {
    string outputFile = Path.Combine(_testOutputPath, $"{Guid.NewGuid()}.md");
    var task = new MarkdownDocumentationGenerator
    {
      MetadataFile = "unused",
      OutputFile = outputFile,
      TestConfig = config ?? CreateConfig(),
      TestMetadata = metadata ?? CreateMetadata(),
      BuildEngine = new MockBuildEngine()
    };

    task.Execute().Should().BeTrue();
    return File.ReadAllText(outputFile);
  }

  // ─── Document structure ─────────────────────────────────────────

  [Fact]
  public void Execute_GeneratesDocumentWithTitle()
  {
    string md = RunGenerator();

    md.Should().Contain("# Database Schema Documentation");
    md.Should().Contain("**Database:** TestDB");
  }

  [Fact]
  public void Execute_IncludesTableOfContents()
  {
    string md = RunGenerator();

    md.Should().Contain("## Table of Contents");
    md.Should().Contain("[users]");
    md.Should().Contain("[departments]");
  }

  [Fact]
  public void Execute_IncludesStatistics()
  {
    string md = RunGenerator();

    md.Should().Contain("## Statistics");
    md.Should().Contain("Total Tables");
    md.Should().Contain("| 2 |");
  }

  [Fact]
  public void Execute_GroupsByCategory()
  {
    string md = RunGenerator();

    md.Should().Contain("## core");
    md.Should().Contain("*Core business entities*");
  }

  // ─── Table detail sections ──────────────────────────────────────

  [Fact]
  public void Execute_RendersTableDescription()
  {
    string md = RunGenerator();

    md.Should().Contain("### users");
    md.Should().Contain("> User accounts");
  }

  [Fact]
  public void Execute_RendersTableProperties()
  {
    string md = RunGenerator();

    md.Should().Contain("Temporal");
    md.Should().Contain("Soft Delete");
    md.Should().Contain("Append-Only");
  }

  [Fact]
  public void Execute_RendersHistoryTable()
  {
    string md = RunGenerator();

    md.Should().Contain("**History Table:** `[test].[users_history]`");
  }

  [Fact]
  public void Execute_RendersColumnTable()
  {
    string md = RunGenerator();

    md.Should().Contain("| Column | Type | Nullable | Constraints | Description |");
    md.Should().Contain("| `id` |");
    md.Should().Contain("PK");
    md.Should().Contain("UNIQUE");
    md.Should().Contain("FK→individuals");
    md.Should().Contain("DEFAULT 1");
  }

  // ─── Constraint sections ────────────────────────────────────────

  [Fact]
  public void Execute_RendersForeignKeys()
  {
    string md = RunGenerator();

    md.Should().Contain("**Foreign Keys:**");
    md.Should().Contain("`fk_users_department`");
    md.Should().Contain("→ departments(id)");
  }

  [Fact]
  public void Execute_RendersCheckConstraints()
  {
    string md = RunGenerator();

    md.Should().Contain("**Check Constraints:**");
    md.Should().Contain("`ck_users_email`");
  }

  // ─── ER diagrams ────────────────────────────────────────────────

  [Fact]
  public void Execute_GeneratesMermaidErDiagram()
  {
    string md = RunGenerator();

    md.Should().Contain("```mermaid");
    md.Should().Contain("erDiagram");
    md.Should().Contain("users {");
  }

  [Fact]
  public void Execute_WithErDiagramsDisabled_OmitsMermaid()
  {
    SchemaToolsConfig config = CreateConfig(d => d.IncludeErDiagrams = false);
    string md = RunGenerator(config);

    md.Should().NotContain("```mermaid");
  }

  // ─── Feature toggles ────────────────────────────────────────────

  [Fact]
  public void Execute_WithStatisticsDisabled_OmitsStatistics()
  {
    SchemaToolsConfig config = CreateConfig(d => d.IncludeStatistics = false);
    string md = RunGenerator(config);

    md.Should().NotContain("## Statistics");
  }

  [Fact]
  public void Execute_WithConstraintsDisabled_OmitsConstraintSection()
  {
    SchemaToolsConfig config = CreateConfig(d => d.IncludeConstraints = false);
    string md = RunGenerator(config);

    md.Should().NotContain("**Foreign Keys:**");
    md.Should().NotContain("**Check Constraints:**");
  }

  // ─── Output file ────────────────────────────────────────────────

  [Fact]
  public void Execute_CreatesOutputDirectory()
  {
    string nestedOutput = Path.Combine(_testOutputPath, "nested", "docs", "schema.md");
    var task = new MarkdownDocumentationGenerator
    {
      MetadataFile = "unused",
      OutputFile = nestedOutput,
      TestConfig = CreateConfig(),
      TestMetadata = CreateMetadata(),
      BuildEngine = new MockBuildEngine()
    };

    task.Execute().Should().BeTrue();
    File.Exists(nestedOutput).Should().BeTrue();
  }

  public void Dispose()
  {
    if (Directory.Exists(_testOutputPath))
    {
      Directory.Delete(_testOutputPath, recursive: true);
    }
  }
}

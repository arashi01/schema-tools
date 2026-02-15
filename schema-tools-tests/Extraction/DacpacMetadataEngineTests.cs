using System.Text.Json;
using Microsoft.SqlServer.Dac.Model;
using SchemaTools.Extraction;
using SchemaTools.Models;

namespace SchemaTools.Tests.Extraction;

/// <summary>
/// Tests for the core DacFx metadata extraction engine, decoupled from MSBuild.
/// Uses in-memory <see cref="TSqlModel"/> instances to avoid .dacpac file I/O.
/// </summary>
public sealed class DacpacMetadataEngineTests : IDisposable
{
  private readonly string _tempDir;
  private readonly List<string> _infoMessages = [];
  private readonly List<string> _errorMessages = [];

  public DacpacMetadataEngineTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"schematools-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_tempDir))
      {
        Directory.Delete(_tempDir, recursive: true);
      }
    }
    catch
    {
      // Clean-up failure is not a test failure
    }
  }

  [Fact]
  public void Execute_SimpleTable_ProducesValidJson()
  {
    TSqlModel model = CreateModel("CREATE TABLE [dbo].[users] ([id] INT NOT NULL PRIMARY KEY, [name] NVARCHAR(100) NOT NULL);");
    string outputFile = OutputPath("schema.json");

    bool result = RunEngine(model, outputFile);

    result.Should().BeTrue();
    File.Exists(outputFile).Should().BeTrue();

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    metadata.Should().NotBeNull();
    metadata!.Tables.Should().ContainSingle(t => t.Name == "users");
  }

  [Fact]
  public void Execute_ExtractsColumnsWithTypes()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[products] (
        [id] INT NOT NULL PRIMARY KEY,
        [name] NVARCHAR(200) NOT NULL,
        [price] DECIMAL(10,2) NOT NULL,
        [description] NVARCHAR(MAX) NULL
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "products");
    table.Columns.Should().HaveCount(4);
    table.Columns.Should().Contain(c => c.Name == "name" && c.Type == "nvarchar(200)");
    table.Columns.Should().Contain(c => c.Name == "price" && c.Type == "decimal(10,2)");
    table.Columns.Should().Contain(c => c.Name == "description" && c.Type == "nvarchar(max)");
  }

  [Fact]
  public void Execute_ExtractsPrimaryKeyConstraint()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[orders] (
        [order_id] INT NOT NULL,
        [line] INT NOT NULL,
        CONSTRAINT [PK_orders] PRIMARY KEY ([order_id], [line])
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "orders");
    table.Constraints.PrimaryKey.Should().NotBeNull();
    table.Constraints.PrimaryKey!.Name.Should().Be("PK_orders");
    table.Constraints.PrimaryKey.Columns.Should().BeEquivalentTo(["order_id", "line"]);
  }

  [Fact]
  public void Execute_ExtractsForeignKeyConstraint()
  {
    TSqlModel model = CreateModel(
      "CREATE TABLE [dbo].[categories] ([id] INT NOT NULL PRIMARY KEY);",
      @"CREATE TABLE [dbo].[products] (
        [id] INT NOT NULL PRIMARY KEY,
        [category_id] INT NOT NULL,
        CONSTRAINT [FK_products_categories] FOREIGN KEY ([category_id]) REFERENCES [dbo].[categories]([id])
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "products");
    table.Constraints.ForeignKeys.Should().ContainSingle();
    Models.ForeignKeyConstraint fk = table.Constraints.ForeignKeys[0];
    fk.Name.Should().Be("FK_products_categories");
    fk.ReferencedTable.Should().Be("categories");
    fk.Columns.Should().ContainSingle("category_id");
  }

  [Fact]
  public void Execute_ExtractsUniqueConstraint()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[users] (
        [id] INT NOT NULL PRIMARY KEY,
        [email] NVARCHAR(255) NOT NULL,
        CONSTRAINT [UQ_users_email] UNIQUE ([email])
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "users");
    table.Constraints.UniqueConstraints.Should().ContainSingle();
    table.Constraints.UniqueConstraints[0].Name.Should().Be("UQ_users_email");
    table.Columns.Single(c => c.Name == "email").IsUnique.Should().BeTrue();
  }

  [Fact]
  public void Execute_ExtractsCheckConstraint()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[products] (
        [id] INT NOT NULL PRIMARY KEY,
        [price] DECIMAL(10,2) NOT NULL,
        CONSTRAINT [CK_products_price_positive] CHECK ([price] > 0)
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "products");
    table.Constraints.CheckConstraints.Should().ContainSingle();
    table.Constraints.CheckConstraints[0].Name.Should().Be("CK_products_price_positive");
  }

  [Fact]
  public void Execute_ExtractsIndexes()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[users] (
        [id] INT NOT NULL PRIMARY KEY,
        [email] NVARCHAR(255) NOT NULL,
        [name] NVARCHAR(100) NOT NULL
      );",
      "CREATE NONCLUSTERED INDEX [IX_users_email] ON [dbo].[users]([email]);");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "users");
    table.Indexes.Should().ContainSingle(i => i.Name == "IX_users_email");
  }

  [Fact]
  public void Execute_DetectsIdentityColumn()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[events] (
        [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [name] NVARCHAR(100) NOT NULL
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "events");
    table.Columns.Single(c => c.Name == "id").IsIdentity.Should().BeTrue();
    table.Columns.Single(c => c.Name == "name").IsIdentity.Should().BeFalse();
  }

  [Fact]
  public void Execute_CalculatesStatistics()
  {
    TSqlModel model = CreateModel(
      "CREATE TABLE [dbo].[users] ([id] INT NOT NULL PRIMARY KEY);",
      "CREATE TABLE [dbo].[orders] ([id] INT NOT NULL PRIMARY KEY, [user_id] INT NOT NULL);");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    metadata!.Statistics.Should().NotBeNull();
    metadata.Statistics!.TotalTables.Should().Be(2);
    metadata.Statistics.TotalColumns.Should().BeGreaterThanOrEqualTo(3);
  }

  [Fact]
  public void Execute_CreatesOutputDirectory()
  {
    TSqlModel model = CreateModel("CREATE TABLE [dbo].[t] ([id] INT NOT NULL PRIMARY KEY);");
    string outputFile = Path.Combine(_tempDir, "nested", "deep", "schema.json");

    RunEngine(model, outputFile);

    File.Exists(outputFile).Should().BeTrue();
  }

  [Fact]
  public void Execute_NonexistentDacpac_ReturnsFalse()
  {
    string outputFile = OutputPath("schema.json");

    var engine = new DacpacMetadataEngine(
      Path.Combine(_tempDir, "nonexistent.dacpac"),
      outputFile,
      string.Empty,
      "Database",
      _ => { }, _ => { }, _ => { }, msg => _errorMessages.Add(msg));

    bool result = engine.Execute();

    result.Should().BeFalse();
    _errorMessages.Should().Contain(m => m.Contains("Dacpac not found"));
  }

  [Fact]
  public void Execute_LogsProgressMessages()
  {
    TSqlModel model = CreateModel("CREATE TABLE [dbo].[t] ([id] INT NOT NULL PRIMARY KEY);");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    _infoMessages.Should().Contain(m => m.Contains("Schema Metadata Extractor"));
    _infoMessages.Should().Contain(m => m.Contains("Extraction Summary"));
    _infoMessages.Should().Contain(m => m.Contains("Metadata written to"));
  }

  [Fact]
  public void Execute_SelfReferencingFK_DoesNotCrash()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[categories] (
        [id] INT NOT NULL PRIMARY KEY,
        [parent_id] INT NULL,
        CONSTRAINT [FK_categories_parent] FOREIGN KEY ([parent_id]) REFERENCES [dbo].[categories]([id])
      );");
    string outputFile = OutputPath("schema.json");

    bool result = RunEngine(model, outputFile);

    result.Should().BeTrue();
    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "categories");
    table.Constraints.ForeignKeys.Should().ContainSingle(fk => fk.ReferencedTable == "categories");
  }

  [Fact]
  public void Execute_WithConfig_AppliesDatabaseName()
  {
    TSqlModel model = CreateModel("CREATE TABLE [dbo].[t] ([id] INT NOT NULL PRIMARY KEY);");
    string outputFile = OutputPath("schema.json");
    var config = new SchemaToolsConfig { Database = "TestDB" };

    var engine = new DacpacMetadataEngine(
      "unused.dacpac", outputFile, string.Empty, "Database",
      msg => _infoMessages.Add(msg), _ => { }, _ => { }, _ => { })
    {
      OverrideModel = model,
      OverrideConfig = config
    };

    engine.Execute();

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    metadata!.Database.Should().Be("TestDB");
  }

  [Fact]
  public void Execute_MultipleSchemas_ExtractsBoth()
  {
    TSqlModel model = CreateModel(
      "CREATE SCHEMA [sales];",
      "CREATE TABLE [dbo].[users] ([id] INT NOT NULL PRIMARY KEY);",
      "CREATE TABLE [sales].[orders] ([id] INT NOT NULL PRIMARY KEY);");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    metadata!.Tables.Should().Contain(t => t.Name == "users" && t.Schema == "dbo");
    metadata.Tables.Should().Contain(t => t.Name == "orders" && t.Schema == "sales");
  }

  [Fact]
  public void Execute_NullableColumns_SetsNullableFlag()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[t] (
        [id] INT NOT NULL PRIMARY KEY,
        [optional] NVARCHAR(50) NULL,
        [required] NVARCHAR(50) NOT NULL
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single();
    table.Columns.Single(c => c.Name == "optional").Nullable.Should().BeTrue();
    table.Columns.Single(c => c.Name == "required").Nullable.Should().BeFalse();
  }

  // -- Helpers ---------------------------------------------------------------

  private static TSqlModel CreateModel(params string[] statements)
  {
    var model = new TSqlModel(SqlServerVersion.Sql170, new TSqlModelOptions());
    foreach (string sql in statements)
    {
      model.AddObjects(sql);
    }
    return model;
  }

  private string OutputPath(string fileName) => Path.Combine(_tempDir, fileName);

  private bool RunEngine(TSqlModel model, string outputFile, SchemaToolsConfig? config = null)
  {
    var engine = new DacpacMetadataEngine(
      "unused.dacpac",
      outputFile,
      string.Empty,
      "Database",
      info: msg => _infoMessages.Add(msg),
      verbose: _ => { },
      warning: _ => { },
      error: msg => _errorMessages.Add(msg))
    {
      OverrideModel = model,
      OverrideConfig = config
    };

    return engine.Execute();
  }

  private static SchemaMetadata? ReadMetadata(string path)
  {
    string json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<SchemaMetadata>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });
  }
}

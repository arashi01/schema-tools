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
      "unused.dacpac", outputFile, string.Empty, string.Empty, "Database",
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

  // -- Bug A: Category bridging from analysis --------------------------------

  [Fact]
  public void Execute_WithCategories_BridgesToTableMetadata()
  {
    TSqlModel model = CreateModel(
      "CREATE TABLE [dbo].[users] ([id] INT NOT NULL PRIMARY KEY);",
      "CREATE TABLE [dbo].[audit_log] ([id] INT NOT NULL PRIMARY KEY);");
    string outputFile = OutputPath("schema.json");
    var categories = new Dictionary<string, string>
    {
      ["users"] = "core",
      ["audit_log"] = "audit"
    };

    RunEngine(model, outputFile, categories: categories);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    metadata!.Tables.Single(t => t.Name == "users").Category.Should().Be("core");
    metadata.Tables.Single(t => t.Name == "audit_log").Category.Should().Be("audit");
  }

  [Fact]
  public void Execute_WithoutCategories_LeavesNull()
  {
    TSqlModel model = CreateModel("CREATE TABLE [dbo].[t] ([id] INT NOT NULL PRIMARY KEY);");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    metadata!.Tables.Single().Category.Should().BeNull();
  }

  // -- Bug B: Structured type decomposition ----------------------------------

  [Fact]
  public void Execute_VarcharColumn_ExposesMaxLength()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[t] (
        [id] INT NOT NULL PRIMARY KEY,
        [name] VARCHAR(100) NOT NULL
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    ColumnMetadata col = metadata!.Tables.Single().Columns.Single(c => c.Name == "name");
    col.MaxLength.Should().Be(100);
    col.IsMaxLength.Should().BeFalse();
    col.Precision.Should().BeNull();
    col.Scale.Should().BeNull();
  }

  [Fact]
  public void Execute_VarcharMaxColumn_SetsIsMaxLength()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[t] (
        [id] INT NOT NULL PRIMARY KEY,
        [payload] VARCHAR(MAX) NOT NULL
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    ColumnMetadata col = metadata!.Tables.Single().Columns.Single(c => c.Name == "payload");
    col.Type.Should().Be("varchar(max)");
    col.IsMaxLength.Should().BeTrue();
    col.MaxLength.Should().BeNull();
  }

  [Fact]
  public void Execute_DecimalColumn_ExposesPrecisionAndScale()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[t] (
        [id] INT NOT NULL PRIMARY KEY,
        [amount] DECIMAL(9,6) NOT NULL
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    ColumnMetadata col = metadata!.Tables.Single().Columns.Single(c => c.Name == "amount");
    col.Type.Should().Be("decimal(9,6)");
    col.Precision.Should().Be(9);
    col.Scale.Should().Be(6);
    col.MaxLength.Should().BeNull();
  }

  [Fact]
  public void Execute_NvarcharColumn_ExposesMaxLength()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[t] (
        [id] INT NOT NULL PRIMARY KEY,
        [title] NVARCHAR(255) NOT NULL
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    ColumnMetadata col = metadata!.Tables.Single().Columns.Single(c => c.Name == "title");
    col.Type.Should().Be("nvarchar(255)");
    col.MaxLength.Should().Be(255);
    col.IsMaxLength.Should().BeFalse();
  }

  [Fact]
  public void Execute_IntColumn_NoStructuredTypeProperties()
  {
    TSqlModel model = CreateModel("CREATE TABLE [dbo].[t] ([id] INT NOT NULL PRIMARY KEY);");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    ColumnMetadata col = metadata!.Tables.Single().Columns.Single(c => c.Name == "id");
    col.MaxLength.Should().BeNull();
    col.Precision.Should().BeNull();
    col.Scale.Should().BeNull();
    col.IsMaxLength.Should().BeFalse();
  }

  // -- Bug C: Polymorphic FK column marking ----------------------------------

  [Fact]
  public void Execute_PolymorphicTable_MarksTypeAndIdColumns()
  {
    TSqlModel model = CreateModel(@"
      CREATE TABLE [dbo].[phones] (
        [id] INT NOT NULL PRIMARY KEY,
        [owner_type] VARCHAR(50) NOT NULL,
        [owner_id] UNIQUEIDENTIFIER NOT NULL,
        [number] VARCHAR(20) NOT NULL,
        CONSTRAINT [CK_phones_owner_type] CHECK ([owner_type] IN ('user', 'organisation'))
      );");
    string outputFile = OutputPath("schema.json");
    var config = new SchemaToolsConfig();
    config.Columns.PolymorphicPatterns.Add(new PolymorphicPatternConfig
    {
      TypeColumn = "owner_type",
      IdColumn = "owner_id"
    });

    RunEngine(model, outputFile, config);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata table = metadata!.Tables.Single(t => t.Name == "phones");
    table.IsPolymorphic.Should().BeTrue();
    table.Columns.Single(c => c.Name == "owner_type").IsPolymorphicForeignKey.Should().BeTrue();
    table.Columns.Single(c => c.Name == "owner_id").IsPolymorphicForeignKey.Should().BeTrue();
    table.Columns.Single(c => c.Name == "number").IsPolymorphicForeignKey.Should().BeFalse();
  }

  // -- Bug D: Composite FK column-level metadata -----------------------------

  [Fact]
  public void Execute_CompositeFK_SetsColumnLevelMetadata()
  {
    TSqlModel model = CreateModel(
      @"CREATE TABLE [dbo].[parent] (
        [key_a] INT NOT NULL,
        [key_b] INT NOT NULL,
        CONSTRAINT [PK_parent] PRIMARY KEY ([key_a], [key_b])
      );",
      @"CREATE TABLE [dbo].[child] (
        [id] INT NOT NULL PRIMARY KEY,
        [ref_a] INT NOT NULL,
        [ref_b] INT NOT NULL,
        CONSTRAINT [FK_child_parent] FOREIGN KEY ([ref_a], [ref_b]) REFERENCES [dbo].[parent]([key_a], [key_b])
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata child = metadata!.Tables.Single(t => t.Name == "child");

    // Constraint-level: isComposite should be true
    child.Constraints.ForeignKeys.Single().IsComposite.Should().BeTrue();

    // Column-level: both columns should have FK reference and composite flag
    ColumnMetadata refA = child.Columns.Single(c => c.Name == "ref_a");
    refA.IsCompositeFK.Should().BeTrue();
    refA.ForeignKey.Should().NotBeNull();
    refA.ForeignKey!.Table.Should().Be("parent");
    refA.ForeignKey.Column.Should().Be("key_a");

    ColumnMetadata refB = child.Columns.Single(c => c.Name == "ref_b");
    refB.IsCompositeFK.Should().BeTrue();
    refB.ForeignKey.Should().NotBeNull();
    refB.ForeignKey!.Table.Should().Be("parent");
    refB.ForeignKey.Column.Should().Be("key_b");
  }

  [Fact]
  public void Execute_SingleColumnFK_NotMarkedAsComposite()
  {
    TSqlModel model = CreateModel(
      "CREATE TABLE [dbo].[parent] ([id] INT NOT NULL PRIMARY KEY);",
      @"CREATE TABLE [dbo].[child] (
        [id] INT NOT NULL PRIMARY KEY,
        [parent_id] INT NOT NULL,
        CONSTRAINT [FK_child_parent] FOREIGN KEY ([parent_id]) REFERENCES [dbo].[parent]([id])
      );");
    string outputFile = OutputPath("schema.json");

    RunEngine(model, outputFile);

    SchemaMetadata? metadata = ReadMetadata(outputFile);
    TableMetadata child = metadata!.Tables.Single(t => t.Name == "child");
    ColumnMetadata parentCol = child.Columns.Single(c => c.Name == "parent_id");
    parentCol.IsCompositeFK.Should().BeFalse();
    parentCol.ForeignKey.Should().NotBeNull();
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

  private bool RunEngine(
    TSqlModel model,
    string outputFile,
    SchemaToolsConfig? config = null,
    Dictionary<string, string>? categories = null)
  {
    var engine = new DacpacMetadataEngine(
      "unused.dacpac",
      outputFile,
      string.Empty,
      string.Empty,
      "Database",
      info: msg => _infoMessages.Add(msg),
      verbose: _ => { },
      warning: _ => { },
      error: msg => _errorMessages.Add(msg))
    {
      OverrideModel = model,
      OverrideConfig = config,
      OverrideCategories = categories
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

using Microsoft.SqlServer.Dac.Model;
using SchemaTools.Tasks;

namespace SchemaTools.Tests.Tasks;

/// <summary>
/// Tests for the <see cref="SchemaMetadataExtractor"/> MSBuild task wrapper.
/// Verifies the thin wrapper correctly delegates to <see cref="SchemaTools.Extraction.DacpacMetadataEngine"/>.
/// </summary>
public sealed class SchemaMetadataExtractorTests : IDisposable
{
  private readonly string _tempDir;

  public SchemaMetadataExtractorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"schematools-extractor-test-{Guid.NewGuid():N}");
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
  public void Execute_WithTestModel_Succeeds()
  {
    var model = new TSqlModel(SqlServerVersion.Sql170, new TSqlModelOptions());
    model.AddObjects("CREATE TABLE [dbo].[users] ([id] INT NOT NULL PRIMARY KEY, [name] NVARCHAR(100) NOT NULL);");

    string outputFile = Path.Combine(_tempDir, "schema.json");
    var task = new SchemaMetadataExtractor
    {
      BuildEngine = new MockBuildEngine(),
      DacpacPath = "unused.dacpac",
      OutputFile = outputFile,
      TestModel = model
    };

    bool result = task.Execute();

    result.Should().BeTrue();
    File.Exists(outputFile).Should().BeTrue();
  }

  [Fact]
  public void Execute_WithTestConfig_AppliesConfiguration()
  {
    var model = new TSqlModel(SqlServerVersion.Sql170, new TSqlModelOptions());
    model.AddObjects("CREATE TABLE [dbo].[t] ([id] INT NOT NULL PRIMARY KEY);");

    string outputFile = Path.Combine(_tempDir, "schema.json");
    var task = new SchemaMetadataExtractor
    {
      BuildEngine = new MockBuildEngine(),
      DacpacPath = "unused.dacpac",
      OutputFile = outputFile,
      TestModel = model,
      TestConfig = new SchemaTools.Configuration.SchemaToolsConfig { Database = "CustomDB" }
    };

    task.Execute();

    string json = File.ReadAllText(outputFile);
    json.Should().Contain("CustomDB");
  }

  [Fact]
  public void Execute_NonexistentDacpac_ReturnsFalseAndLogsError()
  {
    var engine = new MockBuildEngine();
    var task = new SchemaMetadataExtractor
    {
      BuildEngine = engine,
      DacpacPath = Path.Combine(_tempDir, "nonexistent.dacpac"),
      OutputFile = Path.Combine(_tempDir, "schema.json")
    };

    bool result = task.Execute();

    result.Should().BeFalse();
    engine.Errors.Should().NotBeEmpty();
  }
}

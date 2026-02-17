using SchemaTools.Diagnostics;
using SchemaTools.Models;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class MetadataLoaderTests
{
  [Fact]
  public void Load_WithTestOverride_ReturnsSuccess()
  {
    var metadata = new SchemaMetadata { Tables = new List<TableMetadata>() };

    OperationResult<SchemaMetadata> result = MetadataLoader.Load("nonexistent.json", metadata);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().BeSameAs(metadata);
    result.Diagnostics.Should().BeEmpty();
  }

  [Fact]
  public void Load_MissingFile_ReturnsFailureWithST4001()
  {
    OperationResult<SchemaMetadata> result = MetadataLoader.Load("does-not-exist.json");

    result.IsSuccess.Should().BeFalse();
    result.HasErrors.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle()
      .Which.Should().BeOfType<ExtractionError>()
      .Which.Code.Should().Be("ST4001");
  }

  [Fact]
  public void Load_MissingFile_MessageContainsFilePath()
  {
    string path = "missing/schema.json";

    OperationResult<SchemaMetadata> result = MetadataLoader.Load(path);

    result.Diagnostics.Should().ContainSingle()
      .Which.Message.Should().Contain(path);
  }

  [Fact]
  public void Load_EmptyJsonObject_ReturnsSuccessWithEmptyTables()
  {
    string tempFile = Path.GetTempFileName();
    try
    {
      // {} deserialises to a valid SchemaMetadata with default empty Tables list
      File.WriteAllText(tempFile, "{}");

      OperationResult<SchemaMetadata> result = MetadataLoader.Load(tempFile);

      result.IsSuccess.Should().BeTrue();
      result.Value.Tables.Should().BeEmpty();
    }
    finally
    {
      File.Delete(tempFile);
    }
  }

  [Fact]
  public void Load_InvalidJson_ReturnsFailureWithST4002()
  {
    string tempFile = Path.GetTempFileName();
    try
    {
      File.WriteAllText(tempFile, "not valid json {{{");

      OperationResult<SchemaMetadata> result = MetadataLoader.Load(tempFile);

      result.IsSuccess.Should().BeFalse();
      result.Diagnostics.Should().ContainSingle()
        .Which.Should().BeOfType<ExtractionError>()
        .Which.Code.Should().Be("ST4002");
    }
    finally
    {
      File.Delete(tempFile);
    }
  }

  [Fact]
  public void Load_ValidJson_ReturnsSuccess()
  {
    string tempFile = Path.GetTempFileName();
    try
    {
      File.WriteAllText(tempFile, """{"tables": [{"name": "users", "columns": []}]}""");

      OperationResult<SchemaMetadata> result = MetadataLoader.Load(tempFile);

      result.IsSuccess.Should().BeTrue();
      result.Value.Tables.Should().ContainSingle()
        .Which.Name.Should().Be("users");
    }
    finally
    {
      File.Delete(tempFile);
    }
  }
}

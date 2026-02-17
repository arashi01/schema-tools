using SchemaTools.Diagnostics;
using SchemaTools.Models;
using SchemaTools.Utilities;

namespace SchemaTools.Tests.Utilities;

public class AnalysisLoaderTests
{
  [Fact]
  public void Load_WithTestOverride_ReturnsSuccess()
  {
    var analysis = new SourceAnalysisResult { Tables = new List<TableAnalysis>() };

    OperationResult<SourceAnalysisResult> result = AnalysisLoader.Load("nonexistent.json", analysis);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().BeSameAs(analysis);
    result.Diagnostics.Should().BeEmpty();
  }

  [Fact]
  public void Load_MissingFile_ReturnsFailureWithST3001()
  {
    OperationResult<SourceAnalysisResult> result = AnalysisLoader.Load("does-not-exist.json");

    result.IsSuccess.Should().BeFalse();
    result.HasErrors.Should().BeTrue();
    result.Diagnostics.Should().ContainSingle()
      .Which.Should().BeOfType<GenerationError>()
      .Which.Code.Should().Be("ST3001");
  }

  [Fact]
  public void Load_MissingFile_MessageContainsFilePath()
  {
    string path = "missing/analysis.json";

    OperationResult<SourceAnalysisResult> result = AnalysisLoader.Load(path);

    result.Diagnostics.Should().ContainSingle()
      .Which.Message.Should().Contain(path);
  }

  [Fact]
  public void Load_InvalidJson_ReturnsFailureWithST3002()
  {
    string tempFile = Path.GetTempFileName();
    try
    {
      File.WriteAllText(tempFile, "not valid json {{{");

      OperationResult<SourceAnalysisResult> result = AnalysisLoader.Load(tempFile);

      result.IsSuccess.Should().BeFalse();
      result.HasErrors.Should().BeTrue();
      result.Diagnostics.Should().ContainSingle()
        .Which.Should().BeOfType<GenerationError>()
        .Which.Code.Should().Be("ST3002");
    }
    finally
    {
      File.Delete(tempFile);
    }
  }

  [Fact]
  public void Load_EmptyJsonObject_ReturnsSuccessWithEmptyTables()
  {
    string tempFile = Path.GetTempFileName();
    try
    {
      File.WriteAllText(tempFile, "{}");

      OperationResult<SourceAnalysisResult> result = AnalysisLoader.Load(tempFile);

      // {} deserialises to a valid SourceAnalysisResult with default empty Tables list
      result.IsSuccess.Should().BeTrue();
      result.Value.Tables.Should().BeEmpty();
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
      File.WriteAllText(tempFile, """{"tables": [{"name": "users"}]}""");

      OperationResult<SourceAnalysisResult> result = AnalysisLoader.Load(tempFile);

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

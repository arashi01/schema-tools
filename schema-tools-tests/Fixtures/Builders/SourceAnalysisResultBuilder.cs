using SchemaTools.Models;

namespace SchemaTools.Tests.Fixtures.Builders;

/// <summary>
/// Fluent builder for <see cref="SourceAnalysisResult"/> test instances.
/// Uses <c>with</c> expressions to preserve record immutability.
/// </summary>
internal sealed class SourceAnalysisResultBuilder
{
  private SourceAnalysisResult _result;

  public SourceAnalysisResultBuilder()
  {
    _result = new SourceAnalysisResult
    {
      DefaultSchema = "test",
      AnalysedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
  }

  public SourceAnalysisResultBuilder WithDefaultSchema(string schema)
  {
    _result = _result with { DefaultSchema = schema };
    return this;
  }

  public SourceAnalysisResultBuilder WithTable(TableAnalysis table)
  {
    _result = _result with { Tables = [.. _result.Tables, table] };
    return this;
  }

  public SourceAnalysisResultBuilder WithTables(params TableAnalysis[] tables)
  {
    _result = _result with { Tables = [.. tables] };
    return this;
  }

  public SourceAnalysisResultBuilder WithExistingTrigger(
    string name,
    string targetTable,
    string sourceFile,
    bool isGenerated = false,
    string schema = "test")
  {
    _result = _result with
    {
      ExistingTriggers =
      [
        .. _result.ExistingTriggers,
        new ExistingTrigger
        {
          Name = name,
          Schema = schema,
          TargetTable = targetTable,
          SourceFile = sourceFile,
          IsGenerated = isGenerated
        }
      ]
    };
    return this;
  }

  public SourceAnalysisResultBuilder WithExistingView(
    string name,
    string sourceFile,
    bool isGenerated = false,
    string schema = "test")
  {
    _result = _result with
    {
      ExistingViews =
      [
        .. _result.ExistingViews,
        new ExistingView
        {
          Name = name,
          Schema = schema,
          SourceFile = sourceFile,
          IsGenerated = isGenerated
        }
      ]
    };
    return this;
  }

  public SourceAnalysisResultBuilder WithColumns(ColumnConfig columns)
  {
    _result = _result with { Columns = columns };
    return this;
  }

  public SourceAnalysisResultBuilder WithFeatures(FeatureFlags features)
  {
    _result = _result with { Features = features };
    return this;
  }

  public SourceAnalysisResultBuilder WithGeneratedDirectories(
    string triggersDir = "",
    string viewsDir = "")
  {
    _result = _result with { GeneratedTriggersDirectory = triggersDir, GeneratedViewsDirectory = viewsDir };
    return this;
  }

  public SourceAnalysisResultBuilder Configure(Func<SourceAnalysisResult, SourceAnalysisResult> configure)
  {
    _result = configure(_result);
    return this;
  }

  public SourceAnalysisResult Build() => _result;
}

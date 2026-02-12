using System.Collections;
using Microsoft.Build.Framework;

namespace SchemaTools.Tests;

internal class MockBuildEngine : IBuildEngine
{
  private readonly List<string> _messages = [];
  private readonly List<string> _warnings = [];
  private readonly List<string> _errors = [];

  public IReadOnlyList<string> Messages => _messages;
  public IReadOnlyList<string> Warnings => _warnings;
  public IReadOnlyList<string> Errors => _errors;

  public void LogErrorEvent(BuildErrorEventArgs e) => _errors.Add(e.Message ?? string.Empty);
  public void LogWarningEvent(BuildWarningEventArgs e) => _warnings.Add(e.Message ?? string.Empty);
  public void LogMessageEvent(BuildMessageEventArgs e) => _messages.Add(e.Message ?? string.Empty);
  public void LogCustomEvent(CustomBuildEventArgs e) => _messages.Add(e.Message ?? string.Empty);
  public bool BuildProjectFile(string projectFileName, string[] targetNames,
      IDictionary globalProperties, IDictionary targetOutputs) => true;
  public bool ContinueOnError => false;
  public int LineNumberOfTaskNode => 0;
  public int ColumnNumberOfTaskNode => 0;
  public string ProjectFileOfTaskNode => string.Empty;
}

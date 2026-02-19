using Microsoft.Build.Utilities;

namespace SchemaTools.Diagnostics;

/// <summary>
/// Shared helper for reporting <see cref="SchemaToolsError"/> diagnostics
/// through the MSBuild logging infrastructure. Avoids duplicating the
/// <c>LogError</c>/<c>LogWarning</c> dispatch across every task.
/// </summary>
internal static class DiagnosticReporter
{
  /// <summary>
  /// Report all diagnostics from an operation result to the MSBuild log.
  /// Errors are reported via <see cref="TaskLoggingHelper.LogError(string, string, string, string, int, int, int, int, string, object[])"/>
  /// and warnings via the corresponding <c>LogWarning</c> overload, both using
  /// the full positional signature so that IDE error-list entries link back to
  /// the originating source file and line.
  /// </summary>
  /// <param name="log">The MSBuild task logging helper (typically <c>this.Log</c>).</param>
  /// <param name="diagnostics">The diagnostics to report.</param>
  public static void Report(TaskLoggingHelper log, IReadOnlyList<SchemaToolsError> diagnostics)
  {
    foreach (SchemaToolsError d in diagnostics)
    {
      if (d.Severity == ErrorSeverity.Error)
      {
        log.LogError(
          subcategory: "SchemaTools",
          errorCode: d.Code,
          helpKeyword: null,
          file: d.Location?.FilePath,
          lineNumber: d.Location?.Line ?? 0,
          columnNumber: d.Location?.Column ?? 0,
          endLineNumber: 0,
          endColumnNumber: 0,
          message: d.Message);
      }
      else
      {
        log.LogWarning(
          subcategory: "SchemaTools",
          warningCode: d.Code,
          helpKeyword: null,
          file: d.Location?.FilePath,
          lineNumber: d.Location?.Line ?? 0,
          columnNumber: d.Location?.Column ?? 0,
          endLineNumber: 0,
          endColumnNumber: 0,
          message: d.Message);
      }
    }
  }
}

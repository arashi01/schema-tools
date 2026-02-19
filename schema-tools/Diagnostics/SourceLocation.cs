namespace SchemaTools.Diagnostics;

/// <summary>
/// Represents a location within a source file for diagnostic reporting.
/// </summary>
/// <param name="FilePath">Absolute or project-relative path to the source file.</param>
/// <param name="Line">One-based line number within the file.</param>
/// <param name="Column">One-based column number within the line.</param>
public sealed record SourceLocation(string FilePath, int Line, int Column);

namespace SchemaTools.Diagnostics;

/// <summary>
/// Indicates the severity of a diagnostic produced by a SchemaTools operation.
/// </summary>
public enum ErrorSeverity
{
  /// <summary>A non-fatal diagnostic that does not prevent further processing.</summary>
  Warning,

  /// <summary>A fatal diagnostic that prevents further processing.</summary>
  Error
}

/// <summary>
/// Base type for all SchemaTools diagnostics with source location tracking.
/// </summary>
/// <remarks>
/// Uses <c>required init</c> properties rather than positional record parameters
/// to avoid the value-type default problem (positional <c>T?</c> is
/// <c>Nullable&lt;T&gt;</c> for value types) and to allow clear opt-in to
/// location tracking.
/// </remarks>
public abstract record SchemaToolsError
{
  /// <summary>
  /// Optional source location where the diagnostic originated.
  /// </summary>
  public SourceLocation? Location { get; init; }

  /// <summary>
  /// Machine-readable diagnostic code (e.g. ST1001, ST2003).
  /// </summary>
  public required string Code { get; init; }

  /// <summary>
  /// Human-readable diagnostic message.
  /// </summary>
  public required string Message { get; init; }

  /// <summary>
  /// The severity of this diagnostic.
  /// </summary>
  public abstract ErrorSeverity Severity { get; }
}

// ST1xxx: Annotation parsing ------------------------------------------------

/// <summary>
/// An error encountered whilst parsing SQL annotations (ST1xxx range).
/// </summary>
public sealed record AnnotationError : SchemaToolsError
{
  /// <inheritdoc />
  public override ErrorSeverity Severity => ErrorSeverity.Error;
}

/// <summary>
/// A warning encountered whilst parsing SQL annotations (ST1xxx range).
/// </summary>
public sealed record AnnotationWarning : SchemaToolsError
{
  /// <inheritdoc />
  public override ErrorSeverity Severity => ErrorSeverity.Warning;
}

// ST2xxx: Schema validation --------------------------------------------------

/// <summary>
/// An error encountered during schema validation (ST2xxx range).
/// </summary>
public sealed record ValidationError : SchemaToolsError
{
  /// <inheritdoc />
  public override ErrorSeverity Severity => ErrorSeverity.Error;
}

/// <summary>
/// A warning encountered during schema validation (ST2xxx range).
/// </summary>
public sealed record ValidationWarning : SchemaToolsError
{
  /// <inheritdoc />
  public override ErrorSeverity Severity => ErrorSeverity.Warning;
}

// ST3xxx: Code generation ----------------------------------------------------

/// <summary>
/// An error encountered during code generation (ST3xxx range).
/// </summary>
public sealed record GenerationError : SchemaToolsError
{
  /// <inheritdoc />
  public override ErrorSeverity Severity => ErrorSeverity.Error;
}

// ST4xxx: Metadata extraction (DacFx) ----------------------------------------

/// <summary>
/// An error encountered during metadata extraction (ST4xxx range).
/// </summary>
public sealed record ExtractionError : SchemaToolsError
{
  /// <inheritdoc />
  public override ErrorSeverity Severity => ErrorSeverity.Error;
}

/// <summary>
/// A warning encountered during metadata extraction (ST4xxx range).
/// </summary>
public sealed record ExtractionWarning : SchemaToolsError
{
  /// <inheritdoc />
  public override ErrorSeverity Severity => ErrorSeverity.Warning;
}

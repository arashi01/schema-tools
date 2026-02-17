namespace SchemaTools.Diagnostics;

/// <summary>
/// Result of an operation that may produce diagnostics (warnings and/or errors).
/// A result can carry both a value AND diagnostics (e.g. success with warnings).
/// </summary>
/// <typeparam name="T">The type of the successful value.</typeparam>
public sealed class OperationResult<T>
{
  private readonly T? _value;
  private readonly bool _hasValue;

  /// <summary>
  /// All diagnostics (errors and warnings) produced by the operation.
  /// </summary>
  public IReadOnlyList<SchemaToolsError> Diagnostics { get; }

  /// <summary>
  /// <see langword="true"/> if any diagnostic has <see cref="ErrorSeverity.Error"/> severity.
  /// </summary>
  public bool HasErrors => Diagnostics.Any(d => d.Severity == ErrorSeverity.Error);

  /// <summary>
  /// <see langword="true"/> if any diagnostic has <see cref="ErrorSeverity.Warning"/> severity.
  /// </summary>
  public bool HasWarnings => Diagnostics.Any(d => d.Severity == ErrorSeverity.Warning);

  /// <summary>
  /// <see langword="true"/> if the operation produced a value and no errors.
  /// A successful result may still carry warnings.
  /// </summary>
  public bool IsSuccess => _hasValue && !HasErrors;

  private OperationResult(T value, IReadOnlyList<SchemaToolsError> diagnostics)
  {
    _value = value;
    _hasValue = true;
    Diagnostics = diagnostics;
  }

  private OperationResult(IReadOnlyList<SchemaToolsError> diagnostics)
  {
    _value = default;
    _hasValue = false;
    Diagnostics = diagnostics;
  }

  /// <summary>Successful result with no diagnostics.</summary>
  public static OperationResult<T> Success(T value) =>
    new(value, Array.Empty<SchemaToolsError>());

  /// <summary>Successful result carrying warnings.</summary>
  public static OperationResult<T> WithWarnings(T value, IReadOnlyList<SchemaToolsError> warnings) =>
    new(value, warnings);

  /// <summary>Failed result with one or more errors (may also contain warnings).</summary>
  public static OperationResult<T> Fail(IReadOnlyList<SchemaToolsError> errors) =>
    new(errors);

  /// <summary>
  /// Unwrap the value or throw. Use in the impure shell after checking
  /// <see cref="IsSuccess"/>.
  /// </summary>
  /// <exception cref="InvalidOperationException">
  /// Thrown when the result does not carry a value.
  /// </exception>
  public T Value => _hasValue
    ? _value!
    : throw new InvalidOperationException(
        "Cannot access Value on a failed OperationResult. " +
        "Check IsSuccess before accessing Value.");

  /// <summary>
  /// Transform the value if successful, preserving diagnostics.
  /// If the result is a failure, the mapping function is not invoked.
  /// </summary>
  public OperationResult<TOut> Map<TOut>(Func<T, TOut> f) =>
    _hasValue
      ? new OperationResult<TOut>(f(_value!), Diagnostics)
      : OperationResult<TOut>.Fail(Diagnostics);

  /// <summary>
  /// Chain a fallible operation, accumulating diagnostics from both stages.
  /// If this result is a failure, <paramref name="f"/> is not invoked.
  /// </summary>
  public OperationResult<TOut> Bind<TOut>(Func<T, OperationResult<TOut>> f)
  {
    if (!_hasValue)
      return OperationResult<TOut>.Fail(Diagnostics);

    OperationResult<TOut> next = f(_value!);
    IReadOnlyList<SchemaToolsError> combined = CombineDiagnostics(Diagnostics, next.Diagnostics);
    return next._hasValue
      ? OperationResult<TOut>.WithWarnings(next._value!, combined)
      : OperationResult<TOut>.Fail(combined);
  }

  private static IReadOnlyList<SchemaToolsError> CombineDiagnostics(
    IReadOnlyList<SchemaToolsError> a,
    IReadOnlyList<SchemaToolsError> b)
  {
    if (a.Count == 0)
      return b;
    if (b.Count == 0)
      return a;
    List<SchemaToolsError> list = new(a.Count + b.Count);
    list.AddRange(a);
    list.AddRange(b);
    return list;
  }
}

/// <summary>
/// Static combinators for composing multiple <see cref="OperationResult{T}"/> values.
/// </summary>
public static class OperationResult
{
  /// <summary>
  /// Combine two independent results, accumulating ALL diagnostics from both.
  /// If either has errors, the combined result is a failure carrying all diagnostics.
  /// </summary>
  public static OperationResult<(T1, T2)> Combine<T1, T2>(
    OperationResult<T1> r1,
    OperationResult<T2> r2)
  {
    IReadOnlyList<SchemaToolsError> diagnostics = CombineLists(r1.Diagnostics, r2.Diagnostics);

    if (r1.IsSuccess && r2.IsSuccess)
      return OperationResult<(T1, T2)>.WithWarnings((r1.Value, r2.Value), diagnostics);

    return OperationResult<(T1, T2)>.Fail(diagnostics);
  }

  /// <summary>
  /// Accumulate results from multiple independent operations.
  /// All operations run regardless of individual failures.
  /// Successful values are collected; all diagnostics are accumulated.
  /// </summary>
  /// <remarks>
  /// Failed results do NOT contribute values to downstream processing.
  /// The caller receives an <see cref="IReadOnlyList{T}"/> containing only
  /// successfully-processed items, plus ALL diagnostics.
  /// </remarks>
  public static OperationResult<IReadOnlyList<T>> Accumulate<T>(
    IEnumerable<OperationResult<T>> results)
  {
    List<T> values = new();
    List<SchemaToolsError> diagnostics = new();

    foreach (OperationResult<T> result in results)
    {
      diagnostics.AddRange(result.Diagnostics);
      if (result.IsSuccess)
        values.Add(result.Value);
    }

    return diagnostics.Any(d => d.Severity == ErrorSeverity.Error)
      ? OperationResult<IReadOnlyList<T>>.Fail(diagnostics)
      : OperationResult<IReadOnlyList<T>>.WithWarnings(values, diagnostics);
  }

  /// <summary>
  /// Efficiently concatenate two diagnostic lists, avoiding allocation when
  /// either list is empty.
  /// </summary>
  public static IReadOnlyList<SchemaToolsError> CombineLists(
    IReadOnlyList<SchemaToolsError> a,
    IReadOnlyList<SchemaToolsError> b)
  {
    if (a.Count == 0)
      return b;
    if (b.Count == 0)
      return a;
    List<SchemaToolsError> list = new(a.Count + b.Count);
    list.AddRange(a);
    list.AddRange(b);
    return list;
  }
}

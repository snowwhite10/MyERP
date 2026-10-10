namespace MyERP.Shared.Errors;

/// <summary>
/// 輸入欄位驗證失敗（400）。<see cref="Errors"/> 以 camelCase 欄位路徑為鍵（例如 <c>lines[0].qty</c>）。
/// 命名避開 FluentValidation.ValidationException。
/// </summary>
public sealed class RequestValidationException : ErpException
{
    public RequestValidationException(string field, string message)
        : this(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] })
    {
    }

    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base(CommonErrorCodes.ValidationFailed, "One or more validation errors occurred.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one validation error is required.", nameof(errors));
        }

        Errors = errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public override ErpErrorKind Kind => ErpErrorKind.Validation;
}

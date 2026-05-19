namespace IronGOAL;

public readonly struct GoalResult<T> where T : notnull
{
    private readonly T?            _value;
    private readonly string?       _error;
    private readonly GoalErrorCode _code;
    
    private GoalResult(T value)
    {
        _value = value;
        _error = null;
        _code  = GoalErrorCode.None;
    }
    
    private GoalResult(GoalErrorCode code, string error)
    {
        _value = default;
        _error = error;
        _code  = code;
    }
    
    public bool          IsSuccess    => _code == GoalErrorCode.None;
    public bool          IsFailure    => _code != GoalErrorCode.None;
    public GoalErrorCode ErrorCode    => _code;
    public string        ErrorMessage => _error ?? string.Empty;
    
    /// <summary>
    /// Returns the success value.  If called on a failure result, logs a
    /// fatal entry via the host-provided handler and returns default(T).
    /// No exception is thrown under any circumstances.
    /// </summary>
    public T? Value
    {
        get
        {
            if (IsSuccess) return _value;

            GoalResultLogger.Log(
                GoalLogSeverity.Fatal,
                GoalErrorCode.InvalidAccess,
                $"Value accessed on failure result [{_code}]: {_error}");

            return default;
        }
    }
    
    public void Deconstruct(out bool isSuccess, out T? value, out string? error)
    {
        isSuccess = IsSuccess;
        value     = _value;
        error     = _error;
    }
    
    public static GoalResult<T> Okay(T value)                        => new(value);
    public static GoalResult<T> Fail(GoalErrorCode code, string msg) => new(code, msg);
    
    public override string ToString() => IsSuccess
        ? $"Ok({_value})"
        : $"Fail([{_code}] {_error})";
}

public readonly struct GoalResult
{
    private readonly string?       _error;
    private readonly GoalErrorCode _code;
    
    private GoalResult(GoalErrorCode code, string? error)
    {
        _code  = code;
        _error = error;
    }
    
    public bool          IsSuccess    => _code == GoalErrorCode.None;
    public bool          IsFailure    => _code != GoalErrorCode.None;
    public GoalErrorCode ErrorCode    => _code;
    public string        ErrorMessage => _error ?? string.Empty;
    
    public void Deconstruct(out bool isSuccess, out string? error)
    {
        isSuccess = IsSuccess;
        error     = _error;
    }
    
    public static readonly GoalResult Ok = new(GoalErrorCode.None, null);
    
    public static GoalResult Fail(GoalErrorCode code, string msg) => new(code, msg);
    
    public override string ToString() => IsSuccess
        ? "Ok"
        : $"Fail([{_code}] {_error})";
}

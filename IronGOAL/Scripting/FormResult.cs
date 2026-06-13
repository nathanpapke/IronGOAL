namespace IronGOAL.Scripting;

public readonly record struct FormResult(SchemeForm Form, bool Success, object? Value, string? ErrorMessage)
{
    public static FormResult Okay(SchemeForm form, object? value) =>
        new(form, true, value, null);
    
    public static FormResult Failed(SchemeForm form, string errorMessage) =>
        new(form, false, null, errorMessage);
}

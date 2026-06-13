using System.Text;

namespace IronGOAL.Scripting;

/// <summary>
/// The result of loading a source (file or in-memory string) via
/// <see cref="ScriptLoader.LoadSource"/> / <see cref="ScriptLoader.LoadFile"/>:
/// one <see cref="FormResult"/> per top-level form, in the order
/// <c>read</c> returned them.
/// </summary>
public sealed class ScriptLoadReport(string sourceName, IReadOnlyList<FormResult> forms)
{
    public string SourceName { get; } = sourceName;
    public IReadOnlyList<FormResult> Forms { get; } = forms;
    
    public bool IsSuccess => Forms.All(f => f.Success);
    
    public IEnumerable<FormResult> Failures => Forms.Where(f => !f.Success);
 
    /// <summary>
    /// One line per failure: <c>"{SourceName} (form #{Index}, `{preview}`): {message}"</c>,
    /// or <c>"{SourceName}: {message}"</c> for the <c>Index == 0</c> whole-source
    /// syntax-error case.
    /// </summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        
        foreach (var f in Failures)
        {
            sb.Append(SourceName);
            
            if (f.Form.Index > 0)
                sb.Append($" (form #{f.Form.Index}, `{f.Form.Preview}`)");
            
            sb.Append(": ").Append(f.ErrorMessage).Append("\r\n");
        }
        
        return sb.ToString();
    }
}

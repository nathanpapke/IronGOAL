using System.Text;
using IronScheme;

namespace IronGOAL.Scripting;

/// <summary>
/// Accumulates REPL input line-by-line until it contains at least one
/// complete top-level form.
/// </summary>
public sealed class SchemeFormAccumulator
{
    private readonly StringBuilder _buffer = new();
    
    /// <summary>True if a previous <see cref="Feed"/> left an incomplete form buffered.</summary>
    public bool HasPendingInput => _buffer.Length > 0;
    
    /// <summary>
    /// Feed one more line of input. Returns every complete top-level form
    /// now available (possibly more than one, if a single line completed
    /// several forms at once), or an empty list if the accumulated input is
    /// not yet a complete form.
    /// </summary>
    public List<object> Feed(string line)
    {
        _buffer.Append(line).Append('\n');
        
        List<object> forms;
        try
        {
            forms = ReadAll(_buffer.ToString());
        }
        catch (Exception)
        {
            // Incomplete (or unrecoverably invalid - see class remarks).
            // Keep buffering either way.
            return [];
        }
        
        _buffer.Clear();
        return forms;
    }
    
    /// <summary>Discard any buffered input.</summary>
    public void Reset() => _buffer.Clear();
    
    private static List<object> ReadAll(string text)
    {
        object port = "(open-string-input-port {0})".Eval(text);
        var forms = new List<object>();
        
        while (true)
        {
            object datum = "(read {0})".Eval(port);
            
            if ((bool)"(eof-object? {0})".Eval(datum))
                break;
            
            forms.Add(datum);
        }
        
        return forms;
    }
}

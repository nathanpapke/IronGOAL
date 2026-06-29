using System.Text;
using IronScheme;

namespace IronGOAL.Backing;

public static class DatabaseSystem
{
    // =======================================================================
    // HOST DELEGATE
    // =======================================================================
    
    /// <summary>
    /// Signature of the host-provided query handler.
    /// <para>
    /// <paramref name="query"/> is the raw SQL string passed by the script.
    /// The return value must be a <c>string[]</c> where <c>[0]</c> is the
    /// content-type name and <c>[1..N]</c> are the flat field values - or
    /// <c>null</c> to signal failure (causes <c>sql-query</c> to return
    /// <c>#f</c>).
    /// </para>
    /// </summary>
    public delegate string[]? SqlQueryDelegate(string query);
    
    // The host-provided handler, or null if not configured.
    // Null is the correct "not connected" state: sql-query returns #f, just
    // as sqlpipe-query does when its pipe files are absent.
    private static SqlQueryDelegate? _handler;
    
    /// <summary>
    /// Called by <c>Kernel</c> before <c>RegisterAll()</c> to wire the host's
    /// query implementation.  Pass <c>null</c> to disable the surface (all
    /// calls return <c>#f</c>).
    /// </summary>
    public static void Configure(SqlQueryDelegate? handler) => _handler = handler;
    
    // =======================================================================
    // SQL-QUERY
    // =======================================================================
    
    /// <summary>
    /// Executes a SQL query string via the host-provided delegate and returns
    /// a Scheme vector mirroring GOAL's <c>sql-result</c> layout, or
    /// <c>#f</c> on error.
    ///
    /// <para>
    /// Return shape on success:
    /// <code>
    ///   #(content-type-symbol  "val0"  "val1"  ...)
    /// </code>
    /// where <c>content-type-symbol</c> is the interned Scheme symbol
    /// matching the host's <c>result[0]</c> string, and <c>"val0".."valN"</c>
    /// are the remaining host strings verbatim.  An empty result set is
    /// returned as <c>#(error)</c> — matching GOAL's initial
    /// <c>content-type = 'error</c> when the host returns a zero-length
    /// payload.
    /// </para>
    ///
    /// <para>
    /// Returns <c>#f</c> when:
    /// <list type="bullet">
    ///   <item><description>No delegate is configured.</description></item>
    ///   <item><description><paramref name="args"/> is empty or <c>args[0]</c>
    ///     is not a <c>string</c>.</description></item>
    ///   <item><description>The delegate returns <c>null</c>.</description></item>
    ///   <item><description>The delegate throws.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>Scheme: <c>(sql-query query-string)</c></para>
    /// </summary>
    public static object SqlQuery(object[] args)
    {
        // Guard: no delegate configured.
        if (_handler is null)
            return "#f".Eval();
        
        // Guard: first argument must be a string (IronScheme boxing safety).
        if (args.Length == 0 || args[0] is not string query)
            return "#f".Eval();
        
        // Invoke the host handler; treat any exception as a pipe-open failure
        // (sqlpipe-query returned #f on file-open error).
        string[]? result;
        try
        {
            result = _handler(query);
        }
        catch (Exception)
        {
            return "#f".Eval();
        }
        
        // Null return means the host signals failure.
        if (result is null)
            return "#f".Eval();
        
        // Empty result: return #(error) mirroring GOAL's default content-type.
        if (result.Length == 0)
            return BuildVector("error", Array.Empty<string>());
        
        // result[0] = content-type name; result[1..N] = field values.
        string contentTypeName = result[0];
        string[] fieldValues   = result.Length > 1
            ? result[1..]
            : Array.Empty<string>();
        
        return BuildVector(contentTypeName, fieldValues);
    }
    
    // =======================================================================
    // PRIVATE HELPERS
    // =======================================================================
    
    /// <summary>
    /// Builds a Scheme vector whose first element is the content-type symbol
    /// and whose remaining elements are the field-value strings.
    /// Mirrors GOAL's <c>sql-result::print</c> layout: <c>#(content-type val0 val1 ...)</c>.
    /// <para>
    /// Constructed entirely via <c>.Eval()</c> on IronScheme built-ins
    /// (<c>make-vector</c>, <c>vector-set!</c>, <c>string->symbol</c>) —
    /// no IronScheme internal APIs are used.
    /// </para>
    /// </summary>
    private static object BuildVector(string contentTypeName, string[] fieldValues)
    {
        int n = 1 + fieldValues.Length; // slot 0 = symbol, slots 1..N = strings
        
        // Allocate the vector.
        object vec = $"(make-vector {n})".Eval();
        
        // Slot 0: content-type as a Scheme symbol via string->symbol.
        // EscapeSchemeString ensures the name survives round-trip through Eval.
        $"(vector-set! {{0}} 0 (string->symbol {EscapeSchemeString(contentTypeName)}))".Eval(vec);
        
        // Slots 1..N: field values as Scheme strings.
        for (int i = 0; i < fieldValues.Length; i++)
            $"(vector-set! {{0}} {i + 1} {EscapeSchemeString(fieldValues[i])})".Eval(vec);
        
        return vec;
    }
    
    /// <summary>
    /// Wraps a CLR string as a Scheme string literal safe for embedding
    /// in an <c>.Eval()</c> expression.  Escapes backslashes and double
    /// quotes; no other characters need escaping in IronScheme string literals.
    /// </summary>
    private static string EscapeSchemeString(string value)
    {
        // Escape \ first so subsequent replacements don't double-escape.
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                default:   sb.Append(c);      break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

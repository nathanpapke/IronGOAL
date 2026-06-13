using IronScheme;

namespace IronGOAL.Scripting;

public sealed class ScriptLoader
{
    private readonly object _environment;
    
    /// <param name="environment">
    /// The IronScheme environment to <c>eval</c> forms.
    /// </param>
    public ScriptLoader(object environment)
    {
        _environment = environment;
    }
    
    // =======================================================================
    // PER-FORM EVALUATION
    // =======================================================================
    
    /// <summary>
    /// Evaluate one already-read datum: <c>(eval form.Datum environment)</c>,
    /// with any <see cref="Exception"/> captured into
    /// <see cref="FormResult.ErrorMessage"/> rather than thrown.  Never
    /// throws.
    /// </summary>
    public FormResult EvaluateForm(SchemeForm form)
    {
        try
        {
            object value = "(eval {0} {1})".Eval(form.Datum!, _environment);
            return FormResult.Okay(form, value);
        }
        catch (Exception ex)
        {
            return FormResult.Failed(form, FormatException(ex));
        }
    }
    
    /// <summary>
    /// Read exactly one top-level form from <paramref name="expression"/>
    /// and evaluate it - the primitive behind <c>Host.EvaluateForm</c> /
    /// <c>Kernel.EvaluateForm</c> for REPL-style single-form input.
    ///
    /// <para>
    /// If <paramref name="expression"/> contains more than one form, only
    /// the first is read and evaluated; the rest is ignored. If it contains
    /// no form (empty or comments-only), or <c>read</c> rejects it as
    /// invalid, returns a failed <see cref="FormResult"/> with
    /// <see cref="SchemeForm.Index"/> <c>== 0</c>.
    /// </para>
    /// </summary>
    public FormResult EvaluateExpression(string expression)
    {
        object datum;
        
        try
        {
            object port = "(open-string-input-port {0})".Eval(expression);
            datum = "(read {0})".Eval(port);
            
            if (IsEof(datum))
                return FormResult.Failed(new SchemeForm(null, 0, Preview(expression)),
                    "No expression to evaluate.");
        }
        catch (Exception ex)
        {
            return FormResult.Failed(new SchemeForm(null, 0, Preview(expression)),
                $"Syntax error: {FormatException(ex)}");
        }
        
        var form = new SchemeForm(datum, 0, Preview(datum));
        return EvaluateForm(form);
    }
    
    // =======================================================================
    // FILE LOADING
    // =======================================================================
    
    /// <summary>
    /// Read every top-level form out of <paramref name="source"/> and
    /// evaluate each one in turn via <see cref="EvaluateForm"/>.  Every form
    /// that was successfully read is evaluated regardless of earlier
    /// failures - see the class remarks on hot-reload.
    /// </summary>
    /// <param name="source">
    /// Scheme source code.
    /// </param>
    /// <param name="sourceName">
    /// File path used only for diagnostic messages.
    /// </param>
    /// <returns></returns>
    public ScriptLoadReport LoadSource(string source, string sourceName)
    {
        List<object> datums;
        
        try
        {
            object port = "(open-string-input-port {0})".Eval(source);
            datums = ReadAll(port);
        }
        catch (Exception ex)
        {
            var pseudoForm = new SchemeForm(null, 0, Preview(source));
            var failure = FormResult.Failed(pseudoForm,
                $"Syntax error in '{sourceName}': {FormatException(ex)}");
            return new ScriptLoadReport(sourceName, [failure]);
        }
        
        var results = new List<FormResult>(datums.Count);
        
        for (int i = 0; i < datums.Count; i++)
        {
            var form = new SchemeForm(datums[i], i + 1, Preview(datums[i]));
            results.Add(EvaluateForm(form));
        }
        
        return new ScriptLoadReport(sourceName, results);
    }
    
    /// <summary>
    /// Read and load a <c>.gc</c> (or other Scheme source) file.
    /// </summary>
    /// <param name="path">
    /// File path of source file.
    /// </param>
    /// <returns></returns>
    public GoalResult LoadFile(string path)
    {
        string source;
        
        try
        {
            source = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            return GoalResult.Fail(GoalErrorCode.ScriptReadFailed,
                $"Cannot read '{path}': {ex.Message}");
        }
        
        var report = LoadSource(source, path);
        
        if (report.IsSuccess)
            return GoalResult.Okay;
        
        bool syntaxError = report.Forms.Count == 1 && report.Forms[0].Form.Index == 0;
        var  code        = syntaxError ? GoalErrorCode.ScriptSyntaxError
            : GoalErrorCode.ScriptEvalFailed;
        
        return GoalResult.Fail(code, report.Summary());
    }
    
    // =======================================================================
    // PRIVATE HELPERS
    // =======================================================================
    
    /// <summary>
    /// Read every remaining datum from <paramref name="port"/> until
    /// <c>eof-object?</c>. If the underlying source is not well-formed,
    /// IronScheme's reader raises on the <em>first</em> call.
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    private static List<object> ReadAll(object port)
    {
        var forms = new List<object>();
        
        while (true)
        {
            object datum = "(read {0})".Eval(port);
            
            if (IsEof(datum))
                break;
            
            forms.Add(datum);
        }
        
        return forms;
    }
    
    private static bool IsEof(object datum) =>
        (bool)"(eof-object? {0})".Eval(datum);
    
    /// <summary>
    /// Render <paramref name="datum"/> via Scheme <c>write</c>, then
    /// whitespace-collapse and truncate for use as a one-line error preview.
    /// </summary>
    /// <param name="datum"></param>
    /// <returns></returns>
    private static string Preview(object datum)
    {
        try
        {
            object stringPort = "(open-output-string)".Eval();
            "(write {0} {1})".Eval(datum, stringPort);
            string text = (string)"(get-output-string {0})".Eval(stringPort);
            return CollapseAndTruncate(text);
        }
        catch
        {
            return "<unprintable>";
        }
    }
    
    private static string CollapseAndTruncate(string text, int maxLength = 60)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string oneLine = string.Join(' ', parts);
        
        return oneLine.Length <= maxLength
            ? oneLine
            : oneLine[..maxLength] + "…";
    }
    
    /// <summary>
    /// Render an exception the same way <c>Kernel.Evaluate</c> and
    /// <c>Kernel.LoadFile</c> already do - <c>ex.ToString()</c> with
    /// <c>\r\n</c> normalization (see <c>IronScheme-Output-Issues.md</c>,
    /// "Fix A").  For a <see cref="IronScheme.Runtime.SchemeException"/>,
    /// <c>ToString()</c> renders the Scheme condition via <c>display</c>.
    /// </summary>
    /// <param name="ex">Exception to return as a formatted message.</param>
    /// <returns></returns>
    private static string FormatException(Exception ex) =>
        ex.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
}

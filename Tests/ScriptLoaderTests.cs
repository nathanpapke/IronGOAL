using IronGOAL;
using IronGOAL.Scripting;

namespace Tests;

public class ScriptLoaderTests
{
    static readonly GoalRuntimeConfig Config = new GoalRuntimeConfig
    {
        GlobalHeapSize        = 16 * 1024 * 1024,
        StackHeapSize         =  2 * 1024 * 1024,
        RenderChannelCapacity = 64,
        EnableMemoryTracking  = false,
        EnableDebugChannel    = false,
        LogHandler            = (_, _, _) => { }
    };
    
    private readonly Host         _host;
    private readonly ScriptLoader _loader;
    
    public ScriptLoaderTests()
    {
        var result = Host.Create(Config);
        Assert.True(result.IsSuccess, $"Host.Create failed: {result.ErrorMessage}");
        _host = result.Value!;
        
        // ScriptLoader is public and stateless, so it can be exercised
        // directly here for white-box testing alongside the Host/Kernel
        // surface that wraps it for real callers.
        _loader = new ScriptLoader(_host.SchemeEnvironment);
    }
    
    // =======================================================================
    // CONTINUE-ON-ERROR
    // =======================================================================
    
    [Fact]
    public void LoadSource_OneFormErrors_OthersStillSucceed()
    {
        string source =
            "(define sl-cont-a 1)\n" +
            "(sl-cont-undefined-fn 1 2 3)\n" +
            "(define sl-cont-b 2)\n";
        
        var report = _loader.LoadSource(source, "<test>");
        
        Assert.False(report.IsSuccess);
        Assert.Equal(3, report.Forms.Count);
        
        Assert.True(report.Forms[0].Success);
        Assert.False(report.Forms[1].Success);
        Assert.True(report.Forms[2].Success);
        
        Assert.Equal(2, report.Forms[1].Form.Index);
        Assert.False(string.IsNullOrWhiteSpace(report.Forms[1].ErrorMessage));
        Assert.Contains("sl-cont-undefined-fn", report.Forms[1].Form.Preview);
        
        // Forms before AND after the failure actually ran and created
        // their global bindings.
        var a = _loader.EvaluateExpression("sl-cont-a");
        var b = _loader.EvaluateExpression("sl-cont-b");
        
        Assert.True(a.Success, a.ErrorMessage);
        Assert.Equal(1L, Convert.ToInt64(a.Value));
        Assert.True(b.Success, b.ErrorMessage);
        Assert.Equal(2L, Convert.ToInt64(b.Value));
    }
    
    [Fact]
    public void LoadSource_SyntaxError_ReturnsIndexZeroPseudoForm()
    {
        // Forms 1 and 2 are individually well-formed, but the stray ')' on
        // line 3 makes the source as a whole unreadable - IronScheme's
        // reader rejects the whole port on its first `read` call, so no
        // forms (not even 1 and 2) come back.
        string source =
            "(define sl-syntax-a 1)\n" +
            "(define sl-syntax-b (+ 1 2))\n" +
            ")";
        
        var report = _loader.LoadSource(source, "test.gc");
        
        Assert.False(report.IsSuccess);
        Assert.Single(report.Forms);
        Assert.Equal(0, report.Forms[0].Form.Index);
        Assert.Null(report.Forms[0].Form.Datum);
        Assert.Contains("test.gc", report.Summary());
    }
    
    // =======================================================================
    // eval, not guard - defines and macros land in the shared environment
    // =======================================================================
    
    [Fact]
    public void LoadSource_DefineCreatesGlobalBinding_UsableInLaterCall()
    {
        var report1 = _loader.LoadSource(
            "(define (sl-global-double x) (* x 2))", "<test-1>");
        Assert.True(report1.IsSuccess, report1.Summary());
        
        // A *separate* LoadSource call references the function defined by
        // the first - exactly what cross-file references (e.g. boot.gc
        // calling into action-*.gc) depend on.
        var report2 = _loader.LoadSource("(sl-global-double 21)", "<test-2>");
        Assert.True(report2.IsSuccess, report2.Summary());
        Assert.Equal(42L, Convert.ToInt64(report2.Forms[0].Value));
    }
    
    [Fact]
    public void LoadSource_DefineSyntaxThenUse_MacroVisibleInLaterForm()
    {
        // Each top-level form is eval'd separately - this confirms a macro
        // defined by one eval call is visible to the next eval call on the
        // same environment, the same way it would be at a REPL. GOAL's .gc
        // files lean heavily on macros (deftype, defmacro, etc.), so this
        // is the key cross-form guarantee the whole design rests on.
        string source =
            "(define-syntax sl-macro-twice (syntax-rules () ((_ x) (+ x x))))\n" +
            "(sl-macro-twice 21)\n";
        
        var report = _loader.LoadSource(source, "<test>");
        
        Assert.True(report.IsSuccess, report.Summary());
        Assert.Equal(42L, Convert.ToInt64(report.Forms[1].Value));
    }
    
    [Fact]
    public void EvaluateExpression_BeginWithDefines_CreatesGlobalBindings()
    {
        var beginResult = _loader.EvaluateExpression(
            "(begin (define sl-begin-x 10) (define sl-begin-y 20))");
        Assert.True(beginResult.Success, beginResult.ErrorMessage);
        
        var sum = _loader.EvaluateExpression("(+ sl-begin-x sl-begin-y)");
        Assert.True(sum.Success, sum.ErrorMessage);
        Assert.Equal(30L, Convert.ToInt64(sum.Value));
    }
    
    // =======================================================================
    // EvaluateExpression - REPL-style single-form input
    // =======================================================================
    
    [Fact]
    public void EvaluateExpression_SimpleArithmetic_ReturnsValue()
    {
        var result = _loader.EvaluateExpression("(+ 40 2)");
        
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(42L, Convert.ToInt64(result.Value));
    }
    
    [Fact]
    public void EvaluateExpression_DefineAtTopLevel_CreatesGlobalBinding()
    {
        var defineResult = _loader.EvaluateExpression("(define sl-repl-x 99)");
        Assert.True(defineResult.Success, defineResult.ErrorMessage);
        
        var readResult = _loader.EvaluateExpression("sl-repl-x");
        Assert.True(readResult.Success, readResult.ErrorMessage);
        Assert.Equal(99L, Convert.ToInt64(readResult.Value));
    }
    
    [Fact]
    public void EvaluateExpression_ErroringExpression_ReturnsFailedWithMessage()
    {
        var result = _loader.EvaluateExpression("(car '())");
        
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }
    
    [Fact]
    public void EvaluateExpression_EmptyInput_ReturnsFailed()
    {
        var result = _loader.EvaluateExpression("   ");
        
        Assert.False(result.Success);
        Assert.Equal(0, result.Form.Index);
        Assert.Null(result.Form.Datum);
    }
    
    [Fact]
    public void EvaluateExpression_MultipleForms_OnlyEvaluatesFirst()
    {
        var result = _loader.EvaluateExpression("(define sl-multi-a 1) (define sl-multi-b 2)");
        Assert.True(result.Success, result.ErrorMessage);
        
        var a = _loader.EvaluateExpression("sl-multi-a");
        Assert.True(a.Success, a.ErrorMessage);
        Assert.Equal(1L, Convert.ToInt64(a.Value));
        
        // sl-multi-b was on the second form, which was never read/evaluated.
        var b = _loader.EvaluateExpression("sl-multi-b");
        Assert.False(b.Success);
    }
    
    // =======================================================================
    // LoadFile
    // =======================================================================
    
    [Fact]
    public void LoadFile_ValidFile_LoadsAndDefinesGlobally()
    {
        string path = Path.Combine(Path.GetTempPath(), $"irongoal-sl-{Guid.NewGuid():N}.gc");
        File.WriteAllText(path,
            "(define sl-file-val 7)\n" +
            "(define sl-file-doubled (* sl-file-val 2))\n");
        
        try
        {
            var result = _loader.LoadFile(path);
            Assert.True(result.IsSuccess, result.ErrorMessage);
            
            var read = _loader.EvaluateExpression("sl-file-doubled");
            Assert.True(read.Success, read.ErrorMessage);
            Assert.Equal(14L, Convert.ToInt64(read.Value));
        }
        finally
        {
            File.Delete(path);
        }
    }
    
    [Fact]
    public void LoadFile_MissingFile_ReturnsScriptReadFailed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"irongoal-sl-missing-{Guid.NewGuid():N}.gc");
        
        var result = _loader.LoadFile(path);
        
        Assert.True(result.IsFailure);
        Assert.Equal(GoalErrorCode.ScriptReadFailed, result.ErrorCode);
    }
    
    // =======================================================================
    // Host.LoadScript - result-propagation fix
    // (previously discarded Kernel.LoadFile's GoalResult and always
    //  returned GoalResult.Okay)
    // =======================================================================
    
    [Fact]
    public void HostLoadScript_FileWithEvalError_ReturnsFailure()
    {
        string path = Path.Combine(Path.GetTempPath(), $"irongoal-ls-err-{Guid.NewGuid():N}.gc");
        File.WriteAllText(path,
            "(define sl-ls-ok 1)\n" +
            "(sl-ls-totally-undefined-fn)\n");
        
        try
        {
            var result = _host.LoadScript(path);
            
            Assert.True(result.IsFailure);
            Assert.Equal(GoalErrorCode.ScriptEvalFailed, result.ErrorCode);
            Assert.Contains("sl-ls-totally-undefined-fn", result.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }
    
    [Fact]
    public void HostLoadScript_ValidFile_ReturnsSuccess()
    {
        string path = Path.Combine(Path.GetTempPath(), $"irongoal-ls-ok-{Guid.NewGuid():N}.gc");
        File.WriteAllText(path, "(define sl-ls-good 42)\n");
        
        try
        {
            var result = _host.LoadScript(path);
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }
    
    // =======================================================================
    // Host.EvaluateForm
    // =======================================================================
    
    [Fact]
    public void HostEvaluateForm_SimpleExpression_ReturnsValue()
    {
        var result = _host.EvaluateForm("(+ 40 2)");
        
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(42L, Convert.ToInt64(result.Value));
    }
    
    [Fact]
    public void HostEvaluateForm_DefineThenReference_PersistsGlobally()
    {
        var defineResult = _host.EvaluateForm("(define sl-host-repl-y 5)");
        Assert.True(defineResult.Success, defineResult.ErrorMessage);
        
        var readResult = _host.EvaluateForm("(* sl-host-repl-y 10)");
        Assert.True(readResult.Success, readResult.ErrorMessage);
        Assert.Equal(50L, Convert.ToInt64(readResult.Value));
    }
    
    [Fact]
    public void HostEvaluateForm_ErroringExpression_ReturnsFailureAndLogs()
    {
        GoalErrorCode loggedCode = GoalErrorCode.None;
        var config = new GoalRuntimeConfig
        {
            GlobalHeapSize        = 16 * 1024 * 1024,
            StackHeapSize         =  2 * 1024 * 1024,
            RenderChannelCapacity = 64,
            EnableMemoryTracking  = false,
            EnableDebugChannel    = false,
            LogHandler            = (sev, code, _) =>
            {
                if (sev == GoalLogSeverity.Error)
                    loggedCode = code;
            }
        };
        
        var result = Host.Create(config);
        Assert.True(result.IsSuccess, $"Host.Create failed: {result.ErrorMessage}");
        var host = result.Value!;
        
        var formResult = host.EvaluateForm("(car '())");
        
        Assert.False(formResult.Success);
        Assert.Equal(GoalErrorCode.EvalFailed, loggedCode);
    }
    
    [Fact]
    public void HostEvaluateForm_EmptyExpression_ReturnsFailure()
    {
        var result = _host.EvaluateForm("   ");
        
        Assert.False(result.Success);
    }
    
    [Fact]
    public void HostEvaluateForm_Disposed_ReturnsFailure()
    {
        var config = new GoalRuntimeConfig
        {
            GlobalHeapSize        = 16 * 1024 * 1024,
            StackHeapSize         =  2 * 1024 * 1024,
            RenderChannelCapacity = 64,
            EnableMemoryTracking  = false,
            EnableDebugChannel    = false,
            LogHandler            = (_, _, _) => { }
        };
        
        var result = Host.Create(config);
        Assert.True(result.IsSuccess, $"Host.Create failed: {result.ErrorMessage}");
        var host = result.Value!;
        host.Dispose();
        
        var formResult = host.EvaluateForm("(+ 1 2)");
        
        Assert.False(formResult.Success);
    }
}

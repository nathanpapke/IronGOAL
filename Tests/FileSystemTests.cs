using IronScheme;
using IronScheme.Runtime;
using Xunit;

using IronGOAL;
using IronGOAL.Backing;
using IronGOAL.Bus;

namespace Tests;

public class FileSystemTests
{
    private static readonly Host _host;
    
    static FileSystemTests()
    {
        var result = Host.Create(new GoalRuntimeConfig
        {
            LogHandler = (_, _, _) => { },
        });
        _host = result.Value;
    }
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object result)
        => "(equal? #t {0})".Replace("{0}", result?.ToString() ?? "#f")
               .Eval() is bool b && b
           // IronScheme returns the boolean object directly for #t expressions
           || result is bool rb && rb;
 
    private static object Eval(string expr)
        => _host.Evaluate(expr);
    
    // =======================================================================
    // MC CHECK RESULT
    // =======================================================================
    
    [Fact]
    public void McCheckResult_BeforeAnyOperation_ReturnsOk()
    {
        // _lastResult is initialised to 1 (McStatusCode::OK).
        // Verify via C# backing method directly.
        var result = FileSystem.McCheckResult(Array.Empty<object>());
        Assert.Equal(1L, result);
    }
    
    [Fact]
    public void McCheckResult_SchemeCall_ReturnsInteger()
    {
        // Scheme path: symbol must be registered and callable.
        var result = Eval("(mc-check-result)");
        // IronScheme returns long for integer results.
        Assert.IsType<long>(result);
    }
    
    // =======================================================================
    // MC FORMAT
    // =======================================================================
    
    [Fact]
    public void McFormat_ReturnsTrueViaBackingMethod()
    {
        var result = FileSystem.McFormat(new object[] { 0L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McFormat_SchemeCall_ReturnsTrueSymbol()
    {
        var result = Eval("(mc-format 0)");
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McFormat_NoArgs_DoesNotThrow()
    {
        // Default card-idx of 0 should be used gracefully.
        var ex = Record.Exception(() => FileSystem.McFormat(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    // =======================================================================
    // MC UNFORMAT
    // =======================================================================
    
    [Fact]
    public void McUnformat_ReturnsTrueViaBackingMethod()
    {
        var result = FileSystem.McUnformat(new object[] { 0L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McUnformat_SchemeCall_ReturnsTrueSymbol()
    {
        var result = Eval("(mc-unformat 0)");
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // MC CREATE FILE
    // =======================================================================
    
    [Fact]
    public void McCreateFile_ReturnsTrueViaBackingMethod()
    {
        // args[1] = data ptr is ignored; pass a dummy value.
        var result = FileSystem.McCreateFile(new object[] { 0L, 0L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McCreateFile_SchemeCall_ReturnsTrueSymbol()
    {
        var result = Eval("(mc-createfile 0 0)");
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // MC MAKE FILE
    // =======================================================================
    
    [Fact]
    public void McMakeFile_ReturnsTrueViaBackingMethod()
    {
        var result = FileSystem.McMakeFile(new object[] { 0L, 4096L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McMakeFile_SchemeCall_ReturnsTrueSymbol()
    {
        var result = Eval("(mc-makefile 0 4096)");
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // MC SAVE
    // =======================================================================
    
    [Fact]
    public void McSave_ReturnsTrueViaBackingMethod()
    {
        // args[2/3] = PS2 pointer args; ignored by the implementation.
        var result = FileSystem.McSave(new object[] { 0L, 0L, 0L, 0L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McSave_SchemeCall_ReturnsTrueSymbol()
    {
        var result = Eval("(mc-save 0 0 0 0)");
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McSave_NoArgs_DoesNotThrow()
    {
        var ex = Record.Exception(() => FileSystem.McSave(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    // =======================================================================
    // MC LOAD
    // =======================================================================
    
    [Fact]
    public void McLoad_ReturnsTrueViaBackingMethod()
    {
        // args[2] = PS2 pointer arg; ignored.
        var result = FileSystem.McLoad(new object[] { 0L, 0L, 0L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McLoad_SchemeCall_ReturnsTrueSymbol()
    {
        var result = Eval("(mc-load 0 0 0)");
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // MC RUN
    // =======================================================================
    
    [Fact]
    public void McRun_WithNoPendingOp_ReturnsTrueImmediately()
    {
        // Call McRun when nothing has been enqueued.
        // _pendingOpcode is -1 (NO_OP), so the method publishes a no-op
        // notification and returns #t without suspending.
        //
        // Reset any pending state left over from mc-save/mc-load tests
        // by running McRun until the slot is clear.  A single call is
        // sufficient because McRun clears the slot on entry.
        FileSystem.McRun(Array.Empty<object>());   // drain any residual
        var result = FileSystem.McRun(Array.Empty<object>());
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void McRun_SchemeCall_NoPendingOp_ReturnsTrueSymbol()
    {
        // Drain residual pending state, then confirm the Scheme path works.
        Eval("(mc-run)");
        var result = Eval("(mc-run)");
        Assert.True(IsTrue(result));
    }
}

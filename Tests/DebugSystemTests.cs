using IronGOAL;
using IronGOAL.Bus;
using Xunit;

namespace Tests;

public class DebugSystemTests : IDisposable
{
    private readonly Host _host;
    
    private static readonly GoalRuntimeConfig Config = new()
    {
        GlobalHeapSize     = 4 * 1024 * 1024,
        StackHeapSize      = 1 * 1024 * 1024,
        EnableDebugChannel = true,
        LogHandler         = static (_, _, _) => { },
    };
    
    public DebugSystemTests()
    {
        var result = Host.Create(Config);
        Assert.True(result.IsSuccess);
        _host = result.Value!;
    }
    
    public void Dispose() => _host.Dispose();
    
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------
    
    /// Drain all debug commands into a list.
    private List<DebugCommand> DrainDebug()
    {
        var list = new List<DebugCommand>();
        while (_host.DebugCommands.TryRead(out var ts))
            list.Add(ts.Command);
        return list;
    }
    
    // =======================================================================
    // PRINT
    // =======================================================================
    
    [Fact]
    public void Print_ReturnsSameObject()
    {
        // (print 42) must return the argument unchanged.
        var result = _host.Evaluate("(equal? 42 (print 42))");
        Assert.True(result is true);
    }
    
    [Fact]
    public void Print_EmptyArgs_ReturnsNil()
    {
        // (print) with no arguments returns the empty list.
        var result = _host.Evaluate("(print)");
        Assert.NotNull(result);
        // IronScheme represents () as the empty list; ToString is "()"
        Assert.Equal("()", result!.ToString());
    }
    
    [Fact]
    public void Print_PublishesLogCommand()
    {
        _host.Evaluate("(print \"hello\")");
        
        var cmds = DrainDebug();
        Assert.Single(cmds);
        Assert.Equal(DebugCommandType.Log,  cmds[0].Type);
        Assert.Equal("print",               cmds[0].SourceSymbol);
        // SchemeWrite wraps strings in double quotes.
        Assert.Contains("hello",            cmds[0].Message);
    }
    
    [Fact]
    public void Print_Bool_FormatsAsSchemeBoolean()
    {
        _host.Evaluate("(print #t)");
        
        var cmds = DrainDebug();
        Assert.Single(cmds);
        Assert.Equal("#t", cmds[0].Message);
    }
    
    // =======================================================================
    // INSPECT
    // =======================================================================
    
    [Fact]
    public void Inspect_ReturnsSameObject()
    {
        // (inspect 99) must return the argument unchanged.
        var result = _host.Evaluate("(equal? 99 (inspect 99))");
        Assert.True(result is true);
    }
    
    [Fact]
    public void Inspect_EmptyArgs_ReturnsNil()
    {
        var result = _host.Evaluate("(inspect)");
        Assert.Equal("()", result!.ToString());
    }
    
    [Fact]
    public void Inspect_PublishesInspectCommand()
    {
        _host.Evaluate("(inspect 7)");
        
        var cmds = DrainDebug();
        Assert.Single(cmds);
        Assert.Equal(DebugCommandType.Inspect, cmds[0].Type);
        Assert.Equal("inspect",                cmds[0].SourceSymbol);
    }
    
    [Fact]
    public void Inspect_Long_MessageContainsFixnumLabel()
    {
        _host.Evaluate("(inspect 255)");
        
        var cmds = DrainDebug();
        Assert.Single(cmds);
        Assert.Contains("fixnum", cmds[0].Message);
        Assert.Contains("255",    cmds[0].Message);
    }
    
    [Fact]
    public void Inspect_String_MessageContainsStringLiteral()
    {
        _host.Evaluate("(inspect \"world\")");
        
        var cmds = DrainDebug();
        Assert.Single(cmds);
        Assert.Contains("world", cmds[0].Message);
    }
    
    // =======================================================================
    // FORMAT
    // =======================================================================
    
    [Fact]
    public void Format_FalseDestination_ReturnsFormattedString()
    {
        // (_format #f "x=~a" 5) should return the string without publishing.
        var result = _host.Evaluate("(_format #f \"x=~a\" 5)");
        Assert.Equal("x=5", result?.ToString());
    }
    
    [Fact]
    public void Format_FalseDestination_DoesNotPublish()
    {
        _host.Evaluate("(_format #f \"no publish ~a\" 1)");
        
        var cmds = DrainDebug();
        Assert.Empty(cmds);
    }
    
    [Fact]
    public void Format_TrueDestination_PublishesLogCommand()
    {
        _host.Evaluate("(_format #t \"hp=~a\" 100)");
        
        var cmds = DrainDebug();
        Assert.Single(cmds);
        Assert.Equal(DebugCommandType.Log, cmds[0].Type);
        Assert.Equal("_format",            cmds[0].SourceSymbol);
        Assert.Equal("hp=100",             cmds[0].Message);
    }
    
    [Fact]
    public void Format_TrueDestination_ReturnsTrue()
    {
        // (_format #t ...) returns #t (Scheme true).
        var result = _host.Evaluate("(_format #t \"ok\")");
        Assert.True(result is true);
    }
    
    [Fact]
    public void Format_TildePercent_InsertsNewline()
    {
        var result = _host.Evaluate("(_format #f \"a~%b\")");
        Assert.Equal("a\nb", result?.ToString());
    }
    
    [Fact]
    public void Format_TildeS_WritesSchemeForm()
    {
        // ~s uses write-representation: strings are quoted.
        var result = _host.Evaluate("(_format #f \"~s\" \"hi\")");
        Assert.Equal("\"hi\"", result?.ToString());
    }
    
    [Fact]
    public void Format_InsufficientArgs_ReturnsFalse()
    {
        // Fewer than 2 args -> returns #f.
        var result = _host.Evaluate("(_format #t)");
        Assert.True(result is false);
    }
    
    // =======================================================================
    // DISABLED CHANNEL
    // =======================================================================
    
    [Fact]
    public void DisabledChannel_FormatFalse_StillReturnsString()
    {
        // Even when the debug channel is off, (_format #f ...) must still
        // compute and return the string, so ship-build string-building paths work.
        var shipConfig = new GoalRuntimeConfig
        {
            GlobalHeapSize     = 4 * 1024 * 1024,
            StackHeapSize      = 1 * 1024 * 1024,
            EnableDebugChannel = false,
            LogHandler         = static (_, _, _) => { },
        };
        
        var hostResult = Host.Create(shipConfig);
        Assert.True(hostResult.IsSuccess);
        using var shipHost = hostResult.Value!;
        
        var result = shipHost.Evaluate("(_format #f \"val=~a\" 42)");
        Assert.Equal("val=42", result?.ToString());
    }
}

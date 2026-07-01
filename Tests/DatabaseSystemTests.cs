using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public sealed class DatabaseSystemTests : IDisposable
{
    private static readonly Host _host;
    
    static DatabaseSystemTests()
    {
        var config = new GoalRuntimeConfig
        {
            LogHandler      = static (_, _, _) => { },
            SqlQueryHandler = null,   // tests call Configure directly
        };
        
        var result = Host.Create(config);
        Assert.True(result.IsSuccess, $"Console.Create failed: {result.ErrorMessage}");
        _host = result.Value;
    }
    
    // Reset delegate after every test so static state does not leak.
    public void Dispose() => DatabaseSystem.Configure(null);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsFalse(object result)
        => "(eq? {0} #f)".Eval(result) is true;
    
    private static bool IsSymbol(object result, string symbolName)
        => $"(eq? {{0}} '{symbolName})".Eval(result) is true;
    
    // =======================================================================
    // SQL QUERY - C# backing path
    // =======================================================================
    
    [Fact]
    public void SqlQuery_NoDelegate_ReturnsFalse()
    {
        DatabaseSystem.Configure(null);
        
        var result = DatabaseSystem.SqlQuery(["SELECT * FROM races"]);
        
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SqlQuery_EmptyArgs_ReturnsFalse()
    {
        DatabaseSystem.Configure(_ => ["race-info", "turbo-track", "42"]);
        
        var result = DatabaseSystem.SqlQuery([]);
        
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SqlQuery_NonStringArg_ReturnsFalse()
    {
        DatabaseSystem.Configure(_ => ["race-info"]);
        
        // IronScheme boxing rule: pass a string, not a CLR int.
        // Passing a non-string object exercises the type-guard branch.
        var result = DatabaseSystem.SqlQuery([42]);
        
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SqlQuery_DelegateReturnsNull_ReturnsFalse()
    {
        DatabaseSystem.Configure(_ => null);
        
        var result = DatabaseSystem.SqlQuery(["SELECT 1"]);
        
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SqlQuery_DelegateThrows_ReturnsFalse()
    {
        DatabaseSystem.Configure(_ => throw new InvalidOperationException("pipe error"));
        
        var result = DatabaseSystem.SqlQuery(["SELECT 1"]);
        
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SqlQuery_DelegateReturnsEmptyArray_ReturnsErrorVector()
    {
        DatabaseSystem.Configure(_ => []);
        
        var result = DatabaseSystem.SqlQuery(["SELECT 1"]);
        
        // Must be a vector, not #f.
        Assert.True(IsFalse("(not (vector? {0}))".Eval(result)));
        
        // Length must be 1.
        var len = "(vector-length {0})".Eval(result);
        Assert.Equal(1, Convert.ToInt32(len));
        
        // Slot 0 must be the symbol 'error.
        var slot0 = "(vector-ref {0} 0)".Eval(result);
        Assert.True(IsSymbol(slot0, "error"));
    }
    
    [Fact]
    public void SqlQuery_ContentTypeOnly_ReturnsVectorWithSymbolAtSlot0()
    {
        DatabaseSystem.Configure(_ => ["race-info"]);
        
        var result = DatabaseSystem.SqlQuery(["SELECT * FROM race_info"]);
        
        Assert.True(IsFalse("(not (vector? {0}))".Eval(result)));
        
        var len = "(vector-length {0})".Eval(result);
        Assert.Equal(1, Convert.ToInt32(len));
        
        var slot0 = "(vector-ref {0} 0)".Eval(result);
        Assert.True(IsSymbol(slot0, "race-info"));
    }
    
    [Fact]
    public void SqlQuery_ContentTypeAndFields_ReturnsVectorWithSymbolAndStrings()
    {
        DatabaseSystem.Configure(_ => ["race-info", "turbo-track", "42", "3"]);
        
        var result = DatabaseSystem.SqlQuery(["SELECT * FROM race_info WHERE id=1"]);
        
        Assert.True(IsFalse("(not (vector? {0}))".Eval(result)));
        
        // Expect 4 elements: symbol + 3 strings.
        var len = "(vector-length {0})".Eval(result);
        Assert.Equal(4, Convert.ToInt32(len));
        
        // Slot 0: symbol 'race-info.
        var slot0 = "(vector-ref {0} 0)".Eval(result);
        Assert.True(IsSymbol(slot0, "race-info"));
        
        // Slots 1–3: strings verbatim.
        var slot1 = "(vector-ref {0} 1)".Eval(result);
        Assert.Equal("turbo-track", slot1);
        
        var slot2 = "(vector-ref {0} 2)".Eval(result);
        Assert.Equal("42", slot2);
        
        var slot3 = "(vector-ref {0} 3)".Eval(result);
        Assert.Equal("3", slot3);
    }
    
    [Fact]
    public void SqlQuery_FieldsWithSpecialChars_DoNotBreakEval()
    {
        // Backslashes and double quotes in field values must survive the
        // EscapeSchemeString round-trip through .Eval().
        DatabaseSystem.Configure(_ => ["debug-info", "path\\to\\file", "say \"hello\""]);
        
        var result = DatabaseSystem.SqlQuery(["SELECT * FROM debug"]);
        
        Assert.True(IsFalse("(not (vector? {0}))".Eval(result)));
        
        var slot1 = "(vector-ref {0} 1)".Eval(result);
        Assert.Equal("path\\to\\file", slot1);
        
        var slot2 = "(vector-ref {0} 2)".Eval(result);
        Assert.Equal("say \"hello\"", slot2);
    }
    
    // =======================================================================
    // SQL QUERY - Scheme path via (sql-query ...)
    // =======================================================================
    
    [Fact]
    public void SchemeSymbol_SqlQuery_NoDelegate_ReturnsFalse()
    {
        DatabaseSystem.Configure(null);
        
        var result = "(sql-query \"SELECT 1\")".Eval();
        
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SchemeSymbol_SqlQuery_WithDelegate_ReturnsVector()
    {
        DatabaseSystem.Configure(_ => ["race-info", "turbo-track"]);
        
        var result = "(sql-query \"SELECT * FROM race_info\")".Eval();
        
        Assert.True(IsFalse("(not (vector? {0}))".Eval(result)));
        
        var len = "(vector-length {0})".Eval(result);
        Assert.Equal(2, Convert.ToInt32(len));
        
        var slot0 = "(vector-ref {0} 0)".Eval(result);
        Assert.True(IsSymbol(slot0, "race-info"));
        
        var slot1 = "(vector-ref {0} 1)".Eval(result);
        Assert.Equal("turbo-track", slot1);
    }
}

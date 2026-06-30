using IronScheme;
using IronGOAL;
using IronGOAL.Scripting;

namespace Tests;

public class SchemeFormAccumulatorTests
{
    static readonly GoalRuntimeConfig Config = new GoalRuntimeConfig
    {
        GlobalHeapSize        = 16 * 1024 * 1024,
        StackHeapSize         =  2 * 1024 * 1024,
        TransformChannelCapacity = 64,
        EnableMemoryTracking  = false,
        EnableDebugChannel    = false,
        LogHandler            = (_, _, _) => { }
    };
    
    public SchemeFormAccumulatorTests()
    {
        var result = Host.Create(Config);
        Assert.True(result.IsSuccess, $"Host.Create failed: {result.ErrorMessage}");
    }
    
    /// <summary>Render a datum back to text via Scheme <c>write</c>, for assertions.</summary>
    private static string Render(object datum) =>
        (string)"(let ((sfa-sp (open-output-string))) (write {0} sfa-sp) (get-output-string sfa-sp))".Eval(datum);
    
    [Fact]
    public void Feed_CompleteFormOnOneLine_ReturnsItImmediately()
    {
        var accumulator = new SchemeFormAccumulator();
        
        var forms = accumulator.Feed("(define sfa-a 1)");
        
        Assert.Single(forms);
        Assert.Equal("(define sfa-a 1)", Render(forms[0]));
        Assert.False(accumulator.HasPendingInput);
    }
    
    [Fact]
    public void Feed_MultiLineForm_BuffersUntilComplete()
    {
        var accumulator = new SchemeFormAccumulator();
        
        var partial = accumulator.Feed("(define (sfa-b x)");
        Assert.Empty(partial);
        Assert.True(accumulator.HasPendingInput);
        
        var complete = accumulator.Feed("  (* x 2))");
        Assert.Single(complete);
        Assert.False(accumulator.HasPendingInput);
        Assert.Equal("(define (sfa-b x) (* x 2))", Render(complete[0]));
    }
    
    [Fact]
    public void Feed_MultipleLinesBeforeComplete_AccumulatesAll()
    {
        var accumulator = new SchemeFormAccumulator();
        
        Assert.Empty(accumulator.Feed("(begin"));
        Assert.True(accumulator.HasPendingInput);
        
        Assert.Empty(accumulator.Feed("  (define sfa-c 1)"));
        Assert.True(accumulator.HasPendingInput);
        
        Assert.Empty(accumulator.Feed("  (define sfa-d 2)"));
        Assert.True(accumulator.HasPendingInput);
        
        var complete = accumulator.Feed(")");
        Assert.Single(complete);
        Assert.False(accumulator.HasPendingInput);
    }
    
    [Fact]
    public void Feed_TwoCompleteFormsOnOneLine_ReturnsBoth()
    {
        var accumulator = new SchemeFormAccumulator();
        
        var forms = accumulator.Feed("(define sfa-e 1) (define sfa-f 2)");
        
        Assert.Equal(2, forms.Count);
        Assert.Equal("(define sfa-e 1)", Render(forms[0]));
        Assert.Equal("(define sfa-f 2)", Render(forms[1]));
        Assert.False(accumulator.HasPendingInput);
    }
    
    [Fact]
    public void Reset_ClearsPendingInput()
    {
        var accumulator = new SchemeFormAccumulator();
        
        accumulator.Feed("(define (sfa-g x)");
        Assert.True(accumulator.HasPendingInput);
        
        accumulator.Reset();
        Assert.False(accumulator.HasPendingInput);
        
        // The discarded prefix doesn't bleed into later input.
        var forms = accumulator.Feed("(+ 1 2)");
        Assert.Single(forms);
        Assert.Equal("(+ 1 2)", Render(forms[0]));
    }
    
    [Fact]
    public void Feed_StrayCloseBracket_KeepsBufferingUntilReset()
    {
        // Documents the caveat in SchemeFormAccumulator's class remarks:
        // a stray ')' can never become a complete form, but `read` raises
        // the same "&lexical invalid syntax" condition for it as for a
        // genuinely-incomplete prefix, so it is treated as "keep waiting"
        // rather than reported as an error.
        var accumulator = new SchemeFormAccumulator();
        
        var forms = accumulator.Feed(")");
        
        Assert.Empty(forms);
        Assert.True(accumulator.HasPendingInput);
        
        // Still pending after another line - it will never resolve on its own.
        forms = accumulator.Feed("(+ 1 2)");
        Assert.Empty(forms);
        Assert.True(accumulator.HasPendingInput);
        
        accumulator.Reset();
        Assert.False(accumulator.HasPendingInput);
    }
}

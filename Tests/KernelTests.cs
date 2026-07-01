using IronGOAL;

using IronScheme.Runtime;

namespace Tests;

/// <summary>
/// Headless kernel tests - no engine, no graphics, no file I/O beyond script
/// fixtures.  The fixture creates one GoalRuntime per test class; xUnit
/// instantiates a new class instance per test method, so each test gets a
/// fresh runtime.
/// </summary>
public class KernelTests
{
    private readonly Host _runtime;

    public KernelTests()
    {
        var config = new GoalRuntimeConfig
        {
            GlobalHeapSize        = 16 * 1024 * 1024,
            StackHeapSize         =  2 * 1024 * 1024,
            TransformChannelCapacity = 64,
            EnableMemoryTracking  = false,
            EnableDebugChannel    = false,
            LogHandler            = (_, _, _) => { }
        };        
 
        var result = Host.Create(config);
        Assert.True(result.IsSuccess,
            $"GoalRuntime.Create failed: {result.ErrorMessage}");
        _runtime = result.Value!;
    }
    
    // =======================================================================
    // LIFECYCLE - Console.Create config validation
    // =======================================================================
    
    [Fact]
    public void Create_WithNullConfig_ReturnsFailure()
    {
        Assert.True(Host.Create(null!).IsFailure);
    }
    
    [Fact]
    public void Create_WithInvalidHeapSizes_ReturnsFailure()
    {
        var result = Host.Create(new GoalRuntimeConfig
        {
            GlobalHeapSize = 1024,
            StackHeapSize  = 2048,
            LogHandler     = (_, _, _) => { },
        });
        Assert.True(result.IsFailure);
        Assert.Equal(GoalErrorCode.InvalidConfig, result.ErrorCode);
    }
    
    // =======================================================================
    // LIFECYCLE - Tick
    // =======================================================================
    
    [Fact]
    public void Tick_Succeeds() =>
        Assert.True(_runtime.Tick(1f / 60f).IsSuccess);
    
    [Fact]
    public void Tick_ZeroDelta_Succeeds() =>
        Assert.True(_runtime.Tick(0f).IsSuccess);
    
    // =======================================================================
    // EVALUATE - empty / whitespace guard
    // =======================================================================
    
    [Fact]
    public void Evaluate_EmptyString_ReturnsNull() =>
        Assert.Null(_runtime.Evaluate(""));
    
    [Fact]
    public void Evaluate_WhitespaceOnly_ReturnsNull() =>
        Assert.Null(_runtime.Evaluate("   "));
    
    // =======================================================================
    // NATIVE R5RS SCHEME - arithmetic
    // =======================================================================
    
    [Fact]
    public void Scheme_IntegerAddition()
    {
        Assert.Equal(3, _runtime.Evaluate("(+ 1 2)"));
    }
    
    [Fact]
    public void Scheme_FloatAddition()
    {
        Assert.Equal(4.0, _runtime.Evaluate("(+ 1.5 2.5)"));
    }
    
    [Fact]
    public void Scheme_NestedArithmetic()
    {
        Assert.Equal(30, _runtime.Evaluate("(* (+ 2 3) (- 10 4))"));
    }
    
    // =======================================================================
    // NATIVE R5RS SCHEME - boolean and comparison
    // =======================================================================
    
    [Fact]
    public void Scheme_BooleanTrue()
    {
        Assert.Equal(true, _runtime.Evaluate("(= 1 1)"));
    }
    
    [Fact]
    public void Scheme_BooleanFalse()
    {
        Assert.Equal(false, _runtime.Evaluate("(= 1 2)"));
    }
    
    // =======================================================================
    // NATIVE R5RS SCHEME - define and lambda
    // Verifies the interpreter can bind names and close over values -
    // the foundation everything else depends on.
    // =======================================================================
    
    [Fact]
    public void Scheme_DefineAndCall()
    {
        Assert.Equal(49,
            _runtime.Evaluate("(begin (define (square x) (* x x)) (square 7))"));
    }
 
    [Fact]
    public void Scheme_Lambda_Closure()
    {
        Assert.Equal(15,
            _runtime.Evaluate(
                "(let ((adder (lambda (n) (lambda (x) (+ x n))))) ((adder 10) 5))"));
    }
    
    // =======================================================================
    // NATIVE R5RS SCHEME - lists
    // car/cdr/cons/map are all native; prove the list machinery is alive.
    // =======================================================================
    
    [Fact]
    public void Scheme_Map_ReturnsCons()
    {
        Assert.IsType<Cons>(_runtime.Evaluate("(map (lambda (x) (* x x)) '(1 2 3 4))"));
    }
 
    [Fact]
    public void Scheme_Car_ReturnsInt()
    {
        Assert.Equal(42, _runtime.Evaluate("(car (cons 42 '()))"));
    }
 
    [Fact]
    public void Scheme_EmptyList_ReturnsNull()
    {
        Assert.Null(_runtime.Evaluate("'()"));
    }
    
    // =======================================================================
    // NATIVE R6RS SCHEME - strings and symbols
    // =======================================================================
    
    [Fact]
    public void Scheme_StringAppend_ReturnsCLRString()
    {
        Assert.Equal("hello world",
            _runtime.Evaluate("(string-append \"hello\" \" \" \"world\")"));
    }
    
    [Fact]
    public void Scheme_SymbolToString_ReturnsCLRString()
    {
        Assert.Equal("goal", _runtime.Evaluate("(symbol->string 'goal)"));
    }
    
    // =======================================================================
    // NATIVE R6RS SCHEME - tail-call / recursion
    // Confirms the interpreter handles deep recursion without stack overflow
    // (important for GOAL-style state loops).
    // =======================================================================
    
    [Fact]
    public void Scheme_TailRecursion_DoesNotOverflow()
    {
        Assert.Equal(499500,
            _runtime.Evaluate(@"
                (let loop ((n 999) (acc 0))
                  (if (= n 0) acc (loop (- n 1) (+ acc n))))"));
    }
}
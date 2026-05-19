using IronGOAL;

namespace Tests;

/// <summary>
/// Headless kernel tests — no engine, no graphics, no file I/O beyond script
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
            RenderChannelCapacity = 64,
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
    // ARITHMETIC SANITY
    // =======================================================================
    
    [Fact]
    public void Scheme_EvalArithmetic_ReturnsCorrectValue()
    {
        var result = _runtime.Evaluate("(+ 1 2)");
        Assert.True(result.IsSuccess);
        Assert.Equal("3", result.Value);
    }
    
    
    // =======================================================================
    // VECTOR MATH — vector+
    // =======================================================================
    
    [Fact]
    public void VectorAdd_ProducesCorrectSum()
    {
        // (vector+ '(1 2 3) '(4 5 6)) should equal (5 7 9)
        // We verify by asking Scheme itself: (equal? result '(5.0 7.0 9.0))
        var result = _runtime.Evaluate("(equal? (vector+ '(1.0 2.0 3.0) '(4.0 5.0 6.0)) '(5.0 7.0 9.0))");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("#t", result.Value);
    }
    
    // =======================================================================
    // VECTOR MATH — vector-
    // =======================================================================
    
    [Fact]
    public void VectorSub_ProducesCorrectDifference()
    {
        var result = _runtime.Evaluate("(equal? (vector- '(10.0 20.0 30.0) '(1.0 2.0 3.0)) '(9.0 18.0 27.0))");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("#t", result.Value);
    }
    
    // =======================================================================
    // VECTOR MATH — vector-dot
    // =======================================================================
    
    [Fact]
    public void VectorDot_OrthogonalVectors_IsZero()
    {
        var result = _runtime.Evaluate("(= (vector-dot '(1.0 0.0) '(0.0 1.0)) 0.0)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("#t", result.Value);
    }
    
    [Fact]
    public void VectorDot_ParallelVectors_IsSquaredLength()
    {
        var result = _runtime.Evaluate("(= (vector-dot '(3.0 4.0) '(3.0 4.0)) 25.0)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("#t", result.Value);
    }
    
    // =======================================================================
    // VECTOR MATH — vector-length
    // =======================================================================
    
    [Fact]
    public void VectorLength_ThreeFourVector_IsFive()
    {
        var result = _runtime.Evaluate("(= (vector-length '(3.0 4.0)) 5.0)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("#t", result.Value);
    }
    
    // =======================================================================
    // VECTOR MATH — vector-normalize
    // =======================================================================
    
    [Fact]
    public void VectorNormalize_ZeroVector_IsAllZero()
    {
        var result = _runtime.Evaluate("(equal? (vector-normalize '(0.0 0.0 0.0)) '(0.0 0.0 0.0))");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("#t", result.Value);
    }
    
    // =======================================================================
    // VECTOR MATH — vector-cross
    // =======================================================================
    
    [Fact]
    public void VectorCross_XcrossY_IsZ()
    {
        // X × Y = Z = (0 0 1)
        var result = _runtime.Evaluate("(equal? (vector-cross '(1.0 0.0 0.0) '(0.0 1.0 0.0)) '(0.0 0.0 1.0))");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("#t", result.Value);
    }
    
    // =======================================================================
    // RUNTIME LIFECYCLE
    // =======================================================================
    
    [Fact]
    public void Tick_DoesNotError()
    {
        var result = _runtime.Tick(1f / 60f);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }
    
    [Fact]
    public void Evaluate_EmptyExpression_ReturnsFailure()
    {
        var result = _runtime.Evaluate("   ");
        Assert.True(result.IsFailure);
    }
    
    [Fact]
    public void GoalRuntime_Create_WithNullConfig_ReturnsFailure()
    {
        var result = Host.Create(null!);
        Assert.True(result.IsFailure);
    }
    
    [Fact]
    public void GoalRuntime_Create_WithInvalidHeapSizes_ReturnsFailure()
    {
        var config = new GoalRuntimeConfig
        {
            GlobalHeapSize = 1024,
            StackHeapSize  = 2048,   // larger than global - invalid
            LogHandler     = (_, _, _) => { },
        };
        var result = Host.Create(config);
        Assert.True(result.IsFailure);
        Assert.Equal(GoalErrorCode.InvalidConfig, result.ErrorCode);
    }
}
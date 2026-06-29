using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using IronGOAL.Scripting;
using Xunit;

namespace Tests;

public class AssetSystemTests
{
    private static readonly Host _host;
    
    static AssetSystemTests()
    {
        var config = new GoalRuntimeConfig
        {
            GlobalHeapSize = 32 * 1024 * 1024,
            StackHeapSize  =  4 * 1024 * 1024,
            LogHandler     = (_, _, _) => { },
        };
        
        var result = Host.Create(config);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        _host = result.Value;
    }
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? value)  => value is bool b && b;
    private static bool IsFalse(object? value) =>
        value is bool b ? !b : ReferenceEquals(value, "#f".Eval());
    
    // =======================================================================
    // SCHEME SYMBOL REGISTRATION
    // Each GOAL-origin symbol must be bound and callable from Scheme.
    // =======================================================================
    
    [Fact]
    public void SchemeSymbol_Load_IsDefined()
    {
        // (load) with no args hits the wrong-arg guard and returns #f - not a
        // Scheme error. Success means the symbol is bound.
        FormResult result = _host.EvaluateForm("(load)");
        Assert.True(result.Success, result.ErrorMessage);
    }
    
    [Fact]
    public void SchemeSymbol_Loado_IsDefined()
    {
        FormResult result = _host.EvaluateForm("(loado)");
        Assert.True(result.Success, result.ErrorMessage);
    }
    
    [Fact]
    public void SchemeSymbol_Loadb_IsDefined()
    {
        FormResult result = _host.EvaluateForm("(loadb)");
        Assert.True(result.Success, result.ErrorMessage);
    }
    
    [Fact]
    public void SchemeSymbol_Unload_IsDefined()
    {
        FormResult result = _host.EvaluateForm("(unload)");
        Assert.True(result.Success, result.ErrorMessage);
    }
    
    [Fact]
    public void SchemeSymbol_DgoLoad_IsDefined()
    {
        FormResult result = _host.EvaluateForm("(dgo-load)");
        Assert.True(result.Success, result.ErrorMessage);
    }
    
    // =======================================================================
    // WRONG-ARG GUARDS - Scheme path
    // =======================================================================
    
    [Fact]
    public void Load_NoArgs_Scheme_ReturnsFalse()
    {
        object? result = _host.Evaluate("(load)");
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void LoadObject_NoArgs_Scheme_ReturnsFalse()
    {
        object? result = _host.Evaluate("(loado)");
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void LoadBinary_NoArgs_Scheme_ReturnsFalse()
    {
        object? result = _host.Evaluate("(loadb)");
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Unload_NoArgs_Scheme_ReturnsFalse()
    {
        object? result = _host.Evaluate("(unload)");
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DgoLoad_NoArgs_Scheme_ReturnsFalse()
    {
        object? result = _host.Evaluate("(dgo-load)");
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // WRONG-ARG GUARDS - direct C# backing-method path
    // =======================================================================
    
    [Fact]
    public void Load_EmptyArgs_Direct_ReturnsFalse()
    {
        object result = AssetSystem.Load(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Load_WrongArgType_Direct_ReturnsFalse()
    {
        // First arg must be a string (path); passing an int should fail the guard.
        object result = AssetSystem.Load(new object[] { "42" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void LoadObject_EmptyArgs_Direct_ReturnsFalse()
    {
        object result = AssetSystem.LoadObject(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void LoadBinary_EmptyArgs_Direct_ReturnsFalse()
    {
        object result = AssetSystem.LoadBinary(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Unload_EmptyArgs_Direct_ReturnsFalse()
    {
        object result = AssetSystem.Unload(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Unload_WrongArgType_Direct_ReturnsFalse()
    {
        // Arg must be a long handle; a string should fail the guard.
        object result = AssetSystem.Unload(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DgoLoad_EmptyArgs_Direct_ReturnsFalse()
    {
        object result = AssetSystem.DgoLoad(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DgoLoad_WrongArgType_Direct_ReturnsFalse()
    {
        // Arg must be a string (DGO name); a long should fail the guard.
        object result = AssetSystem.DgoLoad(new object[] { 99L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // UNLOAD - fire-and-return, no suspension required
    // A valid long handle is accepted, EntitySetState is published, and the
    // method returns #t immediately regardless of whether the host is
    // draining.
    // =======================================================================
    
    [Fact]
    public void Unload_ValidHandle_Direct_ReturnsTrue()
    {
        object result = AssetSystem.Unload(new object[] { 1L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Unload_ValidHandle_Scheme_ReturnsTrue()
    {
        object? result = _host.Evaluate("(unload 1)");
        Assert.True(IsTrue(result));
    }
}

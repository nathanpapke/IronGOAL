using System;
using System.Numerics;
using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class EntitySystemTests
{
    // =======================================================================
    // BOOT
    // =======================================================================
    
    static readonly GoalRuntimeConfig Config = new GoalRuntimeConfig
    {
        GlobalHeapSize        = 16 * 1024 * 1024,
        StackHeapSize         =  2 * 1024 * 1024,
        RenderChannelCapacity = 64,
        EnableMemoryTracking  = false,
        EnableDebugChannel    = false,
        LogHandler            = (_, _, _) => { }
    };
    
    static EntitySystemTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    // Returns true when the Scheme result equals IronScheme's #t object.
    private static bool IsTrue(object? v)  => v is bool b && b;
    
    // Returns true when the Scheme result equals IronScheme's #f object.
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    // =======================================================================
    // LIFECYCLE - entity-spawn
    // =======================================================================
    
    [Fact]
    public void Spawn_ValidTypeName_ReturnsTrue()
    {
        object? result = "(entity-spawn \"enemy-grunt\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Spawn_EmptyArgs_ReturnsFalse()
    {
        // object[] args is empty — guard fires.
        object result = EntitySystem.Spawn(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Spawn_WrongArgType_ReturnsFalse()
    {
        // Pass a string where a long is expected by the guard check.
        object result = EntitySystem.Spawn(new object[] { 42L });   // long, not string
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // LIFECYCLE - entity-destroy!
    // =======================================================================
    
    [Fact]
    public void Destroy_ValidHandle_ReturnsTrue()
    {
        // Handle need not map to a live entity; the command is fire-and-forget.
        object? result = "(entity-destroy! 99)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Destroy_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.Destroy(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Destroy_WrongArgType_ReturnsFalse()
    {
        // Pass a string where a long handle is required.
        object result = EntitySystem.Destroy(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // LIFECYCLE - entity-exists?
    // =======================================================================
    
    [Fact]
    public void Exists_CalledOutsideProcess_ReturnsFalse()
    {
        // Query() returns null outside a ScriptProcess context; backing
        // method converts null to #f.
        object? result = "(entity-exists? 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Exists_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.Exists(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Exists_WrongArgType_ReturnsFalse()
    {
        object result = EntitySystem.Exists(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TRANSFORM QUERIES - entity-get-pos
    // =======================================================================
    
    [Fact]
    public void GetPosition_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-get-pos 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetPosition_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.GetPosition(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetPosition_WrongArgType_ReturnsFalse()
    {
        object result = EntitySystem.GetPosition(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TRANSFORM MUTATIONS - entity-set-pos!
    // =======================================================================
    
    [Fact]
    public void SetPosition_ValidHandleAndVector_ReturnsTrue()
    {
        // Publish a SetTransform command to the render channel.
        object result = EntitySystem.SetPosition(new object[] { 1L, new Vector3(1f, 2f, 3f) });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetPosition_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.SetPosition(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetPosition_MissingVector_ReturnsFalse()
    {
        object result = EntitySystem.SetPosition(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetPosition_WrongHandleType_ReturnsFalse()
    {
        object result = EntitySystem.SetPosition(new object[] { "bad", new Vector3(1f, 0f, 0f) });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TRANSFORM QUERIES - entity-get-rot
    // =======================================================================
    
    [Fact]
    public void GetRotation_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-get-rot 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetRotation_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.GetRotation(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TRANSFORM MUTATIONS - entity-set-rot!
    // =======================================================================
    
    [Fact]
    public void SetRotation_ValidHandleAndQuat_ReturnsTrue()
    {
        object result = EntitySystem.SetRotation(new object[] { 1L, Quaternion.Identity });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetRotation_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.SetRotation(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetRotation_MissingQuat_ReturnsFalse()
    {
        object result = EntitySystem.SetRotation(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TRANSFORM QUERIES - entity-get-scale
    // =======================================================================
    
    [Fact]
    public void GetScale_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-get-scale 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetScale_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.GetScale(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TRANSFORM MUTATIONS - entity-set-scale!
    // =======================================================================
    
    [Fact]
    public void SetScale_ValidHandleAndVector_ReturnsTrue()
    {
        object result = EntitySystem.SetScale(new object[] { 1L, new Vector3(2f, 2f, 2f) });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetScale_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.SetScale(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetScale_MissingVector_ReturnsFalse()
    {
        object result = EntitySystem.SetScale(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PROPERTIES - entity-get-prop
    // =======================================================================
    
    [Fact]
    public void GetProperty_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-get-prop 1 \"health\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetProperty_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.GetProperty(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetProperty_MissingKey_ReturnsFalse()
    {
        object result = EntitySystem.GetProperty(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PROPERTIES - entity-set-prop!
    // =======================================================================
    
    [Fact]
    public void SetProperty_ValidArgs_ReturnsTrue()
    {
        object? result = "(entity-set-prop! 1 \"health\" 100)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetProperty_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.SetProperty(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetProperty_MissingValue_ReturnsFalse()
    {
        // Only handle + key supplied; value is required.
        object result = EntitySystem.SetProperty(new object[] { 1L, "health" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PROPERTIES - entity-has-prop?
    // =======================================================================
    
    [Fact]
    public void HasProperty_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-has-prop? 1 \"health\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HasProperty_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.HasProperty(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // COMPONENTS - entity-has-component?
    // =======================================================================
    
    [Fact]
    public void HasComponent_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-has-component? 1 \"Rigidbody\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HasComponent_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.HasComponent(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HasComponent_MissingComponentType_ReturnsFalse()
    {
        object result = EntitySystem.HasComponent(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // COMPONENTS - entity-get-component
    // =======================================================================
    
    [Fact]
    public void GetComponent_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-get-component 1 \"Rigidbody\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetComponent_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.GetComponent(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // SPATIAL QUERIES - entity-find-by-type
    // =======================================================================
    
    [Fact]
    public void FindByType_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-find-by-type \"enemy-grunt\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindByType_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.FindByType(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindByType_WrongArgType_ReturnsFalse()
    {
        // Expects string; pass long.
        object result = EntitySystem.FindByType(new object[] { 42L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // SPATIAL QUERIES - entity-find-by-tag
    // =======================================================================
    
    [Fact]
    public void FindByTag_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-find-by-tag \"hostile\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindByTag_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.FindByTag(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindByTag_WrongArgType_ReturnsFalse()
    {
        object result = EntitySystem.FindByTag(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // SPATIAL QUERIES - entity-find-in-radius
    // =======================================================================
    
    [Fact]
    public void FindInRadius_CalledOutsideProcess_ReturnsFalse()
    {
        // Vector3 and float must be passed directly; no Scheme literal for Vector3.
        object result = EntitySystem.FindInRadius(
            new object[] { new Vector3(0f, 0f, 0f), 10f });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindInRadius_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.FindInRadius(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindInRadius_MissingRadius_ReturnsFalse()
    {
        object result = EntitySystem.FindInRadius(new object[] { new Vector3(1f, 0f, 0f) });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // SPATIAL QUERIES - entity-find-nearest
    // =======================================================================
    
    [Fact]
    public void FindNearest_CalledOutsideProcess_ReturnsFalse()
    {
        object result = EntitySystem.FindNearest(
            new object[] { new Vector3(0f, 0f, 0f), "enemy-grunt" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindNearest_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.FindNearest(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindNearest_MissingTypeName_ReturnsFalse()
    {
        object result = EntitySystem.FindNearest(new object[] { new Vector3(0f, 0f, 0f) });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TAGS - entity-add-tag!
    // =======================================================================
    
    [Fact]
    public void AddTag_ValidArgs_ReturnsTrue()
    {
        object? result = "(entity-add-tag! 1 \"hostile\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void AddTag_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.AddTag(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void AddTag_MissingTag_ReturnsFalse()
    {
        object result = EntitySystem.AddTag(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void AddTag_WrongHandleType_ReturnsFalse()
    {
        object result = EntitySystem.AddTag(new object[] { "bad", "hostile" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TAGS - entity-remove-tag!
    // =======================================================================
    
    [Fact]
    public void RemoveTag_ValidArgs_ReturnsTrue()
    {
        object? result = "(entity-remove-tag! 1 \"hostile\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void RemoveTag_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.RemoveTag(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void RemoveTag_MissingTag_ReturnsFalse()
    {
        object result = EntitySystem.RemoveTag(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TAGS - entity-has-tag?
    // =======================================================================
    
    [Fact]
    public void HasTag_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-has-tag? 1 \"hostile\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HasTag_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.HasTag(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HasTag_MissingTag_ReturnsFalse()
    {
        object result = EntitySystem.HasTag(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PROCESS <-> ENTITY BINDING - entity-bind-process!
    // =======================================================================
    
    [Fact]
    public void BindProcess_ValidArgs_ReturnsTrue()
    {
        object? result = "(entity-bind-process! 10 20)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void BindProcess_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.BindProcess(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void BindProcess_MissingProcessHandle_ReturnsFalse()
    {
        object result = EntitySystem.BindProcess(new object[] { 10L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void BindProcess_WrongEntityHandleType_ReturnsFalse()
    {
        object result = EntitySystem.BindProcess(new object[] { "bad", 20L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PROCESS <-> ENTITY BINDING - entity-get-process
    // =======================================================================
    
    [Fact]
    public void GetProcess_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-get-process 10)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetProcess_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.GetProcess(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetProcess_WrongArgType_ReturnsFalse()
    {
        object result = EntitySystem.GetProcess(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PROCESS <-> ENTITY BINDING - entity-get-entity
    // =======================================================================
    
    [Fact]
    public void GetEntity_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(entity-get-entity 20)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetEntity_EmptyArgs_ReturnsFalse()
    {
        object result = EntitySystem.GetEntity(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetEntity_WrongArgType_ReturnsFalse()
    {
        object result = EntitySystem.GetEntity(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
}

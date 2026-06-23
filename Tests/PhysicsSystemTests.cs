using System;
using System.Numerics;
using IronScheme;
using IronGOAL;
using IronGOAL.Backing;

namespace Tests;

public class PhysicsSystemTests
{
    // =======================================================================
    // BOOT
    // =======================================================================
    
    static readonly GoalRuntimeConfig Config = new GoalRuntimeConfig
    {
        GlobalHeapSize        = 16 * 1024 * 1024,
        StackHeapSize         =  2 * 1024 * 1024,
        RenderChannelCapacity = 64,
        PhysicsChannelCapacity = 64,
        EnableMemoryTracking  = false,
        EnableDebugChannel    = false,
        LogHandler            = (_, _, _) => { }
    };
    
    static PhysicsSystemTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? v)  => v is bool b && b;
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    // =======================================================================
    // RAYCASTING - raycast
    // =======================================================================
    
    [Fact]
    public void Raycast_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.Raycast(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Raycast_MissingDirection_ReturnsFalse()
    {
        object result = PhysicsSystem.Raycast(new object[] { new Vector3(0, 1, 0) });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Raycast_MissingMaxDist_ReturnsFalse()
    {
        object result = PhysicsSystem.Raycast(new object[]
        {
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Raycast_WrongOriginType_ReturnsFalse()
    {
        // String where Vector3 origin is required.
        object result = PhysicsSystem.Raycast(new object[]
        {
            "not-a-vector",
            new Vector3(0, -1, 0),
            100.0,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Raycast_WrongDirectionType_ReturnsFalse()
    {
        // String where Vector3 direction is required.
        object result = PhysicsSystem.Raycast(new object[]
        {
            new Vector3(0, 1, 0),
            "not-a-vector",
            100.0,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Raycast_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        // All args valid - returns #f because there is no active ScriptProcess.
        object result = PhysicsSystem.Raycast(new object[]
        {
            new Vector3(0f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            100.0,
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // RAYCASTING - raycast-filtered
    // =======================================================================
    
    [Fact]
    public void RaycastFiltered_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.RaycastFiltered(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void RaycastFiltered_MissingLayerMask_ReturnsFalse()
    {
        // Only three of the four required args supplied.
        object result = PhysicsSystem.RaycastFiltered(new object[]
        {
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            50.0,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void RaycastFiltered_WrongLayerMaskType_ReturnsFalse()
    {
        // Long where string layer mask is required.
        object result = PhysicsSystem.RaycastFiltered(new object[]
        {
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            50.0,
            42L,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void RaycastFiltered_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.RaycastFiltered(new object[]
        {
            new Vector3(0f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            100.0,
            "Default",
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // RAYCASTING - raycast-all
    // =======================================================================
    
    [Fact]
    public void RaycastAll_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.RaycastAll(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void RaycastAll_MissingLayerMask_ReturnsFalse()
    {
        object result = PhysicsSystem.RaycastAll(new object[]
        {
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            50.0,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void RaycastAll_WrongDirectionType_ReturnsFalse()
    {
        // String where Vector3 direction is required.
        object result = PhysicsSystem.RaycastAll(new object[]
        {
            new Vector3(0, 1, 0),
            "not-a-vector",
            50.0,
            "Default",
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void RaycastAll_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.RaycastAll(new object[]
        {
            new Vector3(0f, 5f, 0f),
            new Vector3(0f, -1f, 0f),
            100.0,
            "Default",
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // OVERLAP QUERIES - overlap-sphere
    // =======================================================================
    
    [Fact]
    public void OverlapSphere_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.OverlapSphere(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapSphere_MissingLayerMask_ReturnsFalse()
    {
        object result = PhysicsSystem.OverlapSphere(new object[]
        {
            new Vector3(0, 0, 0),
            5.0,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapSphere_WrongCenterType_ReturnsFalse()
    {
        // String where Vector3 center is required.
        object result = PhysicsSystem.OverlapSphere(new object[]
        {
            "not-a-vector",
            5.0,
            "Default",
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapSphere_WrongLayerMaskType_ReturnsFalse()
    {
        // Long where string layer mask is required.
        object result = PhysicsSystem.OverlapSphere(new object[]
        {
            new Vector3(0, 0, 0),
            5.0,
            99L,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapSphere_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.OverlapSphere(new object[]
        {
            new Vector3(0f, 0f, 0f),
            10.0,
            "Default",
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // OVERLAP QUERIES - overlap-box
    // =======================================================================
    
    [Fact]
    public void OverlapBox_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.OverlapBox(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapBox_MissingHalfExtents_ReturnsFalse()
    {
        object result = PhysicsSystem.OverlapBox(new object[]
        {
            new Vector3(0, 0, 0),
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapBox_WrongHalfExtentsType_ReturnsFalse()
    {
        // String where Vector3 half-extents is required.
        object result = PhysicsSystem.OverlapBox(new object[]
        {
            new Vector3(0, 0, 0),
            "not-a-vector",
            "Default",
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapBox_WrongLayerMaskType_ReturnsFalse()
    {
        // Long where string layer mask is required.
        object result = PhysicsSystem.OverlapBox(new object[]
        {
            new Vector3(0, 0, 0),
            new Vector3(1, 1, 1),
            42L,
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OverlapBox_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.OverlapBox(new object[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 2f, 2f),
            "Default",
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // RIGID BODY COMMANDS - apply-force!
    // These methods publish a PhysicsCommand and do not require a process.
    // =======================================================================
    
    [Fact]
    public void ApplyForce_ValidArgs_ReturnsTrue()
    {
        object? result = "(apply-force! 1 (vec3 0.0 9.8 0.0))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ApplyForce_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.ApplyForce(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ApplyForce_MissingForce_ReturnsFalse()
    {
        // Handle supplied but no force vector - guard fires.
        object result = PhysicsSystem.ApplyForce(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ApplyForce_WrongHandleType_ReturnsFalse()
    {
        // String where long entity handle is required.
        object result = PhysicsSystem.ApplyForce(new object[]
        {
            "not-a-handle",
            new Vector3(0, 9.8f, 0),
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ApplyForce_WrongForceType_ReturnsFalse()
    {
        // String where Vector3 force is required.
        object result = PhysicsSystem.ApplyForce(new object[]
        {
            1L,
            "not-a-vector",
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // RIGID BODY COMMANDS - apply-impulse!
    // =======================================================================
    
    [Fact]
    public void ApplyImpulse_ValidArgs_ReturnsTrue()
    {
        object? result = "(apply-impulse! 1 (vec3 0.0 5.0 0.0))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ApplyImpulse_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.ApplyImpulse(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ApplyImpulse_MissingImpulse_ReturnsFalse()
    {
        object result = PhysicsSystem.ApplyImpulse(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ApplyImpulse_WrongHandleType_ReturnsFalse()
    {
        object result = PhysicsSystem.ApplyImpulse(new object[]
        {
            "not-a-handle",
            new Vector3(0, 5, 0),
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ApplyImpulse_WrongImpulseType_ReturnsFalse()
    {
        object result = PhysicsSystem.ApplyImpulse(new object[]
        {
            1L,
            "not-a-vector",
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // RIGID BODY COMMANDS - set-velocity!
    // =======================================================================
    
    [Fact]
    public void SetVelocity_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-velocity! 1 (vec3 3.0 0.0 0.0))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetVelocity_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.SetVelocity(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVelocity_MissingVelocity_ReturnsFalse()
    {
        object result = PhysicsSystem.SetVelocity(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVelocity_WrongHandleType_ReturnsFalse()
    {
        object result = PhysicsSystem.SetVelocity(new object[]
        {
            "not-a-handle",
            new Vector3(3, 0, 0),
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVelocity_WrongVelocityType_ReturnsFalse()
    {
        object result = PhysicsSystem.SetVelocity(new object[]
        {
            1L,
            "not-a-vector",
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // RIGID BODY COMMANDS - set-kinematic!
    // =======================================================================
    
    [Fact]
    public void SetKinematic_ValidArgs_True_ReturnsTrue()
    {
        object? result = "(set-kinematic! 1 #t)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetKinematic_ValidArgs_False_ReturnsTrue()
    {
        // Toggling back to dynamic - still a valid command that returns #t.
        object? result = "(set-kinematic! 1 #f)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetKinematic_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.SetKinematic(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetKinematic_MissingKinematicFlag_ReturnsFalse()
    {
        // Handle supplied but no boolean flag - guard fires.
        object result = PhysicsSystem.SetKinematic(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetKinematic_WrongHandleType_ReturnsFalse()
    {
        // String where long handle is required.
        object result = PhysicsSystem.SetKinematic(new object[]
        {
            "not-a-handle",
            true,
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // RIGID BODY QUERIES - get-velocity
    // =======================================================================
    
    [Fact]
    public void GetVelocity_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.GetVelocity(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetVelocity_WrongHandleType_ReturnsFalse()
    {
        // String where long entity handle is required.
        object result = PhysicsSystem.GetVelocity(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetVelocity_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        // Entity handle is valid; returns #f because no ScriptProcess exists.
        object result = PhysicsSystem.GetVelocity(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // GROUND / NAVIGATION - ground-probe
    // =======================================================================
    
    [Fact]
    public void GroundProbe_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.GroundProbe(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GroundProbe_WrongPositionType_ReturnsFalse()
    {
        // String where Vector3 position is required.
        object result = PhysicsSystem.GroundProbe(new object[] { "not-a-vector" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GroundProbe_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.GroundProbe(new object[]
        {
            new Vector3(0f, 10f, 0f),
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // GROUND / NAVIGATION - get-ground-height
    // =======================================================================
    
    [Fact]
    public void GetGroundHeight_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.GetGroundHeight(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetGroundHeight_WrongPositionType_ReturnsFalse()
    {
        object result = PhysicsSystem.GetGroundHeight(new object[] { "not-a-vector" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetGroundHeight_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.GetGroundHeight(new object[]
        {
            new Vector3(0f, 10f, 0f),
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // GROUND / NAVIGATION - project-on-navmesh
    // =======================================================================
    
    [Fact]
    public void ProjectOnNavmesh_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.ProjectOnNavmesh(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ProjectOnNavmesh_WrongPointType_ReturnsFalse()
    {
        object result = PhysicsSystem.ProjectOnNavmesh(new object[] { "not-a-vector" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ProjectOnNavmesh_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.ProjectOnNavmesh(new object[]
        {
            new Vector3(1f, 0.5f, 1f),
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // GROUND / NAVIGATION - find-path
    // =======================================================================
    
    [Fact]
    public void FindPath_EmptyArgs_ReturnsFalse()
    {
        object result = PhysicsSystem.FindPath(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindPath_MissingEnd_ReturnsFalse()
    {
        // Only start supplied - guard fires.
        object result = PhysicsSystem.FindPath(new object[]
        {
            new Vector3(0, 0, 0),
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindPath_WrongStartType_ReturnsFalse()
    {
        // String where Vector3 start is required.
        object result = PhysicsSystem.FindPath(new object[]
        {
            "not-a-vector",
            new Vector3(10, 0, 10),
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindPath_WrongEndType_ReturnsFalse()
    {
        // String where Vector3 end is required.
        object result = PhysicsSystem.FindPath(new object[]
        {
            new Vector3(0, 0, 0),
            "not-a-vector",
        });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FindPath_ValidArgs_OutsideProcess_ReturnsFalse()
    {
        object result = PhysicsSystem.FindPath(new object[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 10f),
        });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // SYMBOL REGISTRATION - verify Kernel registered all Scheme symbols
    // =======================================================================
    
    [Fact]
    public void SchemeSymbol_ApplyForce_IsRegistered()
    {
        // procedure? confirms the symbol is bound to a callable.
        object? result = "(procedure? apply-force!)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_ApplyImpulse_IsRegistered()
    {
        object? result = "(procedure? apply-impulse!)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_SetVelocity_IsRegistered()
    {
        object? result = "(procedure? set-velocity!)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_SetKinematic_IsRegistered()
    {
        object? result = "(procedure? set-kinematic!)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_GetVelocity_IsRegistered()
    {
        object? result = "(procedure? get-velocity)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_Raycast_IsRegistered()
    {
        object? result = "(procedure? raycast)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_RaycastFiltered_IsRegistered()
    {
        object? result = "(procedure? raycast-filtered)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_RaycastAll_IsRegistered()
    {
        object? result = "(procedure? raycast-all)".Eval();
        Assert.True(IsTrue(result));
    }

    [Fact]
    public void SchemeSymbol_OverlapSphere_IsRegistered()
    {
        object? result = "(procedure? overlap-sphere)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_OverlapBox_IsRegistered()
    {
        object? result = "(procedure? overlap-box)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_GroundProbe_IsRegistered()
    {
        object? result = "(procedure? ground-probe)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_GetGroundHeight_IsRegistered()
    {
        object? result = "(procedure? get-ground-height)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_ProjectOnNavmesh_IsRegistered()
    {
        object? result = "(procedure? project-on-navmesh)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SchemeSymbol_FindPath_IsRegistered()
    {
        object? result = "(procedure? find-path)".Eval();
        Assert.True(IsTrue(result));
    }
}

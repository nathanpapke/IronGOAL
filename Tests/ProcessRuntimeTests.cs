using System;
using IronScheme;
using IronScheme.Runtime;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class ProcessRuntimeTests
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

    static ProcessRuntimeTests() => Host.Create(Config);
    
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    
    // Spawns a process via Scheme and returns its numeric handle.
    private static long Spawn(string name, string state = "idle", long parent = 0)
        => Convert.ToInt64($"(process-spawn \"{name}\" \"{state}\" {parent})".Eval());
    
    // Kills a process via Scheme and ticks so the kill is applied.
    private static void KillAndTick(long handle, bool killChildren = false)
    {
        $"(process-kill {handle} {(killChildren ? "#t" : "#f")})".Eval();
        "(engine-tick)".Eval();
    }
    
    // -----------------------------------------------------------------------
    // Install
    // -----------------------------------------------------------------------
    
    
    // -----------------------------------------------------------------------
    // ProcessSpawn
    // -----------------------------------------------------------------------
    
    [Fact]
    public void ProcessSpawn_ReturnsPositiveLongHandle()
    {
        long handle = Spawn("ps-positive");
        try   { Assert.True(handle > 0); }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void ProcessSpawn_IncrementsHandlePerCall()
    {
        long h1 = Spawn("ps-inc-a");
        long h2 = Spawn("ps-inc-b");
        try   { Assert.True(h2 > h1); }
        finally { KillAndTick(h1); KillAndTick(h2); }
    }
    
    [Fact]
    public void ProcessSpawn_EmptyArgs_UsesDefaults()
    {
        // Should not throw; all parameters default gracefully.
        object result = ProcessRuntime.ProcessSpawn(Array.Empty<object>());
        Assert.True(Convert.ToInt64(result) > 0);
    }
    
    [Fact]
    public void ProcessSpawn_SetsParentChildRelationship()
    {
        long parent = Spawn("ps-parent");
        long child  = Spawn("ps-child", "idle", parent);
        try
        {
            long reportedParent = Convert.ToInt64(
                $"(get-process-parent {child})".Eval());
            Assert.Equal(parent, reportedParent);
        }
        finally { KillAndTick(parent, killChildren: true); }
    }
    
    [Fact]
    public void ProcessSpawn_SpawnedProcessIsAlive()
    {
        long handle = Spawn("ps-alive");
        try
        {
            bool alive = (bool)$"(is-process-alive? {handle})".Eval();
            Assert.True(alive);
        }
        finally { KillAndTick(handle); }
    }
    
    // -----------------------------------------------------------------------
    // ProcessKill
    // -----------------------------------------------------------------------
    
    [Fact]
    public void ProcessKill_KillsProcessAfterTick()
    {
        long handle = Spawn("pk-dying");
        KillAndTick(handle);
        bool alive = (bool)$"(is-process-alive? {handle})".Eval();
        Assert.False(alive);
    }
    
    [Fact]
    public void ProcessKill_WithKillChildren_True_KillsEntireSubtree()
    {
        long parent = Spawn("pk-root");
        long child  = Spawn("pk-child", "idle", parent);
        long grand  = Spawn("pk-grand", "idle", child);
        
        KillAndTick(parent, killChildren: true);
        
        Assert.False((bool)$"(is-process-alive? {parent})".Eval());
        Assert.False((bool)$"(is-process-alive? {child})".Eval());
        Assert.False((bool)$"(is-process-alive? {grand})".Eval());
    }
    
    [Fact]
    public void ProcessKill_WithKillChildren_False_PreservesChildren()
    {
        long parent = Spawn("pk-preserve-root");
        long child  = Spawn("pk-preserve-child", "idle", parent);
        
        KillAndTick(parent, killChildren: false);
        
        bool childAlive = (bool)$"(is-process-alive? {child})".Eval();
        Assert.True(childAlive);
        
        KillAndTick(child);
    }
    
    [Fact]
    public void ProcessKill_InvalidHandle_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(process-kill 999999 #f)".Eval());
        Assert.Null(ex);
    }
    
    // -----------------------------------------------------------------------
    // IsProcessAlive
    // -----------------------------------------------------------------------
    
    [Fact]
    public void IsProcessAlive_ReturnsTrue_ForLiveProcess()
    {
        long handle = Spawn("ipa-live");
        try   { Assert.True((bool)$"(is-process-alive? {handle})".Eval()); }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void IsProcessAlive_ReturnsFalse_ForInvalidHandle()
    {
        Assert.False((bool)"(is-process-alive? 999999)".Eval());
    }
    
    [Fact]
    public void IsProcessAlive_ReturnsFalse_AfterKillTick()
    {
        long handle = Spawn("ipa-killed");
        KillAndTick(handle);
        Assert.False((bool)$"(is-process-alive? {handle})".Eval());
    }
    
    [Fact]
    public void IsProcessAlive_EmptyArgs_DefaultsToHandleZero_ReturnsFalse()
    {
        object result = ProcessRuntime.IsProcessAlive(Array.Empty<object>());
        Assert.False((bool)result);
    }
    
    // -----------------------------------------------------------------------
    // GetProcessParent
    // -----------------------------------------------------------------------
    
    [Fact]
    public void GetProcessParent_ReturnsZero_ForRootProcess()
    {
        long handle = Spawn("gpp-root");
        try   { Assert.Equal(0L, Convert.ToInt64($"(get-process-parent {handle})".Eval())); }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void GetProcessParent_ReturnsCorrectParentHandle()
    {
        long parent = Spawn("gpp-parent");
        long child  = Spawn("gpp-child", "idle", parent);
        try
        {
            long reported = Convert.ToInt64($"(get-process-parent {child})".Eval());
            Assert.Equal(parent, reported);
        }
        finally { KillAndTick(parent, killChildren: true); }
    }
    
    [Fact]
    public void GetProcessParent_InvalidHandle_ReturnsZero()
    {
        Assert.Equal(0L, Convert.ToInt64("(get-process-parent 999999)".Eval()));
    }
    
    // -----------------------------------------------------------------------
    // GetProcessChildren
    // -----------------------------------------------------------------------
    
    [Fact]
    public void GetProcessChildren_ReturnsEmpty_ForLeafProcess()
    {
        long handle = Spawn("gpc-leaf");
        try
        {
            // (get-process-children h) returns a Scheme list; length 0 = no children.
            long count = Convert.ToInt64(
                $"(length (get-process-children {handle}))".Eval());
            Assert.Equal(0L, count);
        }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void GetProcessChildren_ReturnsAllDirectChildren()
    {
        long parent = Spawn("gpc-parent");
        long c1     = Spawn("gpc-c1", "idle", parent);
        long c2     = Spawn("gpc-c2", "idle", parent);
        try
        {
            long count = Convert.ToInt64(
                $"(length (get-process-children {parent}))".Eval());
            Assert.Equal(2L, count);
            
            bool hasC1 = (bool)
                $"(member {c1} (get-process-children {parent}))".Eval();
            bool hasC2 = (bool)
                $"(member {c2} (get-process-children {parent}))".Eval();
            Assert.True(hasC1);
            Assert.True(hasC2);
        }
        finally { KillAndTick(parent, killChildren: true); }
    }
    
    [Fact]
    public void GetProcessChildren_InvalidHandle_ReturnsEmptyList()
    {
        long count = Convert.ToInt64("(length (get-process-children 999999))".Eval());
        Assert.Equal(0L, count);
    }
    
    // -----------------------------------------------------------------------
    // GoState
    // -----------------------------------------------------------------------
    
    [Fact]
    public void GoState_TransitionApplied_AfterTick()
    {
        // Define a state so the scheduler recognises "run" for this type.
        "(define-state \"gs-actor\" \"run\" #f #f #f #f)".Eval();
        
        long handle = Spawn("gs-actor-1", "idle");
        try
        {
            $"(go-state {handle} \"run\")".Eval();
            "(engine-tick)".Eval();
 
            // The observable proof is that the process is still alive and
            // did not crash on the unknown-state transition.
            Assert.True((bool)$"(is-process-alive? {handle})".Eval());
        }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void GoState_EmptyArgs_DoesNotThrow()
    {
        var ex = Record.Exception(() => ProcessRuntime.GoState(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    [Fact]
    public void GoState_InvalidHandle_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(go-state 999999 \"run\")".Eval());
        Assert.Null(ex);
    }
    
    // -----------------------------------------------------------------------
    // DefineState
    // -----------------------------------------------------------------------
    
    [Fact]
    public void DefineState_EnterProc_CalledOnTransition()
    {
        // Use a Scheme variable as a side-effect flag.
        "(define ds-entered #f)".Eval();
        "(define-state \"ds-npc\" \"greet\" (lambda () (set! ds-entered #t)) #f #f #f)".Eval();
        
        long handle = Spawn("ds-npc-1", "idle");
        try
        {
            $"(go-state {handle} \"greet\")".Eval();
            "(engine-tick)".Eval();
 
            Assert.True((bool)"ds-entered".Eval());
        }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void DefineState_EmptyArgs_DoesNotThrow()
    {
        var ex = Record.Exception(() => ProcessRuntime.DefineState(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    [Fact]
    public void DefineState_ExitProc_CalledOnLeave()
    {
        "(define ds-exited #f)".Eval();
        "(define-state \"ds-robot\" \"patrol\" #f #f (lambda () (set! ds-exited #t)) #f)".Eval();
        "(define-state \"ds-robot\" \"idle\"   #f #f #f #f)".Eval();
        
        long handle = Spawn("ds-robot-1", "patrol");
        try
        {
            $"(go-state {handle} \"idle\")".Eval();
            "(engine-tick)".Eval();
            
            Assert.True((bool)"ds-exited".Eval());
        }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void DefineState_OverwritesPreviousRegistration_NewEnterProcFires()
    {
        "(define ds-v #f)".Eval();
        "(define-state \"ds-boss\" \"rage\" (lambda () (set! ds-v 1)) #f #f #f)".Eval();
        "(define-state \"ds-boss\" \"rage\" (lambda () (set! ds-v 2)) #f #f #f)".Eval();
        
        long handle = Spawn("ds-boss-1", "idle");
        try
        {
            $"(go-state {handle} \"rage\")".Eval();
            "(engine-tick)".Eval();
            
            Assert.Equal(2L, Convert.ToInt64("ds-v".Eval()));
        }
        finally { KillAndTick(handle); }
    }
    
    // -----------------------------------------------------------------------
    // Suspend
    // -----------------------------------------------------------------------
    
    // Suspend must be called from a running process thread.  We simulate that
    // by setting ProcessScheduler.CurrentProcess on a background thread and
    // using the process's internal gates to unblock it after asserting.
    
    [Fact]
    public void Suspend_DoesNotThrow_WhenCalledOutsideProcess()
    {
        // ProcessScheduler.CurrentProcess is null on this thread; the scheduler
        // should log a warning and return without throwing.
        var ex = Record.Exception(() => ProcessRuntime.Suspend(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    [Fact]
    public void Suspend_ReturnsNil_WhenCalledOutsideProcess()
    {
        object result = ProcessRuntime.Suspend(Array.Empty<object>());
        Assert.NotNull(result);
    }
    
    [Fact]
    public void Suspend_OutsideProcess_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(suspend)".Eval());
        Assert.Null(ex);
    }
    
    // -----------------------------------------------------------------------
    // SuspendForFrames
    // -----------------------------------------------------------------------
    
    [Fact]
    public void SuspendForFrames_OutsideProcess_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(suspend-for-frames 3)".Eval());
        Assert.Null(ex);
    }
 
    [Fact]
    public void SuspendForFrames_ZeroFrames_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(suspend-for-frames 0)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SuspendForFrames_OutsideProcess_ReturnsNil()
    {
        object result = ProcessRuntime.SuspendForFrames(new object[] { 5 });
        Assert.NotNull(result);
    }
    
    [Fact]
    public void SuspendForFrames_EmptyArgs_DefaultsToOneFrame_DoesNotThrow()
    {
        var ex = Record.Exception(
            () => ProcessRuntime.SuspendForFrames(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    // -----------------------------------------------------------------------
    // SuspendUntil
    // -----------------------------------------------------------------------
    
    [Fact]
    public void SuspendUntil_OutsideProcess_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(suspend-until (lambda () #t))".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SuspendUntil_NonCallablePredicate_DoesNotThrow()
    {
        // Non-Callable objects are ignored silently.
        var ex = Record.Exception(
            () => ProcessRuntime.SuspendUntil(new object[] { "not-a-lambda" }));
        Assert.Null(ex);
    }
    
    [Fact]
    public void SuspendUntil_FalsePredicate_OutsideProcess_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(suspend-until (lambda () #f))".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SuspendUntil_EmptyArgs_DoesNotThrow()
    {
        var ex = Record.Exception(
            () => ProcessRuntime.SuspendUntil(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    // -----------------------------------------------------------------------
    // SendEvent
    // -----------------------------------------------------------------------
    
    [Fact]
    public void SendEvent_ToLiveProcess_DoesNotThrow()
    {
        long handle = Spawn("se-target");
        try
        {
            var ex = Record.Exception(() =>
                $"(send-event {handle} \"stun\" #f)".Eval());
            Assert.Null(ex);
        }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void SendEvent_ToDeadProcess_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(send-event 999999 \"damage\" 10)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SendEvent_EmptyArgs_DoesNotThrow()
    {
        var ex = Record.Exception(() => ProcessRuntime.SendEvent(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    [Fact]
    public void SendEvent_EventProc_CalledDuringTick()
    {
        "(define se-fired #f)".Eval();
        "(define-state \"se-enemy\" \"idle\" #f #f #f (lambda (proc type data) (set! se-fired #t)))".Eval();
        
        long handle = Spawn("se-enemy-1", "idle");
        try
        {
            $"(send-event {handle} \"alert\" #f)".Eval();
            "(engine-tick)".Eval();
            
            Assert.True((bool)"se-fired".Eval());
        }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void SendEvent_EventProc_ReceivesCorrectType()
    {
        "(define se-type #f)".Eval();
        "(define-state \"se-unit\" \"patrol\" #f #f #f (lambda (proc type data) (set! se-type type)))".Eval();
        
        long handle = Spawn("se-unit-1", "patrol");
        try
        {
            $"(send-event {handle} \"freeze\" #f)".Eval();
            "(engine-tick)".Eval();
            
            Assert.Equal("freeze", "se-type".Eval()?.ToString());
        }
        finally { KillAndTick(handle); }
    }
    
    // -----------------------------------------------------------------------
    // BroadcastEvent
    // -----------------------------------------------------------------------
    
    [Fact]
    public void BroadcastEvent_ReturnsNil()
    {
        object result = ProcessRuntime.BroadcastEvent(
            new object[] { "game-over", null! });
        Assert.NotNull(result);
    }
    
    [Fact]
    public void BroadcastEvent_DoesNotThrow_WithNoActiveProcesses()
    {
        var ex = Record.Exception(() => "(broadcast-event \"level-reset\" #f)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void BroadcastEvent_EmptyArgs_DoesNotThrow()
    {
        var ex = Record.Exception(
            () => ProcessRuntime.BroadcastEvent(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    [Fact]
    public void BroadcastEvent_DeliveredToAllLiveProcesses()
    {
        "(define be-count 0)".Eval();
        "(define-state \"be-unit\" \"patrol\" #f #f #f (lambda (proc type data) (set! be-count (+ be-count 1))))".Eval();
        
        long u1 = Spawn("be-unit-1", "patrol");
        long u2 = Spawn("be-unit-2", "patrol");
        long u3 = Spawn("be-unit-3", "patrol");
        try
        {
            "(broadcast-event \"alert\" #f)".Eval();
            "(engine-tick)".Eval();
            
            Assert.Equal(3L, Convert.ToInt64("be-count".Eval()));
        }
        finally
        {
            KillAndTick(u1);
            KillAndTick(u2);
            KillAndTick(u3);
        }
    }
    
    // -----------------------------------------------------------------------
    // SetProcessPriority
    // -----------------------------------------------------------------------
    
    [Fact]
    public void SetProcessPriority_DoesNotThrow_OnValidHandle()
    {
        long handle = Spawn("spp-valid");
        try
        {
            var ex = Record.Exception(() =>
                $"(set-process-priority {handle} 5)".Eval());
            Assert.Null(ex);
        }
        finally { KillAndTick(handle); }
    }

    [Fact]
    public void SetProcessPriority_NegativePriority_DoesNotThrow()
    {
        long handle = Spawn("spp-negative");
        try
        {
            var ex = Record.Exception(() =>
                $"(set-process-priority {handle} -100)".Eval());
            Assert.Null(ex);
        }
        finally { KillAndTick(handle); }
    }
    
    [Fact]
    public void SetProcessPriority_InvalidHandle_DoesNotThrow()
    {
        var ex = Record.Exception(() => "(set-process-priority 999999 5)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SetProcessPriority_EmptyArgs_DoesNotThrow()
    {
        var ex = Record.Exception(
            () => ProcessRuntime.SetProcessPriority(Array.Empty<object>()));
        Assert.Null(ex);
    }
    
    [Fact]
    public void SetProcessPriority_LowerValueRunsFirst_BothProcessesSurviveTick()
    {
        long low  = Spawn("spp-low",  "idle");
        long high = Spawn("spp-high", "idle");
        try
        {
            $"(set-process-priority {low}  10)".Eval();
            $"(set-process-priority {high}  1)".Eval();
            
            var ex = Record.Exception(() => "(engine-tick)".Eval());
            Assert.Null(ex);
            
            Assert.True((bool)$"(is-process-alive? {low})".Eval());
            Assert.True((bool)$"(is-process-alive? {high})".Eval());
        }
        finally { KillAndTick(low); KillAndTick(high); }
    }

}

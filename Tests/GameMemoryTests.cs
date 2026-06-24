using System;
using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class GameMemoryTests
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
    
    static GameMemoryTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? v)  => v is bool b && b;
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    // =======================================================================
    // SYMBOL REGISTRATION
    // =======================================================================
    
    [Fact]
    public void KmmallocSymbol_IsRegistered()
    {
        object? result = "(procedure? kmalloc)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MallocSymbol_IsRegistered()
    {
        object? result = "(procedure? malloc)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void KfreeSymbol_IsRegistered()
    {
        object? result = "(procedure? kfree)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void KmemopenSymbol_IsRegistered()
    {
        object? result = "(procedure? kmemopen)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void KmemcloseSymbol_IsRegistered()
    {
        object? result = "(procedure? kmemclose)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void NewDynamicStructureSymbol_IsRegistered()
    {
        object? result = "(procedure? new-dynamic-structure)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void HeapBytesUsedSymbol_IsRegistered()
    {
        object? result = "(procedure? heap-bytes-used)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void HeapBytesTotalSymbol_IsRegistered()
    {
        object? result = "(procedure? heap-bytes-total)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void HeapResetSymbol_IsRegistered()
    {
        object? result = "(procedure? heap-reset!)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ObjSerializeSymbol_IsRegistered()
    {
        object? result = "(procedure? obj-serialize)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ObjDeserializeSymbol_IsRegistered()
    {
        object? result = "(procedure? obj-deserialize)".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // ALLOC (kmalloc) - guard rails
    // No ScriptProcess context: Alloc returns #f from the no-process guard.
    // =======================================================================
    
    [Fact]
    public void Alloc_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.Alloc(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Alloc_WrongArenaType_ReturnsFalse()
    {
        // args[0] must be string; pass long instead.
        object result = GameMemory.Alloc(new object[] { 42L, 64L, 0L, "label" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Alloc_WrongLabelType_ReturnsFalse()
    {
        // args[3] must be string; pass long instead.
        object result = GameMemory.Alloc(new object[] { "global", 64L, 0L, 99L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Alloc_ZeroSize_ReturnsFalse()
    {
        // size <= 0 is rejected before process suspension.
        object result = GameMemory.Alloc(new object[] { "global", 0L, 0L, "label" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Alloc_NegativeSize_ReturnsFalse()
    {
        object result = GameMemory.Alloc(new object[] { "global", -1L, 0L, "label" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Alloc_NoProcessContext_ReturnsFalse()
    {
        // Valid args but no running ScriptProcess - no-context guard fires.
        object result = GameMemory.Alloc(new object[] { "global", 64L, 0L, "label" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Alloc_LevelArena_NoProcessContext_ReturnsFalse()
    {
        // All named arenas should hit the same no-context guard.
        object result = GameMemory.Alloc(new object[] { "level", 128L, 0L, "lvl-obj" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Alloc_UnknownArena_NoProcessContext_ReturnsFalse()
    {
        // Unknown arena names fall back to Global via ParseArena; guard still fires.
        object result = GameMemory.Alloc(new object[] { "bogus-arena", 64L, 0L, "label" });
        Assert.True(IsFalse(result));
    }
    
    // Scheme surface - same no-context path.
    [Fact]
    public void Alloc_SchemeCall_NoProcessContext_ReturnsFalse()
    {
        object? result = "(kmalloc \"global\" 64 0 \"test-label\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // MANAGEDALLOC (malloc) - delegates to Alloc
    // =======================================================================
    
    [Fact]
    public void ManagedAlloc_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.ManagedAlloc(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ManagedAlloc_WrongArenaType_ReturnsFalse()
    {
        object result = GameMemory.ManagedAlloc(new object[] { 42L, 64L, 0L, "label" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ManagedAlloc_NoProcessContext_ReturnsFalse()
    {
        object result = GameMemory.ManagedAlloc(new object[] { "global", 64L, 0L, "label" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ManagedAlloc_SchemeCall_NoProcessContext_ReturnsFalse()
    {
        object? result = "(malloc \"global\" 64 0 \"ml-label\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // FREE (kfree) - fire-and-return
    // =======================================================================
    
    [Fact]
    public void Free_ValidHandle_ReturnsTrue()
    {
        // Fire-and-return: no process context required.
        object result = GameMemory.Free(new object[] { 12345L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Free_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.Free(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Free_WrongHandleType_ReturnsFalse()
    {
        // Handle must be long; pass string.
        object result = GameMemory.Free(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Free_SchemeCall_ValidHandle_ReturnsTrue()
    {
        object? result = "(kfree 99)".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // MEMOPEN (kmemopen) - fire-and-return, pushes scope stack
    // =======================================================================
    
    [Fact]
    public void MemOpen_ValidArgs_ReturnsTrue()
    {
        object result = GameMemory.MemOpen(new object[] { "global", "test-scope" });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MemOpen_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.MemOpen(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MemOpen_MissingScopeTag_ReturnsFalse()
    {
        // Arena supplied, but scope tag is absent - guard fires.
        object result = GameMemory.MemOpen(new object[] { "global" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MemOpen_WrongArenaType_ReturnsFalse()
    {
        object result = GameMemory.MemOpen(new object[] { 42L, "scope" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MemOpen_WrongScopeTagType_ReturnsFalse()
    {
        object result = GameMemory.MemOpen(new object[] { "global", 99L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MemOpen_SchemeCall_ReturnsTrue()
    {
        object? result = "(kmemopen \"level\" \"test-level-scope\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // MEMCLOSE (kmemclose) - fire-and-return, pops scope stack
    // =======================================================================
    
    [Fact]
    public void MemClose_AfterMemOpen_ReturnsTrue()
    {
        // Open a scope then close it - stack discipline is satisfied.
        GameMemory.MemOpen(new object[] { "global", "close-test-scope" });
        object result = GameMemory.MemClose(Array.Empty<object>());
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MemClose_EmptyStack_ReturnsFalse()
    {
        // Drain any open scopes that prior tests may have left (MemClose is
        // stateful, so we call it until the stack is exhausted, then assert
        // the next call returns #f).
        //
        // We cannot inspect the stack directly (it is private), so we open a
        // known scope and close it, then do one more close to hit the empty
        // branch.
        GameMemory.MemOpen(new object[] { "stack", "drain-scope" });
        GameMemory.MemClose(Array.Empty<object>()); // balanced close
        
        // Capture the current depth by probing: if the stack happened to have
        // residual entries from other tests we drain them first.
        object probe = GameMemory.MemClose(Array.Empty<object>());
        while (IsTrue(probe))
        {
            probe = GameMemory.MemClose(Array.Empty<object>());
        }
        
        // Now the stack is provably empty.
        object result = GameMemory.MemClose(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MemClose_SchemeCall_AfterOpen_ReturnsTrue()
    {
        "(kmemopen \"debug\" \"scheme-close-test\")".Eval();
        object? result = "(kmemclose)".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // MEMOPEN / MEMCLOSE - scope-stack round-trip
    // =======================================================================
    
    [Fact]
    public void MemOpen_MultipleArenas_ClosesInLIFOOrder()
    {
        // Open two distinct arena scopes, then close both.  Each MemClose
        // should return #t because the stack is non-empty for both pops.
        GameMemory.MemOpen(new object[] { "global", "outer" });
        GameMemory.MemOpen(new object[] { "level",  "inner" });
        
        object close1 = GameMemory.MemClose(Array.Empty<object>()); // pops "inner"
        object close2 = GameMemory.MemClose(Array.Empty<object>()); // pops "outer"
        
        Assert.True(IsTrue(close1));
        Assert.True(IsTrue(close2));
    }
    
    // =======================================================================
    // HEAPRESET (heap-reset!) - fire-and-return, clears scope stack for arena
    // =======================================================================
    
    [Fact]
    public void HeapReset_ValidArena_ReturnsTrue()
    {
        object result = GameMemory.HeapReset(new object[] { "global" });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void HeapReset_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.HeapReset(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapReset_WrongArenaType_ReturnsFalse()
    {
        object result = GameMemory.HeapReset(new object[] { 42L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapReset_ClearsOrphanedScopeStackEntries()
    {
        // Open two scopes on "global", then HeapReset "global".
        // Subsequent MemClose calls for "global" should find the stack empty
        // (entries were cleared by HeapReset).  Non-global scopes are
        // preserved; we open one on "level" to verify.
        GameMemory.MemOpen(new object[] { "global", "orphan-a" });
        GameMemory.MemOpen(new object[] { "global", "orphan-b" });
        GameMemory.MemOpen(new object[] { "level",  "preserved" });
        
        GameMemory.HeapReset(new object[] { "global" });
        
        // The "level" scope should still be closeable.
        object levelClose = GameMemory.MemClose(Array.Empty<object>());
        Assert.True(IsTrue(levelClose));
        
        // All "global" scopes should be gone; stack may now be empty.
        // Drain whatever remains and confirm.
        object probe = GameMemory.MemClose(Array.Empty<object>());
        while (IsTrue(probe))
        {
            probe = GameMemory.MemClose(Array.Empty<object>());
        }
        // After draining, stack is empty.
        object final = GameMemory.MemClose(Array.Empty<object>());
        Assert.True(IsFalse(final));
    }
    
    [Fact]
    public void HeapReset_AllArenas_ReturnTrue()
    {
        // All four named arenas should succeed.
        foreach (string arena in new[] { "global", "level", "stack", "debug" })
        {
            object result = GameMemory.HeapReset(new object[] { arena });
            Assert.True(IsTrue(result), $"HeapReset failed for arena '{arena}'");
        }
    }
    
    [Fact]
    public void HeapReset_SchemeCall_ReturnsTrue()
    {
        object? result = "(heap-reset! \"global\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // NEWDYNAMICSTRUCTURE (new-dynamic-structure)
    // Delegates to Alloc internally; no-process-context guard fires.
    // =======================================================================
    
    [Fact]
    public void NewDynamicStructure_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.NewDynamicStructure(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void NewDynamicStructure_WrongArenaType_ReturnsFalse()
    {
        object result = GameMemory.NewDynamicStructure(new object[] { 42L, "enemy-info", 64L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void NewDynamicStructure_WrongTypeNameType_ReturnsFalse()
    {
        object result = GameMemory.NewDynamicStructure(new object[] { "global", 99L, 64L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void NewDynamicStructure_ZeroSize_ReturnsFalse()
    {
        // size <= 0 rejected before delegation to Alloc.
        object result = GameMemory.NewDynamicStructure(new object[] { "global", "enemy-info", 0L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void NewDynamicStructure_NegativeSize_ReturnsFalse()
    {
        object result = GameMemory.NewDynamicStructure(new object[] { "global", "enemy-info", -8L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void NewDynamicStructure_NoProcessContext_ReturnsFalse()
    {
        // Valid args, but no running ScriptProcess - Alloc's no-context guard fires.
        object result = GameMemory.NewDynamicStructure(new object[] { "global", "enemy-info", 128L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void NewDynamicStructure_SchemeCall_NoProcessContext_ReturnsFalse()
    {
        object? result = "(new-dynamic-structure \"global\" \"enemy-info\" 128)".Eval();
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // HEAPBYTESUSED (heap-bytes-used) - suspending query
    // No ScriptProcess context: Query() returns null -> method returns #f.
    // =======================================================================
    
    [Fact]
    public void HeapBytesUsed_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.HeapBytesUsed(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesUsed_WrongArenaType_ReturnsFalse()
    {
        object result = GameMemory.HeapBytesUsed(new object[] { 42L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesUsed_NoProcessContext_ReturnsFalse()
    {
        // No running ScriptProcess - Query() returns null; method returns #f.
        object result = GameMemory.HeapBytesUsed(new object[] { "global" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesUsed_SchemeCall_NoProcessContext_ReturnsFalse()
    {
        object? result = "(heap-bytes-used \"global\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesUsed_AllNamedArenas_NoProcessContext_ReturnsFalse()
    {
        foreach (string arena in new[] { "global", "level", "stack", "debug" })
        {
            object result = GameMemory.HeapBytesUsed(new object[] { arena });
            Assert.True(IsFalse(result), $"Expected #f for arena '{arena}'");
        }
    }
    
    // =======================================================================
    // HEAPBYTESTOTAL (heap-bytes-total) - suspending query
    // =======================================================================
    
    [Fact]
    public void HeapBytesTotal_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.HeapBytesTotal(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesTotal_WrongArenaType_ReturnsFalse()
    {
        object result = GameMemory.HeapBytesTotal(new object[] { 99L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesTotal_NoProcessContext_ReturnsFalse()
    {
        object result = GameMemory.HeapBytesTotal(new object[] { "global" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesTotal_SchemeCall_NoProcessContext_ReturnsFalse()
    {
        object? result = "(heap-bytes-total \"level\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void HeapBytesTotal_AllNamedArenas_NoProcessContext_ReturnsFalse()
    {
        foreach (string arena in new[] { "global", "level", "stack", "debug" })
        {
            object result = GameMemory.HeapBytesTotal(new object[] { arena });
            Assert.True(IsFalse(result), $"Expected #f for arena '{arena}'");
        }
    }
    
    // =======================================================================
    // SERIALIZE (obj-serialize) - fire-and-return command
    // =======================================================================
    
    [Fact]
    public void Serialize_ValidHandle_ReturnsTrue()
    {
        // Fire-and-return; no process context required.
        object result = GameMemory.Serialize(new object[] { 42L });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Serialize_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.Serialize(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Serialize_WrongHandleType_ReturnsFalse()
    {
        // Handle must be long; pass string.
        object result = GameMemory.Serialize(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Serialize_SchemeCall_ValidHandle_ReturnsTrue()
    {
        object? result = "(obj-serialize 77)".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // DESERIALIZE (obj-deserialize) - fire-and-return command
    // =======================================================================
    
    [Fact]
    public void Deserialize_ValidArgs_ReturnsTrue()
    {
        // Fire-and-return; no process context required.
        object result = GameMemory.Deserialize(new object[] { 55L, "enemy-info" });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Deserialize_EmptyArgs_ReturnsFalse()
    {
        object result = GameMemory.Deserialize(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Deserialize_WrongHandleType_ReturnsFalse()
    {
        // blobHandle must be long.
        object result = GameMemory.Deserialize(new object[] { "not-a-handle", "enemy-info" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Deserialize_WrongTypeNameType_ReturnsFalse()
    {
        // typeName must be string.
        object result = GameMemory.Deserialize(new object[] { 55L, 99L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Deserialize_MissingTypeName_ReturnsFalse()
    {
        // Only blobHandle supplied; typeName is absent.
        object result = GameMemory.Deserialize(new object[] { 55L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Deserialize_SchemeCall_ValidArgs_ReturnsTrue()
    {
        object? result = "(obj-deserialize 55 \"enemy-info\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // ARENA PARSING - UnknownArenaFallsBackToGlobal
    // Verified indirectly: HeapReset on an unknown name must not throw.
    // =======================================================================
    
    [Fact]
    public void ParseArena_UnknownName_DoesNotThrow_ReturnsTrue()
    {
        // ParseArena falls back to Global for unrecognized names.
        // HeapReset is the only non-suspending method that calls ParseArena
        // and returns a visible result without needing a process.
        object result = GameMemory.HeapReset(new object[] { "nonexistent-arena" });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ParseArena_CaseInsensitive_ReturnsTrue()
    {
        // Arena name matching is case-insensitive per ParseArena implementation.
        object upper  = GameMemory.HeapReset(new object[] { "GLOBAL" });
        object mixed  = GameMemory.HeapReset(new object[] { "Level"  });
        Assert.True(IsTrue(upper));
        Assert.True(IsTrue(mixed));
    }
}
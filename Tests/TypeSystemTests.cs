using System;
using IronScheme;
using IronScheme.Runtime;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class TypeSystemTests
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
    
    static TypeSystemTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? v)  => v is bool b && b;
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    /// <summary>
    /// Builds a Cons pair identical to what IronScheme's reader produces for
    /// a two-element list <c>(field-name field-type)</c>:
    ///   Cons { car = field-name, cdr = Cons { car = field-type, cdr = () } }
    /// This matches the structure DefineType expects when iterating field args.
    /// </summary>
    private static Cons MakeFieldPair(string fieldName, string fieldType)
        => new Cons(fieldName, new Cons(fieldType, null));
    
    // =======================================================================
    // SYMBOL REGISTRATION
    // =======================================================================
    
    [Fact]
    public void DefineTypeSymbol_IsRegistered()
    {
        object? result = "(procedure? define-type)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void TypeSizeSymbol_IsRegistered()
    {
        object? result = "(procedure? type-size)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void TypeFieldOffsetSymbol_IsRegistered()
    {
        object? result = "(procedure? type-field-offset)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void TypeParentSymbol_IsRegistered()
    {
        object? result = "(procedure? type-parent)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MethodSetSymbol_IsRegistered()
    {
        object? result = "(procedure? method-set!)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MethodGetSymbol_IsRegistered()
    {
        object? result = "(procedure? method-get)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void IsTypeSymbol_IsRegistered()
    {
        object? result = "(procedure? is-type?)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void TypeOfSymbol_IsRegistered()
    {
        object? result = "(procedure? type-of)".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // DefineType - Scheme path
    // =======================================================================
    
    [Fact]
    public void DefineType_Scheme_NoFields_ReturnsSizeZero()
    {
        // A type with no fields has byte size 0.
        object? result = "(define-type ts-empty-s \"basic\")".Eval();
        Assert.Equal(0L, result);
    }
    
    [Fact]
    public void DefineType_Scheme_SingleInt32Field_ReturnsSizeFour()
    {
        // int/int32/uint32/float are each 4 bytes.
        object? result = "(define-type ts-single-s \"basic\" (\"hp\" \"int32\"))".Eval();
        Assert.Equal(4L, result);
    }
    
    [Fact]
    public void DefineType_Scheme_MultipleFields_ReturnsSumOfSizes()
    {
        // bool(1) + int16(2) + float(4) + int64(8) = 15 bytes.
        object? result = "(define-type ts-multi-s \"structure\"\n" +
                         "  (\"flag\"  \"bool\")\n"  +
                         "  (\"val16\" \"int16\")\n" +
                         "  (\"spd\"   \"float\")\n" +
                         "  (\"big\"   \"int64\"))".Eval();
        Assert.Equal(15L, result);
    }
    
    [Fact]
    public void DefineType_Scheme_Reregistration_SilentlyReplaces()
    {
        // First registration: two float fields -> 8 bytes.
        "(define-type ts-rereg-s \"basic\" (\"x\" \"float\") (\"y\" \"float\"))".Eval();
        
        // Second registration with only one field -> 4 bytes.
        object? result = "(define-type ts-rereg-s \"basic\" (\"x\" \"float\"))".Eval();
        Assert.Equal(4L, result);
        
        // type-size must reflect the replacement, not the original.
        object? size = "(type-size \"ts-rereg-s\")".Eval();
        Assert.Equal(4L, size);
    }
    
    // =======================================================================
    // DefineType - C# backing path
    // =======================================================================
    
    [Fact]
    public void DefineType_Direct_EmptyArgs_ReturnsFalse()
    {
        object result = TypeSystem.DefineType(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DefineType_Direct_MissingParent_ReturnsFalse()
    {
        // Only one arg (type name); parent is absent - guard fires.
        object result = TypeSystem.DefineType(new object[] { "ts-guard-no-parent" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DefineType_Direct_WrongTypeNameType_ReturnsFalse()
    {
        // Long where string is required for the type name.
        object result = TypeSystem.DefineType(new object[] { 42L, "basic" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DefineType_Direct_WrongParentType_ReturnsFalse()
    {
        // Long where string is required for the parent name.
        object result = TypeSystem.DefineType(new object[] { "ts-bad-parent", 99L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DefineType_Direct_ValidNoFields_ReturnsSizeZero()
    {
        object result = TypeSystem.DefineType(new object[] { "ts-empty-d", "basic" });
        Assert.Equal(0L, result);
    }
    
    [Fact]
    public void DefineType_Direct_WithFields_ReturnsCorrectSize()
    {
        // Two float fields -> 8 bytes.
        object result = TypeSystem.DefineType(new object[]
        {
            "ts-twofloat-d",
            "basic",
            MakeFieldPair("x", "float"),
            MakeFieldPair("y", "float"),
        });
        Assert.Equal(8L, result);
    }
    
    [Fact]
    public void DefineType_Direct_MalformedFieldPairSkipped()
    {
        // A non-Cons field arg is silently skipped; only the valid pair counts.
        object result = TypeSystem.DefineType(new object[]
        {
            "ts-skip-bad-d",
            "basic",
            "not-a-cons",              // skipped
            MakeFieldPair("hp", "int32"),
        });
        // Only the valid int32 field contributes 4 bytes.
        Assert.Equal(4L, result);
    }
    
    // =======================================================================
    // DefineType - primitive size correctness
    // =======================================================================
    
    [Theory]
    [InlineData("int8",    1)]
    [InlineData("uint8",   1)]
    [InlineData("bool",    1)]
    [InlineData("int16",   2)]
    [InlineData("uint16",  2)]
    [InlineData("int32",   4)]
    [InlineData("uint32",  4)]
    [InlineData("int",     4)]
    [InlineData("float",   4)]
    [InlineData("int64",   8)]
    [InlineData("uint64",  8)]
    [InlineData("int128",  16)]
    [InlineData("uint128", 16)]
    public void DefineType_PrimitiveSizes_AreCorrect(string goalType, int expectedBytes)
    {
        string typeName = $"ts-prim-{goalType}-d";
        object result = TypeSystem.DefineType(new object[]
        {
            typeName,
            "basic",
            MakeFieldPair("f", goalType),
        });
        Assert.Equal((long)expectedBytes, result);
    }
    
    [Fact]
    public void DefineType_UnknownFieldType_FallsBackToFourBytes()
    {
        // An unregistered type name is treated as a 4-byte pointer reference.
        object result = TypeSystem.DefineType(new object[]
        {
            "ts-unknown-field-d",
            "basic",
            MakeFieldPair("ptr", "no-such-type"),
        });
        Assert.Equal(4L, result);
    }
    
    [Fact]
    public void DefineType_UserTypeField_UsesRegisteredSize()
    {
        // Register a 3-byte inner type first (bool + int16 = 1 + 2 = 3).
        TypeSystem.DefineType(new object[]
        {
            "ts-inner-d",
            "basic",
            MakeFieldPair("a", "bool"),
            MakeFieldPair("b", "int16"),
        });
        
        // Outer type has one field of the inner type → should be 3 bytes.
        object result = TypeSystem.DefineType(new object[]
        {
            "ts-outer-d",
            "basic",
            MakeFieldPair("inner", "ts-inner-d"),
        });
        Assert.Equal(3L, result);
    }
    
    // =======================================================================
    // TypeSize - Scheme path
    // =======================================================================
    
    [Fact]
    public void TypeSize_Scheme_RegisteredType_ReturnsSize()
    {
        "(define-type ts-size-s \"basic\" (\"x\" \"float\") (\"y\" \"float\"))".Eval();
        object? result = "(type-size \"ts-size-s\")".Eval();
        Assert.Equal(8L, result);
    }
    
    [Fact]
    public void TypeSize_Scheme_UnknownType_ReturnsFalse()
    {
        object? result = "(type-size \"ts-not-registered-s\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TypeSize - C# backing path
    // =======================================================================
    
    [Fact]
    public void TypeSize_Direct_RegisteredType_ReturnsSize()
    {
        TypeSystem.DefineType(new object[]
        {
            "ts-size-d",
            "basic",
            MakeFieldPair("hp", "int32"),   // 4
            MakeFieldPair("mp", "int16"),   // 2
        });
        object result = TypeSystem.TypeSize(new object[] { "ts-size-d" });
        Assert.Equal(6L, result);
    }
    
    [Fact]
    public void TypeSize_Direct_UnknownType_ReturnsFalse()
    {
        object result = TypeSystem.TypeSize(new object[] { "ts-no-exist-d" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void TypeSize_Direct_EmptyArgs_ReturnsFalse()
    {
        object result = TypeSystem.TypeSize(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void TypeSize_Direct_WrongArgType_ReturnsFalse()
    {
        // Long where string is required.
        object result = TypeSystem.TypeSize(new object[] { 42L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TypeFieldOffset - Scheme path
    // =======================================================================
    
    [Fact]
    public void TypeFieldOffset_Scheme_FirstField_OffsetZero()
    {
        "(define-type ts-off-s \"basic\" (\"x\" \"float\") (\"y\" \"float\"))".Eval();
        object? result = "(type-field-offset \"ts-off-s\" \"x\")".Eval();
        Assert.Equal(0L, result);
    }
    
    [Fact]
    public void TypeFieldOffset_Scheme_SecondField_CorrectOffset()
    {
        // x(float=4) -> y starts at offset 4.
        "(define-type ts-off2-s \"basic\" (\"x\" \"float\") (\"y\" \"int32\"))".Eval();
        object? result = "(type-field-offset \"ts-off2-s\" \"y\")".Eval();
        Assert.Equal(4L, result);
    }
    
    [Fact]
    public void TypeFieldOffset_Scheme_UnknownType_ReturnsFalse()
    {
        object? result = "(type-field-offset \"ts-noexist-off-s\" \"x\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void TypeFieldOffset_Scheme_UnknownField_ReturnsFalse()
    {
        "(define-type ts-badfield-s \"basic\" (\"x\" \"float\"))".Eval();
        object? result = "(type-field-offset \"ts-badfield-s\" \"zzz\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TypeFieldOffset - C# backing path
    // =======================================================================
    
    [Fact]
    public void TypeFieldOffset_Direct_MultiFieldLayout_OffsetsAccumulate()
    {
        // bool(1) + int16(2) + int32(4) -> offsets 0, 1, 3.
        TypeSystem.DefineType(new object[]
        {
            "ts-layout-d",
            "basic",
            MakeFieldPair("flag",  "bool"),
            MakeFieldPair("val16", "int16"),
            MakeFieldPair("val32", "int32"),
        });
        
        object off0 = TypeSystem.TypeFieldOffset(new object[] { "ts-layout-d", "flag"  });
        object off1 = TypeSystem.TypeFieldOffset(new object[] { "ts-layout-d", "val16" });
        object off2 = TypeSystem.TypeFieldOffset(new object[] { "ts-layout-d", "val32" });
        
        Assert.Equal(0L, off0);
        Assert.Equal(1L, off1);
        Assert.Equal(3L, off2);
    }
    
    [Fact]
    public void TypeFieldOffset_Direct_EmptyArgs_ReturnsFalse()
    {
        object result = TypeSystem.TypeFieldOffset(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void TypeFieldOffset_Direct_WrongArgTypes_ReturnsFalse()
    {
        // Both args are longs; string required for both.
        object result = TypeSystem.TypeFieldOffset(new object[] { 1L, 2L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TypeParent - Scheme path
    // =======================================================================
    
    [Fact]
    public void TypeParent_Scheme_RegisteredType_ReturnsParentString()
    {
        "(define-type ts-parent-s \"structure\")".Eval();
        object? result = "(type-parent \"ts-parent-s\")".Eval();
        Assert.Equal("structure", result);
    }
    
    [Fact]
    public void TypeParent_Scheme_UnknownType_ReturnsFalse()
    {
        object? result = "(type-parent \"ts-noexist-parent-s\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TypeParent - C# backing path
    // =======================================================================
    
    [Fact]
    public void TypeParent_Direct_ReturnsCorrectParent()
    {
        TypeSystem.DefineType(new object[] { "ts-parent-d", "basic" });
        object result = TypeSystem.TypeParent(new object[] { "ts-parent-d" });
        Assert.Equal("basic", result);
    }
    
    [Fact]
    public void TypeParent_Direct_EmptyArgs_ReturnsFalse()
    {
        object result = TypeSystem.TypeParent(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void TypeParent_Direct_WrongArgType_ReturnsFalse()
    {
        object result = TypeSystem.TypeParent(new object[] { 99L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // SetMethod / GetMethod - Scheme path
    // =======================================================================
    
    [Fact]
    public void SetMethod_Scheme_ValidArgs_ReturnsTrueAndMethodIsRetrievable()
    {
        "(define-type ts-vtable-s \"basic\")".Eval();
        
        // Store a lambda at slot 0.
        object? setResult = "(method-set! \"ts-vtable-s\" 0 (lambda () 42))".Eval();
        Assert.True(IsTrue(setResult));
        
        // Retrieve and call it; result should be 42.
        object? getResult = "((method-get \"ts-vtable-s\" 0))".Eval();
        Assert.Equal(42L, getResult);
    }
    
    [Fact]
    public void SetMethod_Scheme_UnknownType_ReturnsFalse()
    {
        object? result = "(method-set! \"ts-no-such-type-s\" 0 (lambda () #f))".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetMethod_Scheme_EmptySlot_ReturnsFalse()
    {
        "(define-type ts-emptyslot-s \"basic\")".Eval();
        object? result = "(method-get \"ts-emptyslot-s\" 99)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetMethod_Scheme_OverwritesExistingSlot()
    {
        "(define-type ts-overwrite-s \"basic\")".Eval();
        "(method-set! \"ts-overwrite-s\" 1 (lambda () 1))".Eval();
        "(method-set! \"ts-overwrite-s\" 1 (lambda () 2))".Eval();
        
        // Only the most recent write should survive.
        object? result = "((method-get \"ts-overwrite-s\" 1))".Eval();
        Assert.Equal(2L, result);
    }
    
    // =======================================================================
    // SetMethod / GetMethod - C# backing path
    // =======================================================================
    
    [Fact]
    public void SetMethod_Direct_ValidArgs_ReturnsTrue()
    {
        TypeSystem.DefineType(new object[] { "ts-setm-d", "basic" });
        object proc = "(lambda () 7)".Eval()!;
        object result = TypeSystem.SetMethod(new object[] { "ts-setm-d", 0L, proc });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetMethod_Direct_UnknownType_ReturnsFalse()
    {
        object proc = "(lambda () 0)".Eval()!;
        object result = TypeSystem.SetMethod(new object[] { "ts-setm-noexist-d", 0L, proc });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetMethod_Direct_EmptyArgs_ReturnsFalse()
    {
        object result = TypeSystem.SetMethod(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetMethod_Direct_WrongTypeNameType_ReturnsFalse()
    {
        // Long where string is required for type name.
        object proc = "(lambda () 0)".Eval()!;
        object result = TypeSystem.SetMethod(new object[] { 42L, 0L, proc });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetMethod_Direct_NonConvertibleMethodId_ReturnsFalse()
    {
        TypeSystem.DefineType(new object[] { "ts-badid-d", "basic" });
        object proc = "(lambda () 0)".Eval()!;
        // String that can't be converted to int - the catch block fires.
        object result = TypeSystem.SetMethod(new object[] { "ts-badid-d", "not-an-int", proc });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetMethod_Direct_EmptySlot_ReturnsFalse()
    {
        TypeSystem.DefineType(new object[] { "ts-getm-empty-d", "basic" });
        object result = TypeSystem.GetMethod(new object[] { "ts-getm-empty-d", 0L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetMethod_Direct_FilledSlot_ReturnsSameObject()
    {
        TypeSystem.DefineType(new object[] { "ts-getm-filled-d", "basic" });
        object proc = "(lambda () 99)".Eval()!;
        TypeSystem.SetMethod(new object[] { "ts-getm-filled-d", 5L, proc });
        
        object result = TypeSystem.GetMethod(new object[] { "ts-getm-filled-d", 5L });
        Assert.Same(proc, result);
    }
    
    [Fact]
    public void GetMethod_Direct_UnknownType_ReturnsFalse()
    {
        object result = TypeSystem.GetMethod(new object[] { "ts-getm-noexist-d", 0L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetMethod_Direct_EmptyArgs_ReturnsFalse()
    {
        object result = TypeSystem.GetMethod(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // IsType - Scheme path
    // =======================================================================
    
    [Fact]
    public void IsType_Scheme_MatchingCLRTypeName_ReturnsTrue()
    {
        // IronScheme boxes integer literals as System.Int64 -> CLR name "Int64".
        object? result = "(is-type? 42 \"Int64\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void IsType_Scheme_NonMatchingCLRTypeName_ReturnsFalse()
    {
        object? result = "(is-type? 42 \"String\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsType_Scheme_FloatIsDouble()
    {
        // IronScheme flonum literals box as System.Double.
        object? result = "(is-type? 3.14 \"Double\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void IsType_Scheme_StringObject()
    {
        object? result = "(is-type? \"hello\" \"String\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // IsType - C# backing path
    // =======================================================================
    
    [Fact]
    public void IsType_Direct_MatchingType_ReturnsTrue()
    {
        // Pass a C# long; CLR name is "Int64".
        object result = TypeSystem.IsType(new object[] { 42L, "Int64" });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void IsType_Direct_NonMatchingType_ReturnsFalse()
    {
        object result = TypeSystem.IsType(new object[] { 42L, "String" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsType_Direct_NullObject_ReturnsFalse()
    {
        // null object - the guard short-circuits before GetType().
        object result = TypeSystem.IsType(new object[] { null!, "Object" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsType_Direct_NoInheritanceWalk_SubclassDoesNotMatchParentName()
    {
        // IsType is a flat CLR name check; a subclass must not match its parent's name.
        // System.Int32 vs "Int64" - names are distinct.
        object result = TypeSystem.IsType(new object[] { (int)1, "Int64" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsType_Direct_EmptyArgs_ReturnsFalse()
    {
        object result = TypeSystem.IsType(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsType_Direct_WrongTypeNameArgType_ReturnsFalse()
    {
        // Second arg (type name) must be string; pass a long instead.
        object result = TypeSystem.IsType(new object[] { "hello", 42L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // TypeOf - Scheme path
    // =======================================================================
    
    [Fact]
    public void TypeOf_Scheme_IntegerLiteral_ReturnsInt64()
    {
        object? result = "(type-of 1)".Eval();
        Assert.Equal("Int64", result);
    }
    
    [Fact]
    public void TypeOf_Scheme_FloatLiteral_ReturnsDouble()
    {
        object? result = "(type-of 1.0)".Eval();
        Assert.Equal("Double", result);
    }
    
    [Fact]
    public void TypeOf_Scheme_StringLiteral_ReturnsString()
    {
        object? result = "(type-of \"hello\")".Eval();
        Assert.Equal("String", result);
    }
    
    [Fact]
    public void TypeOf_Scheme_BooleanTrue_ReturnsBoolean()
    {
        object? result = "(type-of #t)".Eval();
        Assert.Equal("Boolean", result);
    }
    
    // =======================================================================
    // TypeOf - C# backing path
    // =======================================================================
    
    [Fact]
    public void TypeOf_Direct_LongValue_ReturnsInt64()
    {
        object result = TypeSystem.TypeOf(new object[] { 42L });
        Assert.Equal("Int64", result);
    }
    
    [Fact]
    public void TypeOf_Direct_DoubleValue_ReturnsDouble()
    {
        object result = TypeSystem.TypeOf(new object[] { 3.14 });
        Assert.Equal("Double", result);
    }
    
    [Fact]
    public void TypeOf_Direct_StringValue_ReturnsString()
    {
        object result = TypeSystem.TypeOf(new object[] { "hello" });
        Assert.Equal("String", result);
    }
    
    [Fact]
    public void TypeOf_Direct_NullValue_ReturnsNullString()
    {
        object result = TypeSystem.TypeOf(new object[] { null! });
        Assert.Equal("null", result);
    }
    
    [Fact]
    public void TypeOf_Direct_EmptyArgs_ReturnsNullString()
    {
        // args.Length < 1 -> args[0] is null branch -> "null".
        object result = TypeSystem.TypeOf(Array.Empty<object>());
        Assert.Equal("null", result);
    }
    
    [Fact]
    public void TypeOf_Direct_BooleanValue_ReturnsBoolean()
    {
        object result = TypeSystem.TypeOf(new object[] { true });
        Assert.Equal("Boolean", result);
    }
}

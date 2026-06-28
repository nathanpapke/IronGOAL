using System.Collections.Concurrent;
using IronScheme;
using IronScheme.Runtime;

namespace IronGOAL.Backing;

public static class TypeSystem
{
    // =======================================================================
    // TYPE TABLE
    // =======================================================================
    
    /// <summary>
    /// Internal record for a registered GOAL type.
    /// </summary>
    private sealed class TypeRecord
    {
        /// <summary>Name of the parent type ("basic", "structure", etc.).</summary>
        public string Parent { get; }
        
        /// <summary>
        /// Ordered field list: (fieldName, typeName, byteOffset).
        /// Offsets are computed sequentially, no padding, at registration time.
        /// </summary>
        public IReadOnlyList<(string Name, string Type, int Offset)> Fields { get; }
        
        /// <summary>Total byte size of the type (sum of field sizes).</summary>
        public int Size { get; }
        
        /// <summary>
        /// Vtable: methodId -> Scheme callable (IronScheme <c>Callable</c> or
        /// any object the caller deposits).
        /// </summary>
        public ConcurrentDictionary<int, object> Methods { get; } = new();
        
        /// <summary>
        /// Method name -> vtable index, populated at <c>deftype</c> time from
        /// the <c>:methods</c> block.  Used by <c>method-id</c> for reverse lookup.
        /// </summary>
        public ConcurrentDictionary<string, int> MethodIds { get; } = new();
        
        public TypeRecord(
            string parent,
            IReadOnlyList<(string Name, string Type, int Offset)> fields,
            int size)
        {
            Parent = parent;
            Fields = fields;
            Size   = size;
        }
    }
    
    // Key = type name (e.g. "enemy-info").
    private static readonly ConcurrentDictionary<string, TypeRecord> _types = new();
    
    // =======================================================================
    // PRIMITIVE SIZES
    // =======================================================================
    
    /// <summary>
    /// Maps GOAL/DC primitive type names to their byte sizes.
    /// Unknown type names are treated as 4-byte pointer-sized references,
    /// consistent with GOAL's pointer-size default.
    /// </summary>
    private static int PrimitiveSize(string typeName) => typeName switch
    {
        "int8"   or "uint8"  or "bool"   => 1,
        "int16"  or "uint16"             => 2,
        "int32"  or "uint32" or "int"
            or "float"  => 4,
        "int64"  or "uint64"             => 8,
        "int128" or "uint128"            => 16,
        // Registered user types: look up their size; fall back to 4 (pointer).
        _ => _types.TryGetValue(typeName, out var rec) ? rec.Size : 4,
    };
    
    // =======================================================================
    // TYPE
    // =======================================================================
    
    /// <summary>
    /// Registers a new type in the IronGOAL type table and returns its
    /// computed byte size.
    /// 
    /// <para>Scheme: <c>(define-type name parent field-pair...)</c></para>
    /// </summary>
    public static object DefineType(object[] args)
    {
        // Guard: need at least type name + parent name.
        if (args.Length < 2 || args[0] is not string typeName || args[1] is not string parent)
            return "#f".Eval();
        
        // Parse variadic field pairs starting at args[2].
        // Each arg is either:
        //   - A field Cons pair: ("field-name" . ("field-type" . ()))
        //   - A :methods block Cons: (":methods" . (<method-entry> ...))
        //     where each method entry is a Cons whose car is the method name string.
        var fields  = new List<(string Name, string Type, int Offset)>();
        var methodIds  = new Dictionary<string, int>();
        int offset  = 0;
        
        for (int i = 2; i < args.Length; i++)
        {
            // Each field arg must be a Cons pair: ("field-name" . ("field-type" . ()))
            if (args[i] is not Cons pair)
                continue;
            
            // Check for :methods keyword block.
            var carStr = pair.car as string
                         ?? (pair.car?.ToString() ?? string.Empty);
            
            if (carStr == ":methods")
            {
                // Walk the method list; assign IDs sequentially from 0.
                int methodId = 0;
                var methodList = pair.cdr;
                while (methodList is Cons methodCons)
                {
                    // Each entry: (method-name arg-types... return-type)
                    // We only need the name (car).
                    if (methodCons.car is Cons entryPair)
                    {
                        var methodName = entryPair.car as string
                                         ?? entryPair.car?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(methodName))
                            methodIds[methodName] = methodId;
                        methodId++;
                    }
                    methodList = methodCons.cdr;
                }
                continue;
            }
            
            // car = field name
            if (pair.car is not string fieldName)
                continue;
            
            // cadr = field type  (pair.cdr is itself a Cons whose car is the type string)
            if (pair.cdr is not Cons cdrCons || cdrCons.car is not string fieldType)
                continue;
            
            int size = PrimitiveSize(fieldType);
            fields.Add((fieldName, fieldType, offset));
            offset += size;
        }
        
        var record = new TypeRecord(parent, fields.AsReadOnly(), offset);
        foreach (var (name, id) in methodIds)
            record.MethodIds[name] = id;
        _types[typeName] = record;
        
        // Return computed byte size, matching what GOAL's TypeFlags.size holds
        // after parse_deftype completes.
        return (long)offset;
    }
    
    /// <summary>
    /// Returns the total byte size of a registered type.
    /// Returns <c>#f</c> if the type is unknown.
    ///
    /// <para>Scheme: <c>(type-size "type-name")</c></para>
    /// </summary>
    public static object TypeSize(object[] args)
    {
        if (args.Length < 1 || args[0] is not string typeName)
            return "#f".Eval();
        
        if (!_types.TryGetValue(typeName, out var record))
            return "#f".Eval();
        
        return (long)record.Size;
    }
    
    /// <summary>
    /// Returns the byte offset of a named field within a registered type.
    /// Returns <c>#f</c> if the type or field is unknown.
    ///
    /// <para>Scheme: <c>(type-field-offset "type-name" "field-name")</c></para>
    /// </summary>
    public static object TypeFieldOffset(object[] args)
    {
        if (args.Length < 2 || args[0] is not string typeName || args[1] is not string fieldName)
            return "#f".Eval();
        
        if (!_types.TryGetValue(typeName, out var record))
            return "#f".Eval();
        
        foreach (var (name, _, offset) in record.Fields)
        {
            if (name == fieldName)
                return (long)offset;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns the parent type name of a registered type.
    /// Returns <c>#f</c> if the type is unknown.
    ///
    /// <para>Scheme: <c>(type-parent "type-name")</c></para>
    /// </summary>
    public static object TypeParent(object[] args)
    {
        if (args.Length < 1 || args[0] is not string typeName)
            return "#f".Eval();
        
        if (!_types.TryGetValue(typeName, out var record))
            return "#f".Eval();
        
        return record.Parent;
    }
    
    // =======================================================================
    // METHOD
    // =======================================================================
    
    /// <summary>
    /// Stores a Scheme procedure in the vtable for a given type and method id.
    /// Mirrors GOAL's <c>method-set!</c> function.
    /// Returns <c>#t</c> on success, <c>#f</c> if the type is unknown.
    ///
    /// <para>Scheme: <c>(method-set! "type-name" method-id proc)</c></para>
    /// </summary>
    public static object SetMethod(object[] args)
    {
        if (args.Length < 3 || args[0] is not string typeName)
            return "#f".Eval();
        
        int methodId;
        try   { methodId = Convert.ToInt32(args[1]); }
        catch { return "#f".Eval(); }
        
        if (!_types.TryGetValue(typeName, out var record))
            return "#f".Eval();
        
        record.Methods[methodId] = args[2];
        return true;
    }
    
    /// <summary>
    /// Retrieves the Scheme procedure stored at a given vtable slot.
    /// Returns <c>#f</c> if the type is unknown or the slot is empty.
    ///
    /// <para>Scheme: <c>(method-get "type-name" method-id)</c></para>
    /// </summary>
    public static object GetMethod(object[] args)
    {
        if (args.Length < 2 || args[0] is not string typeName)
            return "#f".Eval();
        
        int methodId;
        try   { methodId = Convert.ToInt32(args[1]); }
        catch { return "#f".Eval(); }
        
        if (!_types.TryGetValue(typeName, out var record))
            return "#f".Eval();
        
        return record.Methods.TryGetValue(methodId, out var proc)
            ? proc
            : "#f".Eval();
    }
    
    /// <summary>
    /// Returns the integer vtable index for a named method on a registered type,
    /// as declared in the <c>:methods</c> block of its <c>deftype</c>.
    /// Returns <c>#f</c> if the type is unknown or the method name was not
    /// declared in its <c>deftype</c>.
    /// Mirrors GOAL's <c>method-id</c> kernel call used for virtual dispatch
    /// in <c>.gc</c> scripts.
    ///
    /// <para>Scheme: <c>(method-id "type-name" "method-name")</c></para>
    /// </summary>
    public static object MethodId(object[] args)
    {
        if (args.Length < 2 || args[0] is not string typeName || args[1] is not string methodName)
            return "#f".Eval();
        
        if (!_types.TryGetValue(typeName, out var record))
            return "#f".Eval();
        
        return record.MethodIds.TryGetValue(methodName, out var id)
            ? (long)id
            : "#f".Eval();
    }
    
    // =======================================================================
    // TYPE CHECK
    // =======================================================================
    
    /// <summary>
    /// Returns <c>#t</c> if the CLR runtime type name of <paramref name="obj"/>
    /// (via <see cref="object.GetType()"/>) matches <paramref name="typeName"/>
    /// exactly (flat equality, no inheritance walk).
    ///
    /// <para>Scheme: <c>(type-type? obj "type-name")</c></para>
    /// </summary>
    public static object IsType(object[] args)
    {
        if (args.Length < 2 || args[1] is not string typeName)
            return "#f".Eval();
        
        if (args[0] is null)
            return false;
        
        return args[0].GetType().Name == typeName;
    }
    
    /// <summary>
    /// Returns the CLR runtime type name of an object as a string, exactly
    /// as <see cref="Type.Name"/> reports it (e.g. <c>"Double"</c>,
    /// <c>"String"</c>, <c>"Vector3"</c>).
    ///
    /// <para>Scheme: <c>(type-of obj)</c></para>
    /// </summary>
    public static object TypeOf(object[] args)
    {
        if (args.Length < 1 || args[0] is null)
            return "null";
        
        return args[0].GetType().Name;
    }
}

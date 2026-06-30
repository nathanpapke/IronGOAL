using System;

namespace IronGOAL.Bus;

// ===========================================================================
// Opcode - consolidated Param0 operation discriminator
// ===========================================================================
//
// Every GameEvent that carries a sub-operation encodes it as a single byte in
// Param0.  That byte is laid out as:
//
//     bit:  7  6  5  4 | 3  2  1 | 0
//           └─ordinal─┘ └─cat──┘   └ direction
//
//     value = (ordinal << 4) | (category << 1) | direction
//
//   • direction (bit 0):  0 = Command (even), 1 = Query (odd).
//                         Doubles as the GameEventType selector -
//                         Query -> EntityQuery, Command → EntitySetState.
//   • category  (bits 1-3): which backing system owns the op (8 slots, 5 used).
//   • ordinal   (bits 4-7): operation index within its (category, direction)
//                           bucket; 0 is the most basic op in that bucket.
//
// Values are unique by construction: no two operations share the same
// (ordinal, category, direction) triple, so the set is self-validating and the
// host drain switch can branch on the byte with no ambiguity.
//
// Ordinal is 4 bits, so each (category, direction) bucket holds at most 16
// operations.  Entity queries are the largest bucket at 15 (ordinals 0-14),
// leaving one slot before a widening would be required.
//
// Invalid (0xFF) is the reserved sentinel: category 7 / ordinal 15, outside
// every assigned slot.  Use it for "no opcode" / "unrecognised"; default(Opcode)
// is GameMemory.Serialize (0x00), NOT a sentinel, so never treat zero as empty.
// ===========================================================================

public enum OpcodeDirection : byte
{
    Command = 0,
    Query   = 1,
}

public enum OpcodeCategory : byte
{
    GameMemory = 0,
    Entity     = 1,
    Asset      = 2,
    File       = 3,
    Audio      = 4,
    // 5-7 reserved (7 is used by the Invalid sentinel's category bits)
}

public enum Opcode : byte
{
    // =======================================================================
    // Category 0 - GameMemory
    // =======================================================================
    
    // Commands
    Serialize            = 0x00,
    Deserialize          = 0x10,
    // Queries
    Alloc                = 0x01,
    NewDynamicStructure  = 0x11,
    HeapBytesUsed        = 0x21,
    HeapBytesTotal       = 0x31,
    
    // =======================================================================
    // Category 1 - Entity
    // =======================================================================
    
    // Commands
    SetProperty          = 0x02,
    AddTag               = 0x12,
    RemoveTag            = 0x22,
    BindProcess          = 0x32,
    // Queries
    Exists               = 0x03,
    GetPosition          = 0x13,
    GetRotation          = 0x23,
    GetScale             = 0x33,
    GetProperty          = 0x43,
    HasProperty          = 0x53,
    HasComponent         = 0x63,
    GetComponent         = 0x73,
    FindByType           = 0x83,
    FindByTag            = 0x93,
    FindInRadius         = 0xA3,
    FindNearest          = 0xB3,
    HasTag               = 0xC3,
    GetProcess           = 0xD3,
    GetEntity            = 0xE3,
    
    // =======================================================================
    // Category 2 - Asset
    // =======================================================================
    
    // Commands
    Unload               = 0x04,
    // Queries
    Load                 = 0x05,
    LoadObject           = 0x15,
    LoadBinary           = 0x25,
    DgoLoad              = 0x35,
    
    // =======================================================================
    // Category 3 - File
    // =======================================================================
    
    // Commands
    McFormat             = 0x06,
    McUnformat           = 0x16,
    McCreateFile         = 0x26,
    McMakeFile           = 0x36,
    McSave               = 0x46,
    McLoad               = 0x56,
    // Queries
    McRun                = 0x07,
    McGetStatus          = 0x17,
    
    // =======================================================================
    // Category 4 - Audio
    // =======================================================================
    
    // Queries
    DialogIsPlaying      = 0x09,
    
    // =======================================================================
    // Sentinel
    // =======================================================================
    
    Invalid              = 0xFF,
}

/// <summary>
/// Bit-field accessors for <see cref="Opcode"/>.  These decode the same
/// layout the values are packed under, so the host can branch on direction or
/// category without a lookup table.
/// </summary>
public static class OpcodeBits
{
    private const byte DirectionMask = 0b0000_0001;
    private const int  CategoryShift = 1;
    private const byte CategoryMask  = 0b0000_0111; // applied after the shift
    private const int  OrdinalShift  = 4;
    
    /// <summary>0 = Command, 1 = Query (bit 0).</summary>
    public static OpcodeDirection Direction(this Opcode op)
        => (OpcodeDirection)((byte)op & DirectionMask);
    
    /// <summary>Owning backing system (bits 1-3).</summary>
    public static OpcodeCategory Category(this Opcode op)
        => (OpcodeCategory)(((byte)op >> CategoryShift) & CategoryMask);
    
    /// <summary>Operation index within its (category, direction) bucket (bits 4-7).</summary>
    public static int Ordinal(this Opcode op)
        => (byte)op >> OrdinalShift;
    
    /// <summary>True for an odd-valued, valid query opcode.</summary>
    public static bool IsQuery(this Opcode op)
        => op.IsValid() && ((byte)op & DirectionMask) == (byte)OpcodeDirection.Query;
    
    /// <summary>True for an even-valued, valid command opcode.</summary>
    public static bool IsCommand(this Opcode op)
        => op.IsValid() && ((byte)op & DirectionMask) == (byte)OpcodeDirection.Command;
    
    /// <summary>
    /// True if the opcode falls in an assigned category (0-4) and is not the
    /// <see cref="Opcode.Invalid"/> sentinel.  This is a cheap category guard,
    /// not a strict membership test - a byte with a valid category but an
    /// unassigned ordinal (e.g. an Audio query at ordinal 5) passes here.
    /// Use <see cref="Enum.IsDefined(Type, object)"/> where exact membership
    /// matters and the call is not on a hot path.
    /// </summary>
    public static bool IsValid(this Opcode op)
        => op != Opcode.Invalid && op.Category() <= OpcodeCategory.Audio;
}

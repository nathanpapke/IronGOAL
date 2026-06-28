using System;
using System.Numerics;
using System.Text;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;

namespace IronGOAL.Backing;

public class DebugSystem
{
    // =======================================================================
    // BUS REFERENCE + ENABLE GATE
    // =======================================================================
    
    private static EventBus? _bus;
    private static bool      _enabled;
    
    /// <summary>
    /// Called by <c>Kernel</c> after constructing its <see cref="EventBus"/>
    /// and before <c>RegisterAll()</c>.
    /// </summary>
    public static void Install(EventBus bus) => _bus = bus;
    
    /// <summary>
    /// Called by <c>Kernel</c> with <see cref="GoalRuntimeConfig.EnableDebugChannel"/>
    /// before <c>RegisterAll()</c>.  When <c>false</c>, <see cref="PublishDebug"/>
    /// is a no-op; methods still compute and return their values so that
    /// <c>(_format #f ...)</c> string-building paths continue to work in
    /// ship builds.
    /// </summary>
    public static void Configure(bool enabled) => _enabled = enabled;
    
    // =======================================================================
    // INTERNAL PUBLISH HELPER
    // =======================================================================
    
    private static void PublishDebug(DebugCommand cmd)
    {
        if (!_enabled || _bus is null) return;
    
        long  frameId  = GameClock.FrameCount(Array.Empty<object>()) is long fc ? fc : 0L;
        float gameTime = GameClock.TotalTime(Array.Empty<object>())  is float gt ? gt : 0f;
        _bus.PublishDebug(cmd, frameId, gameTime);
    }
    
    // =======================================================================
    // DEBUG FUNCTIONS
    // =======================================================================
    
    /// <summary>
    /// Prints a Scheme object to the debug channel and returns it unchanged,
    /// mirroring GOAL's <c>print</c> which writes to the print buffer and
    /// returns its argument.
    /// <para>Scheme: <c>(print obj)</c></para>
    /// </summary>
    public static object Print(object[] args)
    {
        if (args.Length == 0) return "()".Eval();
        
        object obj    = args[0];
        string output = SchemeWrite(obj);
        
        PublishDebug(new DebugCommand
        {
            Type         = DebugCommandType.Log,
            SourceSymbol = "print",
            Message      = output,
        });
        
        return obj;
    }
    
    /// <summary>
    /// Produces a typed field-dump string for the given object, matching
    /// GOAL's <c>inspect_*</c> output style (<c>[hex] typename value</c>),
    /// publishes it as <see cref="DebugCommandType.Inspect"/>, and returns
    /// the original object (GOAL's <c>inspect</c> always returns its arg).
    /// <para>Scheme: <c>(inspect obj)</c></para>
    /// </summary>
    public static object Inspect(object[] args)
    {
        if (args.Length == 0) return "()".Eval();
        
        object obj    = args[0];
        string dump   = BuildInspectString(obj);
        
        PublishDebug(new DebugCommand
        {
            Type         = DebugCommandType.Inspect,
            SourceSymbol = "inspect",
            Message      = dump,
        });
        
        return obj;
    }
    
    /// <summary>
    /// GOAL-compatible string formatter.
    /// <list type="bullet">
    ///   <item><description>
    ///     First argument <c>#t</c>: formats remaining args into the fmt
    ///     string, publishes the result as a <see cref="DebugCommandType.Log"/>
    ///     command, and returns <c>#t</c>.
    ///   </description></item>
    ///   <item><description>
    ///     First argument <c>#f</c>: formats and returns the result string
    ///     without publishing.  This is the string-building path used
    ///     pervasively in <c>.gc</c> scripts.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Supported directives: <c>~a</c> (display/ToString), <c>~s</c>
    /// (Scheme write-form), <c>~%</c> (newline).  Unknown directives are
    /// passed through literally.
    /// </para>
    /// <para>Scheme: <c>(_format #t "pos=~a hp=~a~%" pos hp)</c></para>
    /// <para>Scheme: <c>(_format #f "~a-~a" prefix suffix)</c></para>
    /// </summary>
    public static object Format(object[] args)
    {
        // Need at least destination + format string.
        if (args.Length < 2) return "#f".Eval();
        
        object dest      = args[0];
        bool   toChannel = dest is bool b && b; // #t → publish; #f -> return string
        
        if (args[1] is not string fmt) return "#f".Eval();
        
        // Collect format arguments (everything after fmt).
        object[] fmtArgs = args.Length > 2
            ? args[2..]
            : Array.Empty<object>();
        
        string result = GoalFormat(fmt, fmtArgs);
        
        if (toChannel)
        {
            PublishDebug(new DebugCommand
            {
                Type         = DebugCommandType.Log,
                SourceSymbol = "_format",
                Message      = result,
            });
            return "#t".Eval();
        }
        else
        {
            // Return the formatted string as a Scheme string object.
            // IronScheme treats a bare CLR string as a Scheme string.
            return result;
        }
    }
    
    // =======================================================================
    // PRIVATE HELPERS
    // =======================================================================
    
    /// <summary>
    /// Converts a CLR object to its Scheme write-representation string,
    /// matching what GOAL's print buffer output looks like.
    /// </summary>
    private static string SchemeWrite(object? obj) => obj switch
    {
        null              => "()",
        bool true_val     => true_val ? "#t" : "#f",
        string s          => $"\"{s}\"",
        long l            => l.ToString(),
        int i             => i.ToString(),
        double d          => d.ToString("G"),
        float f           => f.ToString("G"),
        Vector3 v         => $"({v.X} {v.Y} {v.Z})",
        Vector4 v         => $"({v.X} {v.Y} {v.Z} {v.W})",
        Quaternion q      => $"(quat {q.X} {q.Y} {q.Z} {q.W})",
        Matrix4x4 _       => "[matrix4x4]",
        _                 => obj.ToString() ?? "()",
    };
    
    /// <summary>
    /// Produces a GOAL-style inspect dump string for the given object.
    /// Format: <c>[hex-or-type] typename value\n\tfield: value\n...</c>
    /// For CLR types that have no meaningful GOAL-side struct layout, a
    /// compact single-line form is used instead.
    /// </summary>
    private static string BuildInspectString(object obj)
    {
        var sb = new StringBuilder();
        
        switch (obj)
        {
            case long l:
                sb.AppendLine($"[{l:x16}] fixnum {l}");
                break;
            
            case int i:
                sb.AppendLine($"[{(uint)i:x8}] fixnum {i}");
                break;
            
            case double d:
                sb.AppendLine($"[float] float {d:G}");
                break;
            
            case float f:
                sb.AppendLine($"[float] float {f:G}");
                break;
            
            case bool bv:
                sb.AppendLine($"[symbol] {(bv ? "#t" : "#f")}");
                break;
            
            case string s:
                sb.AppendLine($"[string] \"{s}\"");
                break;
            
            case Vector3 v:
                sb.AppendLine("[vector3] vector3");
                sb.AppendLine($"\tx: {v.X}");
                sb.AppendLine($"\ty: {v.Y}");
                sb.AppendLine($"\tz: {v.Z}");
                break;
            
            case Vector4 v:
                sb.AppendLine("[vector4] vector4");
                sb.AppendLine($"\tx: {v.X}");
                sb.AppendLine($"\ty: {v.Y}");
                sb.AppendLine($"\tz: {v.Z}");
                sb.AppendLine($"\tw: {v.W}");
                break;
            
            case Quaternion q:
                sb.AppendLine("[quaternion] quaternion");
                sb.AppendLine($"\tx: {q.X}");
                sb.AppendLine($"\ty: {q.Y}");
                sb.AppendLine($"\tz: {q.Z}");
                sb.AppendLine($"\tw: {q.W}");
                break;
            
            case Matrix4x4 m:
                sb.AppendLine("[matrix4x4] matrix4x4");
                sb.AppendLine($"\trow0: ({m.M11} {m.M12} {m.M13} {m.M14})");
                sb.AppendLine($"\trow1: ({m.M21} {m.M22} {m.M23} {m.M24})");
                sb.AppendLine($"\trow2: ({m.M31} {m.M32} {m.M33} {m.M34})");
                sb.AppendLine($"\trow3: ({m.M41} {m.M42} {m.M43} {m.M44})");
                break;
            
            default:
                sb.AppendLine($"[structure] {obj.GetType().Name}");
                sb.AppendLine($"\t{obj}");
                break;
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Applies GOAL <c>_format</c> directives to <paramref name="fmt"/>,
    /// consuming <paramref name="fmtArgs"/> in order.
    /// <list type="bullet">
    ///   <item><c>~a</c> - display (ToString / SchemeDisplay)</item>
    ///   <item><c>~s</c> - write (SchemeWrite)</item>
    ///   <item><c>~%</c> - newline</item>
    ///   <item>unknown - emitted literally as <c>~x</c></item>
    /// </list>
    /// </summary>
    private static string GoalFormat(string fmt, object[] fmtArgs)
    {
        var sb      = new StringBuilder(fmt.Length + 16);
        int argIdx  = 0;
        int i       = 0;
        
        while (i < fmt.Length)
        {
            if (fmt[i] == '~' && i + 1 < fmt.Length)
            {
                char dir = fmt[i + 1];
                switch (dir)
                {
                    case 'a':
                    case 'A':
                        sb.Append(argIdx < fmtArgs.Length
                            ? SchemeDisplay(fmtArgs[argIdx++])
                            : "");
                        break;
                    
                    case 's':
                    case 'S':
                        sb.Append(argIdx < fmtArgs.Length
                            ? SchemeWrite(fmtArgs[argIdx++])
                            : "");
                        break;
                    
                    case '%':
                        sb.Append('\n');
                        break;
                    
                    default:
                        // Unknown directive - pass through literally
                        sb.Append('~');
                        sb.Append(dir);
                        break;
                }
                i += 2;
            }
            else
            {
                sb.Append(fmt[i]);
                i++;
            }
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Scheme display representation - like write but strings are unquoted.
    /// Used by <c>~a</c> in <c>_format</c>.
    /// </summary>
    private static string SchemeDisplay(object? obj) => obj switch
    {
        null          => "()",
        bool bv       => bv ? "#t" : "#f",
        string s      => s,          // display: no quotes
        _             => SchemeWrite(obj),
    };
}

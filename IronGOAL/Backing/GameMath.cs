using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using IronScheme;

namespace IronGOAL.Backing;

/// <summary>
/// SIMD-backed game math primitives.  Equivalent to GOAL's engine/math/ types.
/// All vectors and matrices are passed as float arrays at the Scheme boundary;
/// internally the methods delegate to System.Numerics value types for hardware
/// acceleration.
/// </summary>
public static class GameMath
{
    // =======================================================================
    // CONSTANTS
    // =======================================================================
    
    /// <summary>GOAL fixed-point scale: 1 unit == 1/4096 metre.</summary>
    public const float UnitsPerMeter = 4096f;
    
    private const float Deg2RadF = MathF.PI / 180f;
    private const float Rad2DegF = 180f / MathF.PI;
    
    // =======================================================================
    // THREAD-LOCAL RANDOM (avoids lock overhead in per-frame script calls)
    // =======================================================================
    
    [ThreadStatic]
    private static Random? _rng;
    
    private static Random Rng
    {
        get
        {
            if (_rng is null)
            {
                // Seed from CSPRNG
                // Each thread gets a distinct sequence.
                Span<byte> seed = stackalloc byte[4];
                RandomNumberGenerator.Fill(seed);
                _rng = new Random(BitConverter.ToInt32(seed));
            }
            return _rng;
        }
    }
    
    // =======================================================================
    // TRANSFORM HANDLE POOL
    // =======================================================================
    
    private record struct TransformData(Vector3 Position, Quaternion Rotation, Vector3 Scale);
    
    private static readonly ConcurrentDictionary<long, TransformData> _transforms = new();
    private static long _nextHandle;
    
    private static TransformData GetTransform(long handle)
    {
        if (!_transforms.TryGetValue(handle, out var t))
            throw new ArgumentException($"Invalid transform handle: {handle}");
        return t;
    }
    
    // =======================================================================
    // NUMERICS
    // =======================================================================
    
    /// <summary>
    /// Extracts a float from a Scheme numeric argument.  IronScheme's flonum
    /// literals (e.g. 1.0) are boxed as System.Double, not System.Single, so
    /// any GameMath function taking a raw scalar from a .gc file must accept
    /// both.  Returns null for anything else.
    /// </summary>
    private static float? AsFloat(object? o) => o switch
    {
        float  f => f,
        double d => (float)d,
        long   l => l,
        int    i => i,
        _        => null
    };
    
    /// <summary>
    /// Extracts an exact integer from a Scheme argument.  Only int/long -
    /// deliberately does NOT accept float/double, so passing a flonum where
    /// an exact integer is required still correctly returns #f.
    /// </summary>
    private static int? AsInt(object? o) => o switch
    {
        int  i => i,
        long l => (int)l,
        _      => null
    };
    
    // =======================================================================
    // VECTOR 3
    // =======================================================================
    
    /// <summary>
    /// Constructs a Vector3.
    /// Scheme: <c>(vec3 x y z)</c>
    /// </summary>
    public static object Vec3(object[] args)
    {
        float? x = args.Length > 0 ? AsFloat(args[0]) : null;
        float? y = args.Length > 1 ? AsFloat(args[1]) : null;
        float? z = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (x is float xf && y is float yf && z is float zf)
            return new Vector3(xf, yf, zf);
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Component-wise addition.
        /// Scheme: <c>(vector+ a b)</c>
    /// </summary>
    public static object Vector3Add(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is Vector3 && b is Vector3)
        {
            return Vector3.Add((Vector3)a, (Vector3)b);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Component-wise subtraction.
    /// Scheme: <c>(vector- a b)</c>
    /// </summary>
    public static object Vector3Subtract(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is Vector3 && b is Vector3)
        {
            return Vector3.Subtract((Vector3)a, (Vector3)b);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Scalar multiplication.
    /// Scheme: <c>(vector-scale v s)</c>
    /// </summary>
    public static object Vector3Scale(object[] args)
    {
        var v = args.Length > 0 ? args[0] : null;
        float? s = args.Length > 1 ? AsFloat(args[1]) : null;
        
        if (v is Vector3 vv && s is float sf)
            return Vector3.Multiply(vv, sf);
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Dot product of two Vector3s.
    /// Scheme: <c>(vector-dot a b)</c>
    /// </summary>
    public static object Vector3Dot(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is Vector3 && b is Vector3)
        {
            return Vector3.Dot((Vector3)a, (Vector3)b);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Cross product. Result is perpendicular to both inputs.
    /// Scheme: <c>(vector-cross a b)</c>
    /// </summary>
    public static object Vector3Cross(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is Vector3 && b is Vector3)
        {
            return Vector3.Cross((Vector3)a, (Vector3)b);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Euclidean length (magnitude).
    /// Scheme: <c>(vector-length v)</c>
    /// </summary>
    public static object Vector3Length(object[] args)
    {
        var v = args.Length > 0 ? args[0] : null;
        
        if (v is Vector3)
        {
            return ((Vector3)v).Length();
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns a unit-length copy of v. Returns Vector3.Zero when the input
    /// magnitude is below float epsilon, matching GOAL's safe-normalize behavior.
    /// Scheme: <c>(vector-normalize v)</c>
    /// </summary>
    public static object Vector3Normalize(object[] args)
    {
        var v = args.Length > 0 ? args[0] : null;
        
        if (v is Vector3)
        {
            float len = ((Vector3)v).Length();
            if (len < float.Epsilon)
            {
                return Vector3.Zero;
            }
            return (Vector3)v / len;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Euclidean distance between two points.
    /// Scheme: <c>(vector-distance a b)</c>
    /// </summary>
    public static object Vector3Distance(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is Vector3 && b is Vector3)
        {
            return Vector3.Distance((Vector3)a, (Vector3)b);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Linear interpolation between two Vector3s at parameter t in [0, 1].
    /// Scheme: <c>(vector-lerp a b t)</c>
    /// </summary>
    public static object Vector3Lerp(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        float? t = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (a is Vector3 va && b is Vector3 vb && t is float tf)
            return Vector3.Lerp(va, vb, tf);
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // VECTOR 4
    // =======================================================================
    
    /// <summary>
    /// Constructs a Vector4. GOAL's native <c>vector</c> type includes a w
    /// component; spatial positions use units where value / 4096 == meters.
    /// Scheme: <c>(vec4 x y z w)</c>
    /// </summary>
    public static object Vec4(object[] args)
    {
        float? x = args.Length > 0 ? AsFloat(args[0]) : null;
        float? y = args.Length > 1 ? AsFloat(args[1]) : null;
        float? z = args.Length > 2 ? AsFloat(args[2]) : null;
        float? w = args.Length > 3 ? AsFloat(args[3]) : null;
        
        if (x is float xf && y is float yf && z is float zf && w is float wf)
            return new Vector4(xf, yf, zf, wf);
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // QUATERNION
    // =======================================================================
    
    /// <summary>
    /// Returns the identity quaternion (no rotation).
    /// Scheme: <c>(quat-identity)</c>
    /// </summary>
    public static object QuatIdentity(object[] args)
    {
        return Quaternion.Identity;
    }
    
    /// <summary>
    /// Constructs a quaternion from an axis vector and a rotation angle in
    /// degrees.  The axis does not need to be pre-normalised.
    /// Scheme: <c>(quat-from-axis-angle axis deg)</c>
    /// </summary>
    public static object QuatFromAxisAngle(object[] args)
    {
        var axis     = args.Length > 0 ? args[0] : null;
        float? angleDeg = args.Length > 1 ? AsFloat(args[1]) : null;
        
        if (axis is Vector3 a && angleDeg is float deg)
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(a), deg * Deg2RadF);
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Constructs a quaternion from Euler angles in degrees (pitch, yaw, roll
    /// applied in that order — matches GOAL's convention).
    /// Scheme: <c>(quat-from-euler pitch yaw roll)</c>
    /// </summary>
    public static object QuatFromEuler(object[] args)
    {
        float? pitchDeg = args.Length > 0 ? AsFloat(args[0]) : null;
        float? yawDeg   = args.Length > 1 ? AsFloat(args[1]) : null;
        float? rollDeg  = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (pitchDeg is float p && yawDeg is float y && rollDeg is float r)
        {
            return Quaternion.CreateFromYawPitchRoll(y * Deg2RadF, p * Deg2RadF, r * Deg2RadF);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Hamilton product: applies rotation b then a (right-to-left composition).
    /// Scheme: <c>(quat* a b)</c>
    /// </summary>
    public static object QuatMultiply(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is Quaternion && b is Quaternion)
        {
            return Quaternion.Multiply((Quaternion)a, (Quaternion)b);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Spherical linear interpolation between two quaternions at t in [0, 1].
    /// Scheme: <c>(quat-slerp a b t)</c>
    /// </summary>
    public static object QuatSlerp(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        float? t = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (a is Quaternion qa && b is Quaternion qb && t is float tf)
            return Quaternion.Slerp(qa, qb, tf);
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Decomposes a quaternion to Euler angles in degrees [pitch, yaw, roll].
    /// Intended for debugging and serialization; prefer quaternion arithmetic
    /// in hot paths to avoid gimbal lock.
    /// Scheme: <c>(quat-to-euler q)</c>
    /// </summary>
    public static object QuatToEuler(object[] args)
    {
        var q = args.Length > 0 ? args[0] : null;
        
        if (q is Quaternion)
        {
            Quaternion quat = (Quaternion)q;
            
            // Roll (rotation around Z)
            float sinrCosp = 2f * (quat.W * quat.Z + quat.X * quat.Y);
            float cosrCosp = 1f - 2f * (quat.Z * quat.Z + quat.Y * quat.Y);
            float roll     = MathF.Atan2(sinrCosp, cosrCosp) * Rad2DegF;
            
            // Pitch (rotation around X)
            float sinp  = 2f * (quat.W * quat.X - quat.Z * quat.Y);
            float pitch = MathF.Abs(sinp) >= 1f
                ? MathF.CopySign(90f, sinp)
                : MathF.Asin(sinp) * Rad2DegF;
            
            // Yaw (rotation around Y)
            float sinyCosp = 2f * (quat.W * quat.Y + quat.Z * quat.X);
            float cosyCosp = 1f - 2f * (quat.Y * quat.Y + quat.X * quat.X);
            float yaw      = MathF.Atan2(sinyCosp, cosyCosp) * Rad2DegF;
            
            return new Vector3(pitch, yaw, roll);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Rotates a Vector3 by a quaternion.
    /// Scheme: <c>(quat-rotate-vec3 q v)</c>
    /// </summary>
    public static object QuatRotateVec3(object[] args)
    {
        var q = args.Length > 0 ? args[0] : null;
        var v = args.Length > 1 ? args[1] : null;
        
        if (q is Quaternion && v is Vector3)
        {
            return Vector3.Transform((Vector3)v, (Quaternion)q);
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // MATRIX 4x4
    // =======================================================================
    
    /// <summary>
    /// Returns the 4x4 identity matrix.
    /// Scheme: <c>(matrix-identity)</c>
    /// </summary>
    public static object MatrixIdentity(object[] args)
    {
        return Matrix4x4.Identity;
    }
    
    /// <summary>
    /// Matrix multiplication: a x b.
    /// Scheme: <c>(matrix* a b)</c>
    /// </summary>
    public static object MatrixMultiply(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is Matrix4x4 && b is Matrix4x4)
        {
            return Matrix4x4.Multiply((Matrix4x4)a, (Matrix4x4)b);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Matrix inverse.  Returns the identity matrix when the matrix is singular
    /// (determinant ~= 0) to handle potential errors in the script.
    /// Scheme: <c>(matrix-inverse m)</c>
    /// </summary>
    public static object MatrixInverse(object[] args)
    {
        var m = args.Length > 0 ? args[0] : null;
        
        if (m is Matrix4x4)
        {
            if (!Matrix4x4.Invert((Matrix4x4)m, out Matrix4x4 result))
            {
                // Return the identity matrix as a fail-safe.
                return Matrix4x4.Identity;
            }
            return result;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Builds a combined rotation and translation matrix from a quaternion and
    /// a translation vector.  No scale component; use TransformCreate for TRS.
    /// Scheme: <c>(matrix-from-quat-trans q t)</c>
    /// </summary>
    public static object MatrixFromQuatTrans(object[] args)
    {
        var q           = args.Length > 0 ? args[0] : null;
        var translation = args.Length > 1 ? args[1] : null;
        
        if (q is Quaternion && translation is Vector3)
        {
            Matrix4x4 m = Matrix4x4.CreateFromQuaternion((Quaternion)q);
            m.Translation = (Vector3)translation;
            return m;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Transforms a point by a matrix (applies translation).  Use for positions.
    /// Scheme: <c>(matrix-transform-point m p)</c>
    /// </summary>
    public static object MatrixTransformPoint(object[] args)
    {
        var m     = args.Length > 0 ? args[0] : null;
        var point = args.Length > 1 ? args[1] : null;
        
        if (m is Matrix4x4 && point is Vector3)
        {
            return Vector3.Transform((Vector3)point, (Matrix4x4)m);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Transforms a direction vector by a matrix (ignores translation).  Use for
    /// normals and velocities where the origin does not matter.
    /// Scheme: <c>(matrix-transform-dir m d)</c>
    /// </summary>
    public static object MatrixTransformDirection(object[] args)
    {
        var m   = args.Length > 0 ? args[0] : null;
        var dir = args.Length > 1 ? args[1] : null;
        
        if (m is Matrix4x4 && dir is Vector3)
        {
            return Vector3.TransformNormal((Vector3)dir, (Matrix4x4)m);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Constructs a view (look-at) matrix.
    /// Scheme: <c>(matrix-look-at eye target up)</c>
    /// </summary>
    public static object MatrixLookAt(object[] args)
    {
        var eye    = args.Length > 0 ? args[0] : null;
        var target = args.Length > 1 ? args[1] : null;
        var up     = args.Length > 2 ? args[2] : null;
        
        if (eye is Vector3 && target is Vector3 && up is Vector3)
        {
            return Matrix4x4.CreateLookAt((Vector3)eye, (Vector3)target, (Vector3)up);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Constructs a right-handed perspective projection matrix.
    /// Scheme: <c>(matrix-perspective fov aspect near far)</c>
    /// </summary>
    public static object MatrixPerspective(object[] args)
    {
        float? fovDeg = args.Length > 0 ? AsFloat(args[0]) : null;
        float? aspect = args.Length > 1 ? AsFloat(args[1]) : null;
        float? near   = args.Length > 2 ? AsFloat(args[2]) : null;
        float? far    = args.Length > 3 ? AsFloat(args[3]) : null;
        
        if (fovDeg is float fov && aspect is float asp && near is float n && far is float f)
            return Matrix4x4.CreatePerspectiveFieldOfView(fov * Deg2RadF, asp, n, f);
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // TRANSFORM  (position + rotation + scale, stored as opaque handles)
    // =======================================================================
    
    /// <summary>
    /// Allocates a new transform in the engine pool and returns an opaque
    /// handle.  The handle is valid for the lifetime of the runtime.
    /// Scheme: <c>(transform-create pos rot scale)</c>
    /// </summary>
    public static object TransformCreate(object[] args)
    {
        var pos   = args.Length > 0 ? args[0] : null;
        var rot   = args.Length > 1 ? args[1] : null;
        var scale = args.Length > 2 ? args[2] : null;
        
        if (pos is Vector3 && rot is Quaternion && scale is Vector3)
        {
            long handle = Interlocked.Increment(ref _nextHandle);
            _transforms[handle] = new TransformData((Vector3)pos, (Quaternion)rot, (Vector3)scale);
            return handle;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns the world position of a transform.
    /// Scheme: <c>(transform-get-pos handle)</c>
    /// </summary>
    public static object TransformGetPosition(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        
        if (handle is long)
        {
            return GetTransform((long)handle).Position;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Updates the world position of a transform in-place.
    /// Scheme: <c>(transform-set-pos! handle pos)</c>
    /// </summary>
    public static object TransformSetPosition(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        var pos    = args.Length > 1 ? args[1] : null;
        
        if (handle is long && pos is Vector3)
        {
            TransformData t = GetTransform((long)handle);
            _transforms[(long)handle] = t with { Position = (Vector3)pos };
            return "#t".Eval();
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns the rotation quaternion of a transform.
    /// Scheme: <c>(transform-get-rot handle)</c>
    /// </summary>
    public static object TransformGetRotation(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        
        if (handle is long)
        {
            return GetTransform((long)handle).Rotation;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Updates the rotation quaternion of a transform in-place.
    /// Scheme: <c>(transform-set-rot! handle rot)</c>
    /// </summary>
    public static object TransformSetRotation(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        var rot    = args.Length > 1 ? args[1] : null;
        
        if (handle is long && rot is Quaternion)
        {
            TransformData t = GetTransform((long)handle);
            _transforms[(long)handle] = t with { Rotation = (Quaternion)rot };
            return "#t".Eval();
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns the local forward vector (+Z in GOAL space) of a transform,
    /// derived from its rotation quaternion.
    /// Scheme: <c>(transform-forward handle)</c>
    /// </summary>
    public static object TransformForward(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        
        if (handle is long)
        {
            // GOAL uses a left-handed coordinate system where +Z is forward.
            // System.Numerics is right-handed, so rotating +Z by 90° around +Y
            // gives +X rather than -X.  Negating Z before transforming corrects
            // for the handedness mismatch and preserves GOAL's convention.
            return Vector3.Transform(-Vector3.UnitZ, GetTransform((long)handle).Rotation);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Releases a transform handle and frees its pool entry.
    /// Scheme: <c>(transform-destroy! handle)</c>
    /// </summary>
    public static object TransformDestroy(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        
        if (handle is long)
        {
            _transforms.TryRemove((long)handle, out _);
            return "#t".Eval();
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // GEOMETRY HELPERS
    // =======================================================================
    
    /// <summary>
    /// Constructs an axis-aligned bounding box from its minimum and maximum
    /// corner points.
    /// Scheme: <c>(bbox-make min max)</c>
    /// </summary>
    public static object BBoxMake(object[] args)
    {
        var min = args.Length > 0 ? args[0] : null;
        var max = args.Length > 1 ? args[1] : null;
        
        if (min is Vector3 && max is Vector3)
        {
            return ((Vector3)min, (Vector3)max);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns true if the given point lies inside or on the surface of the
    /// bounding box.
    /// Scheme: <c>(bbox-contains? bbox point)</c>
    /// </summary>
    public static object BBoxContains(object[] args)
    {
        var bbox  = args.Length > 0 ? args[0] : null;
        var point = args.Length > 1 ? args[1] : null;
        
        if (bbox is ValueTuple<Vector3, Vector3> && point is Vector3)
        {
            var (min, max) = (ValueTuple<Vector3, Vector3>)bbox;
            Vector3 pt = (Vector3)point;
            return pt.X >= min.X && pt.X <= max.X
                && pt.Y >= min.Y && pt.Y <= max.Y
                && pt.Z >= min.Z && pt.Z <= max.Z;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns true if two bounding boxes overlap.  Touching surfaces count as
    /// intersection, consistent with GOAL's collision queries.
    /// Scheme: <c>(bbox-intersects? a b)</c>
    /// </summary>
    public static object BBoxIntersects(object[] args)
    {
        var a = args.Length > 0 ? args[0] : null;
        var b = args.Length > 1 ? args[1] : null;
        
        if (a is ValueTuple<Vector3, Vector3> && b is ValueTuple<Vector3, Vector3>)
        {
            var (aMin, aMax) = (ValueTuple<Vector3, Vector3>)a;
            var (bMin, bMax) = (ValueTuple<Vector3, Vector3>)b;
            return aMin.X <= bMax.X && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y
                && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns the center point of a bounding box.
    /// Scheme: <c>(bbox-center bbox)</c>
    /// </summary>
    public static object BBoxCenter(object[] args)
    {
        var bbox = args.Length > 0 ? args[0] : null;
        
        if (bbox is ValueTuple<Vector3, Vector3>)
        {
            var (min, max) = (ValueTuple<Vector3, Vector3>)bbox;
            return (min + max) * 0.5f;
        }
        
        return "#f".Eval();
    }
    
    // =====================================================================
    // INTERPOLATION AND EASING
    // =====================================================================
    
    /// <summary>
    /// Linear interpolation between two scalars.
    /// Scheme: <c>(lerp a b t)</c>
    /// </summary>
    public static object Lerp(object[] args)
    {
        float? a = args.Length > 0 ? AsFloat(args[0]) : null;
        float? b = args.Length > 1 ? AsFloat(args[1]) : null;
        float? t = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (a is float af && b is float bf && t is float tf)
            return af + (bf - af) * tf;
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Clamps a value to the inclusive range [min, max].
    /// Scheme: <c>(clamp val min max)</c>
    /// </summary>
    public static object Clamp(object[] args)
    {
        float? val = args.Length > 0 ? AsFloat(args[0]) : null;
        float? min = args.Length > 1 ? AsFloat(args[1]) : null;
        float? max = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (val is float v && min is float lo && max is float hi)
            return MathF.Min(MathF.Max(v, lo), hi);
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Hermite interpolation that eases in and out.  Returns 0 when val is at
    /// or below edge0, 1 when val is at or above edge1, and a smooth cubic
    /// curve in between.
    /// Scheme: <c>(smooth-step edge0 edge1 val)</c>
    /// </summary>
    public static object SmoothStep(object[] args)
    {
        float? edge0 = args.Length > 0 ? AsFloat(args[0]) : null;
        float? edge1 = args.Length > 1 ? AsFloat(args[1]) : null;
        float? val   = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (edge0 is float e0 && edge1 is float e1 && val is float v)
        {
            float t = MathF.Min(MathF.Max((v - e0) / (e1 - e0), 0f), 1f);
            return t * t * (3f - 2f * t);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Smoother variant of SmoothStep (Ken Perlin's improved version).  Has zero
    /// first and second derivatives at both edges; better for noise functions.
    /// Scheme: <c>(smoother-step edge0 edge1 val)</c>
    /// </summary>
    public static object SmootherStep(object[] args)
    {
        float? edge0 = args.Length > 0 ? AsFloat(args[0]) : null;
        float? edge1 = args.Length > 1 ? AsFloat(args[1]) : null;
        float? val   = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (edge0 is float e0 && edge1 is float e1 && val is float v)
        {
            float t = MathF.Min(MathF.Max((v - e0) / (e1 - e0), 0f), 1f);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Converts degrees to radians.
    /// Scheme: <c>(deg->rad deg)</c>
    /// </summary>
    public static object DegToRad(object[] args)
    {
        float? deg = args.Length > 0 ? AsFloat(args[0]) : null;
        
        if (deg is float d)
            return d * Deg2RadF;
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Converts radians to degrees.
    /// Scheme: <c>(rad->deg rad)</c>
    /// </summary>
    public static object RadToDeg(object[] args)
    {
        float? rad = args.Length > 0 ? AsFloat(args[0]) : null;
        
        if (rad is float r)
            return r * Rad2DegF;
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Wraps an angle in degrees to the range (-180, 180].  Useful for
    /// shortest-path rotation arithmetic.
    /// Scheme: <c>(wrap-angle-180 deg)</c>
    /// </summary>
    public static object WrapAngle180(object[] args)
    {
        float? deg = args.Length > 0 ? AsFloat(args[0]) : null;
        
        if (deg is float d)
        {
            float wrapped = (d % 360f + 360f) % 360f;
            return wrapped > 180f ? wrapped - 360f : wrapped;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Shortest angular distance from angle a to angle b in degrees.  Returns
    /// a signed value in the range (-180, 180].
    /// Scheme: <c>(angle-delta a b)</c>
    /// </summary>
    public static object AngleDelta(object[] args)
    {
        float? a = args.Length > 0 ? AsFloat(args[0]) : null;
        float? b = args.Length > 1 ? AsFloat(args[1]) : null;
        
        if (a is float af && b is float bf)
        {
            float diff = ((bf - af) % 360f + 360f) % 360f;
            return diff > 180f ? diff - 360f : diff;
        }
        
        return "#f".Eval();
    }
    
    // =====================================================================
    // UNIT CONVERSION  (GOAL fixed-point <-> meters)
    // =====================================================================
    
    /// <summary>
    /// Converts a value in GOAL units to meters.  4096 units == 1 meter.
    /// Scheme: <c>(units->meters u)</c>
    /// </summary>
    public static object UnitsToMeters(object[] args)
    {
        float? units = args.Length > 0 ? AsFloat(args[0]) : null;
        
        if (units is float u)
            return u / UnitsPerMeter;
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Converts a value in meters to GOAL units.
    /// Scheme: <c>(meters->units m)</c>
    /// </summary>
    public static object MetersToUnits(object[] args)
    {
        float? meters = args.Length > 0 ? AsFloat(args[0]) : null;
        
        if (meters is float m)
            return m * UnitsPerMeter;
        
        return "#f".Eval();
    }
    
    // =====================================================================
    // RANDOM
    // =====================================================================
    
    /// <summary>
    /// Returns a uniformly distributed random float in [min, max).
    /// Scheme: <c>(random-float min max)</c>
    /// </summary>
    public static object RandomFloat(object[] args)
    {
        float? min = args.Length > 0 ? AsFloat(args[0]) : null;
        float? max = args.Length > 1 ? AsFloat(args[1]) : null;
        
        if (min is float lo && max is float hi)
            return lo + (float)Rng.NextDouble() * (hi - lo);
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns a uniformly distributed random integer in [min, max].
    /// Scheme: <c>(random-int min max)</c>
    /// </summary>
    public static object RandomInt(object[] args)
    {
        var min = args.Length > 0 ? args[0] : null;
        var max = args.Length > 1 ? args[1] : null;
        
        if (min is int && max is int)
        {
            return Rng.Next((int)min, (int)max + 1);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns a uniformly distributed random point inside a sphere of the
    /// given radius using rejection sampling.  Expected iterations per call ~= 1.91.
    /// Scheme: <c>(random-point-in-sphere radius)</c>
    /// </summary>
    public static object RandomPointInSphere(object[] args)
    {
        var radius = args.Length > 0 ? args[0] : null;
        
        if (radius is float)
        {
            float r = (float)radius;
            while (true)
            {
                float x = (float)Rng.NextDouble() * 2f - 1f;
                float y = (float)Rng.NextDouble() * 2f - 1f;
                float z = (float)Rng.NextDouble() * 2f - 1f;
                if (x * x + y * y + z * z <= 1f)
                {
                    return new Vector3(x * r, y * r, z * r);
                }
            }
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns a uniformly distributed random point on the surface of a sphere
    /// of the given radius (Marsaglia method — avoids trig calls).
    /// Scheme: <c>(random-on-sphere radius)</c>
    /// </summary>
    public static object RandomOnSphere(object[] args)
    {
        var radius = args.Length > 0 ? args[0] : null;
        
        if (radius is float)
        {
            float r = (float)radius;
            while (true)
            {
                float u = (float)Rng.NextDouble() * 2f - 1f;
                float v = (float)Rng.NextDouble() * 2f - 1f;
                float s = u * u + v * v;
                if (s >= 1f)
                {
                    continue;
                }
                float root = MathF.Sqrt(1f - s);
                return new Vector3(
                    2f * u * root * r,
                    2f * v * root * r,
                    (1f - 2f * s) * r);
            }
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // SCALAR UTILITIES
    // =======================================================================
    
    /// <summary>
    /// Absolute value of a float.  Equivalent to GOAL's <c>fabs</c> macro.
    /// Scheme: <c>(fabs x)</c>
    /// </summary>
    public static object Fabs(object[] args)
    {
        float? x = args.Length > 0 ? AsFloat(args[0]) : null;
        
        if (x is float xf)
            return MathF.Abs(xf);
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Square root with absolute value guard.  Equivalent to GOAL's <c>sqrtf</c>.
    /// Scheme: <c>(sqrtf x)</c>
    /// </summary>
    public static object Sqrtf(object[] args)
    {
        float? x = args.Length > 0 ? AsFloat(args[0]) : null;
        
        if (x is float xf)
            return MathF.Sqrt(MathF.Abs(xf));
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Returns true when the absolute difference between a and b is less than
    /// epsilon.  Matches GOAL's <c>fequal-epsilon?</c>.
    /// Scheme: <c>(fequal-epsilon? a b eps)</c>
    /// </summary>
    public static object FEqualEpsilon(object[] args)
    {
        float? a       = args.Length > 0 ? AsFloat(args[0]) : null;
        float? b       = args.Length > 1 ? AsFloat(args[1]) : null;
        float? epsilon = args.Length > 2 ? AsFloat(args[2]) : null;
        
        if (a is float af && b is float bf && epsilon is float ef)
            return MathF.Abs(af - bf) < ef;
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Integer sign-safe division matching the EE's DIV instruction.  Returns
    /// -1 when the divisor is zero, and int.MinValue for the MIN_INT / -1
    /// overflow case.
    /// Scheme: <c>(/-signed-0-guard a b)</c>
    /// </summary>
    public static object SignedDiv0Guard(object[] args)
    {
        int? dividend = args.Length > 0 ? AsInt(args[0]) : null;
        int? divisor  = args.Length > 1 ? AsInt(args[1]) : null;
        
        if (dividend is int n && divisor is int d)
        {
            if (d == 0) return n < 0 ? 1 : -1;
            if (n == int.MinValue && d == -1) return int.MinValue;
            return n / d;
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Integer modulo matching the EE's DIV instruction edge cases.
    /// Scheme: <c>(mod-signed-0-guard a b)</c>
    /// </summary>
    public static object SignedMod0Guard(object[] args)
    {
        int? dividend = args.Length > 0 ? AsInt(args[0]) : null;
        int? divisor  = args.Length > 1 ? AsInt(args[1]) : null;
        
        if (dividend is int n && divisor is int d)
        {
            if (d == 0) return n;
            if (n == int.MinValue && d == -1) return 0;
            return n % d;
        }
        
        return "#f".Eval();
    }
}
using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;

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
    // VECTOR 3
    // =======================================================================

    /// <summary>
    /// Constructs a Vector3.
    /// Scheme: <c>(vec3 x y z)</c>
    public static Vector3 Vec3(float x, float y, float z)
    {
        return new Vector3(x, y, z);
    }
    
    /// <summary>
    /// Component-wise addition.
    /// Scheme: <c>(vector+ a b)</c>
    /// </summary>
    public static Vector3 Vector3Add(Vector3 a, Vector3 b)
    {
        return Vector3.Add(a, b);
    }
    
    /// <summary>
    /// Component-wise subtraction.
    /// Scheme: <c>(vector- a b)</c>
    /// </summary>
    public static Vector3 Vector3Sub(Vector3 a, Vector3 b)
    {
        return Vector3.Subtract(a, b);
    }
    
    /// <summary>
    /// Scalar multiplication.
    /// Scheme: <c>(vector-scale v s)</c>
    /// </summary>
    public static Vector3 Vector3Scale(Vector3 v, float s)
    {
        return Vector3.Multiply(v, s);
    }
    
    /// <summary>
    /// Dot product of two Vector3s.
    /// Scheme: <c>(vector-dot a b)</c>
    /// </summary>
    public static float Vector3Dot(Vector3 a, Vector3 b)
    {
        return Vector3.Dot(a, b);
    }
    
    /// <summary>
    /// Cross product. Result is perpendicular to both inputs.
    /// Scheme: <c>(vector-cross a b)</c>
    /// </summary>
    public static Vector3 Vector3Cross(Vector3 a, Vector3 b)
    {
        return Vector3.Cross(a, b);
    }
    
    /// <summary>
    /// Euclidean length (magnitude).
    /// Scheme: <c>(vector-length v)</c>
    /// </summary>
    public static float Vector3Length(Vector3 v)
    {
        return v.Length();
    }
    
    /// <summary>
    /// Returns a unit-length copy of v. Returns Vector3.Zero when the input
    /// magnitude is below float epsilon, matching GOAL's safe-normalize behavior.
    /// Scheme: <c>(vector-normalize v)</c>
    /// </summary>
    public static Vector3 Vector3Normalize(Vector3 v)
    {
        float len = v.Length();
        if (len < float.Epsilon)
        {
            return Vector3.Zero;
        }
        return v / len;
    }
    
    /// <summary>
    /// Euclidean distance between two points.
    /// Scheme: <c>(vector-distance a b)</c>
    /// </summary>
    public static float Vector3Distance(Vector3 a, Vector3 b)
    {
        return Vector3.Distance(a, b);
    }
    
    /// <summary>
    /// Linear interpolation between two Vector3s at parameter t in [0, 1].
    /// Scheme: <c>(vector-lerp a b t)</c>
    /// </summary>

    public static Vector3 Vector3Lerp(Vector3 a, Vector3 b, float t)
    {
        return Vector3.Lerp(a, b, t);
    }
    
    // =======================================================================
    // VECTOR 4
    // =======================================================================
    
    /// <summary>
    /// Constructs a Vector4. GOAL's native <c>vector</c> type includes a w
    /// component; spatial positions use units where value / 4096 == meters.
    /// Scheme: <c>(vec4 x y z w)</c>
    /// </summary>
    public static Vector4 Vector4(float x, float y, float z, float w)
    {
        return new Vector4(x, y, z, w);
    }
    
    // =======================================================================
    // QUATERNION
    // =======================================================================
    
    /// <summary>
    /// Returns the identity quaternion (no rotation).
    /// Scheme: <c>(quat-identity)</c>
    /// </summary>
    public static Quaternion QuatIdentity()
    {
        return Quaternion.Identity;
    }
    
    /// <summary>
    /// Constructs a quaternion from an axis vector and a rotation angle in
    /// degrees. The axis does not need to be pre-normalised.
    /// Scheme: <c>(quat-from-axis-angle axis deg)</c>
    /// </summary>
    public static Quaternion QuatFromAxisAngle(Vector3 axis, float angleDeg)
    {
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angleDeg * Deg2RadF);
    }
    
    /// <summary>
    /// Constructs a quaternion from Euler angles in degrees (pitch, yaw, roll
    /// applied in that order — matches GOAL's convention).
    /// Scheme: <c>(quat-from-euler pitch yaw roll)</c>
    /// </summary>
    public static Quaternion QuatFromEuler(float pitchDeg, float yawDeg, float rollDeg)
    {
        return Quaternion.CreateFromYawPitchRoll(
            yawDeg   * Deg2RadF,
            pitchDeg * Deg2RadF,
            rollDeg  * Deg2RadF);
    }
    
    /// <summary>
    /// Hamilton product: applies rotation b then a (right-to-left composition).
    /// Scheme: <c>(quat* a b)</c>
    /// </summary>
    public static Quaternion QuatMultiply(Quaternion a, Quaternion b)
    {
        return Quaternion.Multiply(a, b);
    }
    
    /// <summary>
    /// Spherical linear interpolation between two quaternions at t in [0, 1].
    /// Scheme: <c>(quat-slerp a b t)</c>
    /// </summary>
    public static Quaternion QuatSlerp(Quaternion a, Quaternion b, float t)
    {
        return Quaternion.Slerp(a, b, t);
    }
    
    /// <summary>
    /// Decomposes a quaternion to Euler angles in degrees [pitch, yaw, roll].
    /// Intended for debugging and serialization; prefer quaternion arithmetic
    /// in hot paths to avoid gimbal lock.
    /// Scheme: <c>(quat-to-euler q)</c>
    /// </summary>
    public static Vector3 QuatToEuler(Quaternion q)
    {
        float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
        float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll     = MathF.Atan2(sinrCosp, cosrCosp) * Rad2DegF;
        
        float sinp  = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1f
            ? MathF.CopySign(90f, sinp)
            : MathF.Asin(sinp) * Rad2DegF;
        
        float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
        float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw      = MathF.Atan2(sinyCosp, cosyCosp) * Rad2DegF;
        
        return new Vector3(pitch, yaw, roll);
    }
    
    /// <summary>
    /// Rotates a Vector3 by a quaternion.
    /// Scheme: <c>(quat-rotate-vec3 q v)</c>
    /// </summary>
    public static Vector3 QuatRotateVec3(Quaternion q, Vector3 v)
    {
        return Vector3.Transform(v, q);
    }
    
    // =======================================================================
    // MATRIX 4x4
    // =======================================================================
    
    /// <summary>
    /// Returns the 4x4 identity matrix.
    /// Scheme: <c>(matrix-identity)</c>
    /// </summary>
    public static Matrix4x4 MatrixIdentity()
    {
        return Matrix4x4.Identity;
    }
    
    /// <summary>
    /// Matrix multiplication: a x b.
    /// Scheme: <c>(matrix* a b)</c>
    /// </summary>
    public static Matrix4x4 MatrixMultiply(Matrix4x4 a, Matrix4x4 b)
    {
        return Matrix4x4.Multiply(a, b);
    }
    
    /// <summary>
    /// Matrix inverse.  Returns the identity matrix when the matrix is singular
    /// (determinant ~= 0) to handle potential errors in the script.
    /// Scheme: <c>(matrix-inverse m)</c>
    /// </summary>
    public static Matrix4x4 MatrixInverse(Matrix4x4 m)
    {
        if (!Matrix4x4.Invert(m, out Matrix4x4 result))
        {
            // Return the identity matrix as a fail-safe.
            return Matrix4x4.Identity;
        }
        return result;
    }
    
    /// <summary>
    /// Builds a combined rotation and translation matrix from a quaternion and
    /// a translation vector.  No scale component; use TransformCreate for TRS.
    /// Scheme: <c>(matrix-from-quat-trans q t)</c>
    /// </summary>
    public static Matrix4x4 MatrixFromQuatTrans(Quaternion q, Vector3 translation)
    {
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(q);
        m.Translation = translation;
        return m;
    }
    
    /// <summary>
    /// Transforms a point by a matrix (applies translation).  Use for positions.
    /// Scheme: <c>(matrix-transform-point m p)</c>
    /// </summary>
    public static Vector3 MatrixTransformPoint(Matrix4x4 m, Vector3 point)
    {
        return Vector3.Transform(point, m);
    }
    
    /// <summary>
    /// Transforms a direction vector by a matrix (ignores translation).  Use for
    /// normals and velocities where the origin does not matter.
    /// Scheme: <c>(matrix-transform-dir m d)</c>
    /// </summary>
    public static Vector3 MatrixTransformDirection(Matrix4x4 m, Vector3 dir)
    {
        return Vector3.TransformNormal(dir, m);
    }
    
    /// <summary>
    /// Constructs a view (look-at) matrix.
    /// Scheme: <c>(matrix-look-at eye target up)</c>
    /// </summary>
    /// <param name="eye">Camera origin.</param>
    /// <param name="target">TPoint to look at.</param>
    /// <param name="up">World up vector.</param>
    public static Matrix4x4 MatrixLookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        return Matrix4x4.CreateLookAt(eye, target, up);
    }
    
    /// <summary>
    /// Constructs a right-handed perspective projection matrix.
    /// Scheme: <c>(matrix-perspective fov aspect near far)</c>
    /// </summary>
    /// <param name="fovDeg">Vertical field of view in degrees.</param>
    /// <param name="aspect">Viewport width / height.</param>
    /// <param name="near">Near clip plane distance (positive).</param>
    /// <param name="far">Far clip plane distance (positive, must be greater than near).</param>
    public static Matrix4x4 MatrixPerspective(float fovDeg, float aspect, float near, float far)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(fovDeg * Deg2RadF, aspect, near, far);
    }
    
    // =======================================================================
    // TRANSFORM  (position + rotation + scale, stored as opaque handles)
    // =======================================================================
    
    /// <summary>
    /// Allocates a new transform in the engine pool and returns an opaque
    /// handle.  The handle is valid for the lifetime of the runtime.
    /// Scheme: <c>(transform-create pos rot scale)</c>
    /// </summary>
    public static long TransformCreate(Vector3 pos, Quaternion rot, Vector3 scale)
    {
        long handle = Interlocked.Increment(ref _nextHandle);
        _transforms[handle] = new TransformData(pos, rot, scale);
        return handle;
    }
    
    /// <summary>
    /// Returns the world position of a transform.
    /// Scheme: <c>(transform-get-pos handle)</c>
    /// </summary>
    public static Vector3 TransformGetPosition(long handle)
    {
        return GetTransform(handle).Position;
    }
    
    /// <summary>
    /// Updates the world position of a transform in-place.
    /// Scheme: <c>(transform-set-pos! handle pos)</c>
    /// </summary>
    public static void TransformSetPosition(long handle, Vector3 pos)
    {
        TransformData t = GetTransform(handle);
        _transforms[handle] = t with { Position = pos };
    }
    
    /// <summary>
    /// Returns the rotation quaternion of a transform.
    /// Scheme: <c>(transform-get-rot handle)</c>
    /// </summary>
    public static Quaternion TransformGetRotation(long handle)
    {
        return GetTransform(handle).Rotation;
    }
    
    /// <summary>
    /// Updates the rotation quaternion of a transform in-place.
    /// Scheme: <c>(transform-set-rot! handle rot)</c>
    /// </summary>
    public static void TransformSetRotation(long handle, Quaternion rot)
    {
        TransformData t = GetTransform(handle);
        _transforms[handle] = t with { Rotation = rot };
    }
    
    /// <summary>
    /// Returns the local forward vector (+Z in GOAL space) of a transform,
    /// derived from its rotation quaternion.
    /// Scheme: <c>(transform-forward handle)</c>
    /// </summary>
    public static Vector3 TransformForward(long handle)
    {
        return Vector3.Transform(Vector3.UnitZ, GetTransform(handle).Rotation);
    }
    
    /// <summary>
    /// Releases a transform handle and frees its pool entry.
    /// Scheme: <c>(transform-destroy! handle)</c>
    /// </summary>
    public static void TransformDestroy(long handle)
    {
        _transforms.TryRemove(handle, out _);
    }
    
    // =======================================================================
    // GEOMETRY HELPERS
    // =======================================================================
    
    /// <summary>
    /// Constructs an axis-aligned bounding box from its minimum and maximum
    /// corner points.
    /// Scheme: <c>(bbox-make min max)</c>
    /// </summary>
    public static (Vector3 Min, Vector3 Max) BBoxMake(Vector3 min, Vector3 max)
    {
        return (min, max);
    }
    
    /// <summary>
    /// Returns true if the given point lies inside or on the surface of the
    /// bounding box.
    /// Scheme: <c>(bbox-contains? bbox point)</c>
    /// </summary>
    public static bool BBoxContains((Vector3 Min, Vector3 Max) bbox, Vector3 point)
    {
        return point.X >= bbox.Min.X && point.X <= bbox.Max.X
                                     && point.Y >= bbox.Min.Y && point.Y <= bbox.Max.Y
                                     && point.Z >= bbox.Min.Z && point.Z <= bbox.Max.Z;
    }
    
    /// <summary>
    /// Returns true if two bounding boxes overlap.  Touching surfaces count as
    /// intersection, consistent with GOAL's collision queries.
    /// Scheme: <c>(bbox-intersects? a b)</c>
    /// </summary>
    public static bool BBoxIntersects(
        (Vector3 Min, Vector3 Max) a,
        (Vector3 Min, Vector3 Max) b)
    {
        return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
                                  && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
                                  && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }
    
    /// <summary>
    /// Returns the centre point of a bounding box.
    /// Scheme: <c>(bbox-center bbox)</c>
    /// </summary>
    public static Vector3 BBoxCenter((Vector3 Min, Vector3 Max) bbox)
    {
        return (bbox.Min + bbox.Max) * 0.5f;
    }
    
    // =====================================================================
    // INTERPOLATION AND EASING
    // =====================================================================
    
    /// <summary>
    /// Linear interpolation between two scalars.
    /// Scheme: <c>(lerp a b t)</c>
    /// </summary>
    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
    
    /// <summary>
    /// Clamps a value to the inclusive range [min, max].
    /// Scheme: <c>(clamp val min max)</c>
    /// </summary>
    public static float Clamp(float val, float min, float max)
    {
        return MathF.Min(MathF.Max(val, min), max);
    }
    
    /// <summary>
    /// Hermite interpolation that eases in and out.  Returns 0 when val is at
    /// or below edge0, 1 when val is at or above edge1, and a smooth cubic
    /// curve in between.
    /// Scheme: <c>(smooth-step edge0 edge1 val)</c>
    /// </summary>
    public static float SmoothStep(float edge0, float edge1, float val)
    {
        float t = Clamp((val - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
    
    /// <summary>
    /// Smoother variant of SmoothStep (Ken Perlin's improved version).  Has zero
    /// first and second derivatives at both edges; better for noise functions.
    /// Scheme: <c>(smoother-step edge0 edge1 val)</c>
    /// </summary>
    public static float SmootherStep(float edge0, float edge1, float val)
    {
        float t = Clamp((val - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
    
    /// <summary>
    /// Converts degrees to radians.
    /// Scheme: <c>(deg->rad deg)</c>
    /// </summary>
    public static float DegToRad(float deg)
    {
        return deg * Deg2RadF;
    }
    
    /// <summary>
    /// Converts radians to degrees.
    /// Scheme: <c>(rad->deg rad)</c>
    /// </summary>
    public static float RadToDeg(float rad)
    {
        return rad * Rad2DegF;
    }
    
    /// <summary>
    /// Wraps an angle in degrees to the range (-180, 180].  Useful for
    /// shortest-path rotation arithmetic.
    /// Scheme: <c>(wrap-angle-180 deg)</c>
    /// </summary>
    public static float WrapAngle180(float deg)
    {
        deg = ((deg % 360f) + 360f) % 360f;
        return deg > 180f ? deg - 360f : deg;
    }
    
    /// <summary>
    /// Shortest angular distance from angle a to angle b in degrees.  Returns
    /// a signed value in the range (-180, 180].
    /// Scheme: <c>(angle-delta a b)</c>
    /// </summary>
    public static float AngleDelta(float a, float b)
    {
        return WrapAngle180(b - a);
    }
    
    // =====================================================================
    // UNIT CONVERSION  (GOAL fixed-point <-> meters)
    // =====================================================================
    
    /// <summary>
    /// Converts a value in GOAL units to meters.  4096 units == 1 meter.
    /// Scheme: <c>(units->meters u)</c>
    /// </summary>
    public static float UnitsToMeters(float units)
    {
        return units / UnitsPerMeter;
    }
    
    /// <summary>
    /// Converts a value in meters to GOAL units.
    /// Scheme: <c>(meters->units m)</c>
    /// </summary>
    public static float MetersToUnits(float meters)
    {
        return meters * UnitsPerMeter;
    }
    
    // =====================================================================
    // RANDOM
    // =====================================================================
    
    /// <summary>
    /// Returns a uniformly distributed random float in [min, max).
    /// Scheme: <c>(random-float min max)</c>
    /// </summary>
    public static float RandomFloat(float min, float max)
    {
        return min + (float)Rng.NextDouble() * (max - min);
    }
    
    /// <summary>
    /// Returns a uniformly distributed random integer in [min, max].
    /// Scheme: <c>(random-int min max)</c>
    /// </summary>
    public static int RandomInt(int min, int max)
    {
        return Rng.Next(min, max + 1);
    }
    
    /// <summary>
    /// Returns a uniformly distributed random point inside a sphere of the
    /// given radius using rejection sampling.  Expected iterations per call ~= 1.91.
    /// Scheme: <c>(random-point-in-sphere radius)</c>
    /// </summary>
    public static Vector3 RandomPointInSphere(float radius)
    {
        while (true)
        {
            float x = RandomFloat(-1f, 1f);
            float y = RandomFloat(-1f, 1f);
            float z = RandomFloat(-1f, 1f);
            if (x * x + y * y + z * z <= 1f)
            {
                return new Vector3(x * radius, y * radius, z * radius);
            }
        }
    }
    
    /// <summary>
    /// Returns a uniformly distributed random point on the surface of a sphere
    /// of the given radius (Marsaglia method — avoids trig calls).
    /// Scheme: <c>(random-on-sphere radius)</c>
    /// </summary>
    public static Vector3 RandomOnSphere(float radius)
    {
        while (true)
        {
            float u = RandomFloat(-1f, 1f);
            float v = RandomFloat(-1f, 1f);
            float s = u * u + v * v;
            if (s >= 1f)
            {
                continue;
            }
            float root = MathF.Sqrt(1f - s);
            return new Vector3(
                2f * u * root * radius,
                2f * v * root * radius,
                (1f - 2f * s) * radius);
        }
    }
    
    // =======================================================================
    // SCALAR UTILITIES
    // =======================================================================
    
    /// <summary>
    /// Absolute value of a float.  Equivalent to GOAL's <c>fabs</c> macro.
    /// Scheme: <c>(fabs x)</c>
    /// </summary>
    public static float Fabs(float x)
    {
        return MathF.Abs(x);
    }
    
    /// <summary>
    /// Square root with absolute value guard.  Equivalent to GOAL's <c>sqrtf</c>.
    /// Scheme: <c>(sqrtf x)</c>
    /// </summary>
    public static float Sqrtf(float x)
    {
        return MathF.Sqrt(MathF.Abs(x));
    }
    
    /// <summary>
    /// Returns true when the absolute difference between a and b is less than
    /// epsilon.  Matches GOAL's <c>fequal-epsilon?</c>.
    /// Scheme: <c>(fequal-epsilon? a b eps)</c>
    /// </summary>
    public static bool FEqualEpsilon(float a, float b, float epsilon)
    {
        return MathF.Abs(a - b) < epsilon;
    }
    
    /// <summary>
    /// Integer sign-safe division matching the EE's DIV instruction.  Returns
    /// -1 when the divisor is zero, and int.MinValue for the MIN_INT / -1
    /// overflow case.
    /// Scheme: <c>(/-signed-0-guard a b)</c>
    /// </summary>
    public static int SignedDiv0Guard(int dividend, int divisor)
    {
        if (divisor == 0)
        {
            return dividend < 0 ? 1 : -1;
        }
        if (dividend == int.MinValue && divisor == -1)
        {
            return int.MinValue;
        }
        return dividend / divisor;
    }
    
    /// <summary>
    /// Integer modulo matching the EE's DIV instruction edge cases.
    /// Scheme: <c>(mod-signed-0-guard a b)</c>
    /// </summary>
    public static int SignedMod0Guard(int dividend, int divisor)
    {
        if (divisor == 0)
        {
            return dividend;
        }
        if (dividend == int.MinValue && divisor == -1)
        {
            return 0;
        }
        return dividend % divisor;
    }
}

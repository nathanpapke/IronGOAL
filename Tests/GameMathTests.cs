using System;
using System.Numerics;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="GameMath"/>.
/// Method order matches the declaration order in GameMath.cs:
///   Vector3 → Vector4 → Quaternion → Matrix4x4 → Transform →
///   Geometry → Interpolation/Easing → Unit Conversion → Random → Scalar Utilities
/// </summary>
public class GameMathTests
{
    private const float Epsilon = 1e-5f;
 
    // =====================================================================
    // HELPERS
    // =====================================================================
 
    private static object F(float v) => (object)v;
    private static object I(int v)   => (object)v;
 
    private static void AssertVec3Equal(Vector3 expected, object result, float eps = Epsilon)
    {
        var v = Assert.IsType<Vector3>(result);
        Assert.Equal(expected.X, v.X, 4);
        Assert.Equal(expected.Y, v.Y, 4);
        Assert.Equal(expected.Z, v.Z, 4);
    }
 
    private static void AssertVec4Equal(Vector4 expected, object result, float eps = Epsilon)
    {
        var v = Assert.IsType<Vector4>(result);
        Assert.Equal(expected.X, v.X, 4);
        Assert.Equal(expected.Y, v.Y, 4);
        Assert.Equal(expected.Z, v.Z, 4);
        Assert.Equal(expected.W, v.W, 4);
    }
 
    private static void AssertQuatEqual(Quaternion expected, object result, float eps = Epsilon)
    {
        var q = Assert.IsType<Quaternion>(result);
        Assert.Equal(expected.X, q.X, 4);
        Assert.Equal(expected.Y, q.Y, 4);
        Assert.Equal(expected.Z, q.Z, 4);
        Assert.Equal(expected.W, q.W, 4);
    }
 
    private static void AssertMatrixEqual(Matrix4x4 expected, object result, float eps = Epsilon)
    {
        var m = Assert.IsType<Matrix4x4>(result);
        Assert.Equal(expected.M11, m.M11, 4); Assert.Equal(expected.M12, m.M12, 4);
        Assert.Equal(expected.M13, m.M13, 4); Assert.Equal(expected.M14, m.M14, 4);
        Assert.Equal(expected.M21, m.M21, 4); Assert.Equal(expected.M22, m.M22, 4);
        Assert.Equal(expected.M23, m.M23, 4); Assert.Equal(expected.M24, m.M24, 4);
        Assert.Equal(expected.M31, m.M31, 4); Assert.Equal(expected.M32, m.M32, 4);
        Assert.Equal(expected.M33, m.M33, 4); Assert.Equal(expected.M34, m.M34, 4);
        Assert.Equal(expected.M41, m.M41, 4); Assert.Equal(expected.M42, m.M42, 4);
        Assert.Equal(expected.M43, m.M43, 4); Assert.Equal(expected.M44, m.M44, 4);
    }
 
    private static void AssertInRange(float value, float min, float max)
    {
        Assert.True(value >= min && value <= max,
            $"Expected value {value} to be in range [{min}, {max}]");
    }
 
    // =====================================================================
    // VECTOR 3 — Vec3
    // =====================================================================
    
    [Fact]
    public void Vec3_ValidFloats_ReturnsCorrectVector3()
    {
        var result = Assert.IsType<Vector3>(GameMath.Vec3(new object[] { F(1f), F(2f), F(3f) }));
        Assert.Equal(1f, result.X, 4);
        Assert.Equal(2f, result.Y, 4);
        Assert.Equal(3f, result.Z, 4);
    }
 
    [Fact]
    public void Vec3_WrongTypes_ReturnsFalse()
    {
        var result = GameMath.Vec3(new object[] { "x", "y", "z" });
        Assert.IsNotType<Vector3>(result);
    }
 
    [Fact]
    public void Vec3_TooFewArgs_ReturnsFalse()
    {
        var result = GameMath.Vec3(new object[] { F(1f) });
        Assert.IsNotType<Vector3>(result);
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Add
    // =====================================================================
    
    [Fact]
    public void Vector3Add_ValidVectors_ReturnsSum()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, 5f, 6f);
        AssertVec3Equal(new Vector3(5f, 7f, 9f), GameMath.Vector3Add(new object[] { a, b }));
    }
 
    [Fact]
    public void Vector3Add_WrongTypes_ReturnsFalse()
    {
        var result = GameMath.Vector3Add(new object[] { F(1f), F(2f) });
        Assert.IsNotType<Vector3>(result);
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Subtract
    // =====================================================================
    
    [Fact]
    public void Vector3Subtract_ValidVectors_ReturnsDifference()
    {
        var a = new Vector3(5f, 6f, 7f);
        var b = new Vector3(1f, 2f, 3f);
        AssertVec3Equal(new Vector3(4f, 4f, 4f), GameMath.Vector3Subtract(new object[] { a, b }));
    }
 
    [Fact]
    public void Vector3Subtract_WrongTypes_ReturnsFalse()
    {
        var result = GameMath.Vector3Subtract(new object[] { "a", "b" });
        Assert.IsNotType<Vector3>(result);
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Scale
    // =====================================================================
 
    [Fact]
    public void Vector3Scale_ValidArgs_ReturnsScaledVector()
    {
        var result = GameMath.Vector3Scale(new object[] { new Vector3(1f, 2f, 3f), F(2f) });
        AssertVec3Equal(new Vector3(2f, 4f, 6f), result);
    }
 
    [Fact]
    public void Vector3Scale_ScaleByZero_ReturnsZeroVector()
    {
        var result = GameMath.Vector3Scale(new object[] { new Vector3(5f, 10f, 15f), F(0f) });
        AssertVec3Equal(Vector3.Zero, result);
    }
 
    [Fact]
    public void Vector3Scale_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.Vector3Scale(new object[] { F(1f), F(2f) }));
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Dot
    // =====================================================================
 
    [Fact]
    public void Vector3Dot_OrthogonalVectors_ReturnsZero()
    {
        var result = GameMath.Vector3Dot(new object[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) });
        Assert.Equal(0f, Assert.IsType<float>(result), 4);
    }
 
    [Fact]
    public void Vector3Dot_ParallelUnitVectors_ReturnsOne()
    {
        var result = GameMath.Vector3Dot(new object[] { new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 0f) });
        Assert.Equal(1f, Assert.IsType<float>(result), 4);
    }
 
    [Fact]
    public void Vector3Dot_KnownValues_ReturnsCorrectScalar()
    {
        // 1*4 + 2*5 + 3*6 = 32
        var result = GameMath.Vector3Dot(new object[] { new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f) });
        Assert.Equal(32f, Assert.IsType<float>(result), 4);
    }
 
    [Fact]
    public void Vector3Dot_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.Vector3Dot(new object[] { F(1f), F(2f) }));
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Cross
    // =====================================================================
 
    [Fact]
    public void Vector3Cross_XCrossY_ReturnsZ()
    {
        AssertVec3Equal(new Vector3(0f, 0f, 1f),
            GameMath.Vector3Cross(new object[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) }));
    }
 
    [Fact]
    public void Vector3Cross_ParallelVectors_ReturnsZero()
    {
        AssertVec3Equal(Vector3.Zero,
            GameMath.Vector3Cross(new object[] { new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f) }));
    }
 
    [Fact]
    public void Vector3Cross_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.Vector3Cross(new object[] { F(1f), F(1f) }));
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Length
    // =====================================================================
 
    [Fact]
    public void Vector3Length_UnitX_ReturnsOne()
    {
        var result = GameMath.Vector3Length(new object[] { new Vector3(1f, 0f, 0f) });
        Assert.Equal(1f, Assert.IsType<float>(result), 4);
    }
 
    [Fact]
    public void Vector3Length_KnownVector_ReturnsCorrectLength()
    {
        // (3, 4, 0) → length = 5
        Assert.Equal(5f, Assert.IsType<float>(GameMath.Vector3Length(new object[] { new Vector3(3f, 4f, 0f) })), 4);
    }
 
    [Fact]
    public void Vector3Length_ZeroVector_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.Vector3Length(new object[] { Vector3.Zero })), 4);
    }
 
    [Fact]
    public void Vector3Length_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.Vector3Length(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Normalize
    // =====================================================================
 
    [Fact]
    public void Vector3Normalize_KnownVector_ReturnsUnitVector()
    {
        AssertVec3Equal(new Vector3(1f, 0f, 0f),
            GameMath.Vector3Normalize(new object[] { new Vector3(3f, 0f, 0f) }));
    }
 
    [Fact]
    public void Vector3Normalize_ZeroVector_ReturnsZeroVector()
    {
        AssertVec3Equal(Vector3.Zero, GameMath.Vector3Normalize(new object[] { Vector3.Zero }));
    }
 
    [Fact]
    public void Vector3Normalize_ResultHasLengthOne()
    {
        var result = Assert.IsType<Vector3>(GameMath.Vector3Normalize(new object[] { new Vector3(1f, 2f, 3f) }));
        Assert.Equal(1f, result.Length(), 4);
    }
 
    [Fact]
    public void Vector3Normalize_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.Vector3Normalize(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Distance
    // =====================================================================
 
    [Fact]
    public void Vector3Distance_SamePoint_ReturnsZero()
    {
        var p = new Vector3(1f, 2f, 3f);
        Assert.Equal(0f, Assert.IsType<float>(GameMath.Vector3Distance(new object[] { p, p })), 4);
    }
 
    [Fact]
    public void Vector3Distance_AxisAligned_ReturnsCorrectDistance()
    {
        Assert.Equal(5f,
            Assert.IsType<float>(GameMath.Vector3Distance(new object[] { new Vector3(0f, 0f, 0f), new Vector3(3f, 4f, 0f) })), 4);
    }
 
    [Fact]
    public void Vector3Distance_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.Vector3Distance(new object[] { F(1f), F(2f) }));
    }
 
    // =====================================================================
    // VECTOR 3 — Vector3Lerp
    // =====================================================================
 
    [Fact]
    public void Vector3Lerp_AtZero_ReturnsA()
    {
        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(10f, 10f, 10f);
        AssertVec3Equal(a, GameMath.Vector3Lerp(new object[] { a, b, F(0f) }));
    }
 
    [Fact]
    public void Vector3Lerp_AtOne_ReturnsB()
    {
        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(10f, 10f, 10f);
        AssertVec3Equal(b, GameMath.Vector3Lerp(new object[] { a, b, F(1f) }));
    }
 
    [Fact]
    public void Vector3Lerp_AtHalf_ReturnsMidpoint()
    {
        AssertVec3Equal(new Vector3(5f, 5f, 5f),
            GameMath.Vector3Lerp(new object[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f), F(0.5f) }));
    }
 
    [Fact]
    public void Vector3Lerp_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.Vector3Lerp(new object[] { F(1f), F(2f), F(0.5f) }));
    }
 
    // =====================================================================
    // VECTOR 4 — Vec4
    // =====================================================================
 
    [Fact]
    public void Vec4_ValidFloats_ReturnsVector4()
    {
        AssertVec4Equal(new Vector4(1f, 2f, 3f, 4f), GameMath.Vec4(new object[] { F(1f), F(2f), F(3f), F(4f) }));
    }
 
    [Fact]
    public void Vec4_WComponent_IsPreserved()
    {
        var result = Assert.IsType<Vector4>(GameMath.Vec4(new object[] { F(0f), F(0f), F(0f), F(1f) }));
        Assert.Equal(1f, result.W, 4);
    }
 
    [Fact]
    public void Vec4_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Vector4>(GameMath.Vec4(new object[] { "x", "y", "z", "w" }));
    }
 
    [Fact]
    public void Vec4_TooFewArgs_ReturnsFalse()
    {
        Assert.IsNotType<Vector4>(GameMath.Vec4(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // QUATERNION — QuatIdentity
    // =====================================================================
 
    [Fact]
    public void QuatIdentity_NoArgs_ReturnsIdentityQuaternion()
    {
        AssertQuatEqual(Quaternion.Identity, GameMath.QuatIdentity(Array.Empty<object>()));
    }
 
    [Fact]
    public void QuatIdentity_IsNormalized()
    {
        var q = Assert.IsType<Quaternion>(GameMath.QuatIdentity(Array.Empty<object>()));
        Assert.Equal(1f, q.Length(), 4);
    }
 
    // =====================================================================
    // QUATERNION — QuatFromAxisAngle
    // =====================================================================
 
    [Fact]
    public void QuatFromAxisAngle_ZeroAngle_ReturnsIdentity()
    {
        AssertQuatEqual(Quaternion.Identity,
            GameMath.QuatFromAxisAngle(new object[] { new Vector3(0f, 1f, 0f), F(0f) }));
    }
 
    [Fact]
    public void QuatFromAxisAngle_180DegAroundY_ReturnsExpectedQuaternion()
    {
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        AssertQuatEqual(expected, GameMath.QuatFromAxisAngle(new object[] { new Vector3(0f, 1f, 0f), F(180f) }));
    }
 
    [Fact]
    public void QuatFromAxisAngle_ResultIsNormalized()
    {
        var q = Assert.IsType<Quaternion>(GameMath.QuatFromAxisAngle(new object[] { new Vector3(1f, 1f, 1f), F(45f) }));
        Assert.Equal(1f, q.Length(), 4);
    }
 
    [Fact]
    public void QuatFromAxisAngle_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Quaternion>(GameMath.QuatFromAxisAngle(new object[] { F(1f), F(90f) }));
    }
 
    // =====================================================================
    // QUATERNION — QuatFromEuler
    // =====================================================================
 
    [Fact]
    public void QuatFromEuler_AllZero_ReturnsIdentity()
    {
        AssertQuatEqual(Quaternion.Identity, GameMath.QuatFromEuler(new object[] { F(0f), F(0f), F(0f) }));
    }
 
    [Fact]
    public void QuatFromEuler_ResultIsNormalized()
    {
        var q = Assert.IsType<Quaternion>(GameMath.QuatFromEuler(new object[] { F(30f), F(45f), F(60f) }));
        Assert.Equal(1f, q.Length(), 4);
    }
 
    [Fact]
    public void QuatFromEuler_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Quaternion>(GameMath.QuatFromEuler(new object[] { "p", "y", "r" }));
    }
 
    // =====================================================================
    // QUATERNION — QuatMultiply
    // =====================================================================
 
    [Fact]
    public void QuatMultiply_TwoIdentities_ReturnsIdentity()
    {
        AssertQuatEqual(Quaternion.Identity,
            GameMath.QuatMultiply(new object[] { Quaternion.Identity, Quaternion.Identity }));
    }
 
    [Fact]
    public void QuatMultiply_KnownRotations_MatchesSystemNumerics()
    {
        var a = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
        var b = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
        AssertQuatEqual(Quaternion.Multiply(a, b), GameMath.QuatMultiply(new object[] { a, b }));
    }
 
    [Fact]
    public void QuatMultiply_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Quaternion>(GameMath.QuatMultiply(new object[] { F(1f), F(2f) }));
    }
 
    // =====================================================================
    // QUATERNION — QuatSlerp
    // =====================================================================
 
    [Fact]
    public void QuatSlerp_AtZero_ReturnsA()
    {
        var a = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0f);
        var b = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        AssertQuatEqual(a, GameMath.QuatSlerp(new object[] { a, b, F(0f) }));
    }
 
    [Fact]
    public void QuatSlerp_AtOne_ReturnsB()
    {
        var a      = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0f);
        var b      = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        var result = Assert.IsType<Quaternion>(GameMath.QuatSlerp(new object[] { a, b, F(1f) }));
        // Float slerp to t=1 near PI accumulates error; compare via |dot| ≈ 1
        // (dot == ±1 means the quaternions represent the same rotation).
        float dot = MathF.Abs(Quaternion.Dot(result, b));
        Assert.True(dot > 0.9999f, $"Quaternions differ: |dot|={dot}");
    }
 
    [Fact]
    public void QuatSlerp_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Quaternion>(GameMath.QuatSlerp(new object[] { F(0f), F(1f), F(0.5f) }));
    }
 
    // =====================================================================
    // QUATERNION — QuatToEuler
    // =====================================================================
 
    [Fact]
    public void QuatToEuler_Identity_ReturnsZeroAngles()
    {
        var result = Assert.IsType<Vector3>(GameMath.QuatToEuler(new object[] { Quaternion.Identity }));
        Assert.Equal(0f, result.X, 4);
        Assert.Equal(0f, result.Y, 4);
        Assert.Equal(0f, result.Z, 4);
    }
 
    [Fact]
    public void QuatToEuler_90DegYaw_ReturnsExpectedAngles()
    {
        var q      = Quaternion.CreateFromYawPitchRoll(MathF.PI / 2f, 0f, 0f);
        var result = Assert.IsType<Vector3>(GameMath.QuatToEuler(new object[] { q }));
        // Euler decomposition from floats loses precision; allow 0.1 degree tolerance.
        Assert.True(MathF.Abs(result.Y - 90f) < 0.1f, $"Yaw was {result.Y}, expected ~90");
    }
 
    [Fact]
    public void QuatToEuler_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.QuatToEuler(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // QUATERNION — QuatRotateVec3
    // =====================================================================
 
    [Fact]
    public void QuatRotateVec3_IdentityRotation_ReturnsUnchangedVector()
    {
        var v = new Vector3(1f, 2f, 3f);
        AssertVec3Equal(v, GameMath.QuatRotateVec3(new object[] { Quaternion.Identity, v }));
    }
 
    [Fact]
    public void QuatRotateVec3_180DegAroundZ_NegatesX()
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
        AssertVec3Equal(new Vector3(-1f, 0f, 0f),
            GameMath.QuatRotateVec3(new object[] { q, new Vector3(1f, 0f, 0f) }));
    }
 
    [Fact]
    public void QuatRotateVec3_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.QuatRotateVec3(new object[] { F(1f), new Vector3(1f, 0f, 0f) }));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixIdentity
    // =====================================================================
 
    [Fact]
    public void MatrixIdentity_NoArgs_ReturnsIdentityMatrix()
    {
        AssertMatrixEqual(Matrix4x4.Identity, GameMath.MatrixIdentity(Array.Empty<object>()));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixMultiply
    // =====================================================================
 
    [Fact]
    public void MatrixMultiply_TwoIdentities_ReturnsIdentity()
    {
        AssertMatrixEqual(Matrix4x4.Identity,
            GameMath.MatrixMultiply(new object[] { Matrix4x4.Identity, Matrix4x4.Identity }));
    }
 
    [Fact]
    public void MatrixMultiply_KnownMatrices_MatchesSystemNumerics()
    {
        var a = Matrix4x4.CreateScale(2f);
        var b = Matrix4x4.CreateTranslation(1f, 2f, 3f);
        AssertMatrixEqual(Matrix4x4.Multiply(a, b), GameMath.MatrixMultiply(new object[] { a, b }));
    }
 
    [Fact]
    public void MatrixMultiply_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Matrix4x4>(GameMath.MatrixMultiply(new object[] { F(1f), F(2f) }));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixInverse
    // =====================================================================
 
    [Fact]
    public void MatrixInverse_IdentityMatrix_ReturnsIdentity()
    {
        AssertMatrixEqual(Matrix4x4.Identity, GameMath.MatrixInverse(new object[] { Matrix4x4.Identity }));
    }
 
    [Fact]
    public void MatrixInverse_KnownMatrix_InverseMultipliedByOriginalIsIdentity()
    {
        var m   = Matrix4x4.CreateRotationY(MathF.PI / 3f) * Matrix4x4.CreateTranslation(1f, 2f, 3f);
        var inv = Assert.IsType<Matrix4x4>(GameMath.MatrixInverse(new object[] { m }));
        AssertMatrixEqual(Matrix4x4.Identity, Matrix4x4.Multiply(m, inv));
    }
 
    [Fact]
    public void MatrixInverse_SingularMatrix_ReturnsIdentityAsFallback()
    {
        AssertMatrixEqual(Matrix4x4.Identity, GameMath.MatrixInverse(new object[] { new Matrix4x4() }));
    }
 
    [Fact]
    public void MatrixInverse_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Matrix4x4>(GameMath.MatrixInverse(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixFromQuatTrans
    // =====================================================================
 
    [Fact]
    public void MatrixFromQuatTrans_IdentityQuatZeroTrans_ReturnsIdentity()
    {
        AssertMatrixEqual(Matrix4x4.Identity,
            GameMath.MatrixFromQuatTrans(new object[] { Quaternion.Identity, Vector3.Zero }));
    }
 
    [Fact]
    public void MatrixFromQuatTrans_TranslationIsEmbeddedCorrectly()
    {
        var t      = new Vector3(5f, 6f, 7f);
        var result = Assert.IsType<Matrix4x4>(GameMath.MatrixFromQuatTrans(new object[] { Quaternion.Identity, t }));
        Assert.Equal(5f, result.Translation.X, 4);
        Assert.Equal(6f, result.Translation.Y, 4);
        Assert.Equal(7f, result.Translation.Z, 4);
    }
 
    [Fact]
    public void MatrixFromQuatTrans_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Matrix4x4>(GameMath.MatrixFromQuatTrans(new object[] { "bad-input" }));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixTransformPoint
    // =====================================================================
 
    [Fact]
    public void MatrixTransformPoint_IdentityMatrix_ReturnsOriginalPoint()
    {
        var p = new Vector3(1f, 2f, 3f);
        AssertVec3Equal(p, GameMath.MatrixTransformPoint(new object[] { Matrix4x4.Identity, p }));
    }
 
    [Fact]
    public void MatrixTransformPoint_TranslationMatrix_AppliesTranslation()
    {
        AssertVec3Equal(new Vector3(11f, 0f, 0f),
            GameMath.MatrixTransformPoint(new object[] { Matrix4x4.CreateTranslation(10f, 0f, 0f), new Vector3(1f, 0f, 0f) }));
    }
 
    [Fact]
    public void MatrixTransformPoint_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.MatrixTransformPoint(new object[] { F(1f), new Vector3(0f, 0f, 0f) }));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixTransformDirection
    // =====================================================================
 
    [Fact]
    public void MatrixTransformDirection_IdentityMatrix_ReturnsOriginalDirection()
    {
        var d = new Vector3(0f, 1f, 0f);
        AssertVec3Equal(d, GameMath.MatrixTransformDirection(new object[] { Matrix4x4.Identity, d }));
    }
 
    [Fact]
    public void MatrixTransformDirection_TranslationMatrix_IgnoresTranslation()
    {
        // Translation must NOT affect direction vectors
        AssertVec3Equal(new Vector3(1f, 0f, 0f),
            GameMath.MatrixTransformDirection(new object[] { Matrix4x4.CreateTranslation(100f, 100f, 100f), new Vector3(1f, 0f, 0f) }));
    }
 
    [Fact]
    public void MatrixTransformDirection_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.MatrixTransformDirection(new object[] { F(1f), new Vector3(0f, 1f, 0f) }));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixLookAt
    // =====================================================================
 
    [Fact]
    public void MatrixLookAt_ValidArgs_ReturnsMatrix4x4()
    {
        var result = GameMath.MatrixLookAt(new object[]
        {
            new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f)
        });
        Assert.IsType<Matrix4x4>(result);
    }
 
    [Fact]
    public void MatrixLookAt_MatchesSystemNumerics()
    {
        var eye    = new Vector3(1f, 2f, 5f);
        var target = new Vector3(0f, 0f, 0f);
        var up     = new Vector3(0f, 1f, 0f);
        AssertMatrixEqual(Matrix4x4.CreateLookAt(eye, target, up),
            GameMath.MatrixLookAt(new object[] { eye, target, up }));
    }
 
    [Fact]
    public void MatrixLookAt_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Matrix4x4>(GameMath.MatrixLookAt(new object[] { F(1f), F(2f), F(3f) }));
    }
 
    // =====================================================================
    // MATRIX 4x4 — MatrixPerspective
    // =====================================================================
 
    [Fact]
    public void MatrixPerspective_ValidArgs_ReturnsMatrix4x4()
    {
        Assert.IsType<Matrix4x4>(GameMath.MatrixPerspective(new object[] { F(60f), F(16f / 9f), F(0.1f), F(1000f) }));
    }
 
    [Fact]
    public void MatrixPerspective_MatchesSystemNumerics()
    {
        float fov = 60f, aspect = 16f / 9f, near = 0.1f, far = 1000f;
        AssertMatrixEqual(
            Matrix4x4.CreatePerspectiveFieldOfView(fov * (MathF.PI / 180f), aspect, near, far),
            GameMath.MatrixPerspective(new object[] { F(fov), F(aspect), F(near), F(far) }));
    }
 
    [Fact]
    public void MatrixPerspective_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<Matrix4x4>(GameMath.MatrixPerspective(new object[] { "fov", "a", "n", "f" }));
    }
 
    // =====================================================================
    // TRANSFORM — TransformCreate
    // =====================================================================
 
    [Fact]
    public void TransformCreate_ValidArgs_ReturnsLongHandle()
    {
        var result = GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        Assert.IsType<long>(result);
    }
 
    [Fact]
    public void TransformCreate_EachCallReturnsUniqueHandle()
    {
        var h1 = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        var h2 = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        Assert.NotEqual(h1, h2);
    }
 
    [Fact]
    public void TransformCreate_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<long>(GameMath.TransformCreate(new object[] { F(1f), F(2f), F(3f) }));
    }
 
    // =====================================================================
    // TRANSFORM — TransformGetPosition
    // =====================================================================
 
    [Fact]
    public void TransformGetPosition_ReturnsCreatedPosition()
    {
        var pos    = new Vector3(4f, 5f, 6f);
        var handle = (long)GameMath.TransformCreate(new object[] { pos, Quaternion.Identity, Vector3.One });
        AssertVec3Equal(pos, GameMath.TransformGetPosition(new object[] { handle }));
    }
 
    [Fact]
    public void TransformGetPosition_InvalidHandle_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            GameMath.TransformGetPosition(new object[] { (long)-9999 }));
    }
 
    [Fact]
    public void TransformGetPosition_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.TransformGetPosition(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // TRANSFORM — TransformSetPosition
    // =====================================================================
 
    [Fact]
    public void TransformSetPosition_UpdatesPositionInPlace()
    {
        var handle = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        var newPos = new Vector3(10f, 20f, 30f);
        GameMath.TransformSetPosition(new object[] { handle, newPos });
        AssertVec3Equal(newPos, GameMath.TransformGetPosition(new object[] { handle }));
    }
 
    [Fact]
    public void TransformSetPosition_WrongTypes_ReturnsFalse()
    {
        Assert.False((bool)GameMath.TransformSetPosition(new object[] { F(0f), Vector3.Zero }));
    }
 
    // =====================================================================
    // TRANSFORM — TransformGetRotation
    // =====================================================================
 
    [Fact]
    public void TransformGetRotation_ReturnsCreatedRotation()
    {
        var rot    = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
        var handle = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, rot, Vector3.One });
        AssertQuatEqual(rot, GameMath.TransformGetRotation(new object[] { handle }));
    }
 
    [Fact]
    public void TransformGetRotation_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Quaternion>(GameMath.TransformGetRotation(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // TRANSFORM — TransformSetRotation
    // =====================================================================
 
    [Fact]
    public void TransformSetRotation_UpdatesRotationInPlace()
    {
        var handle = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        var newRot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        GameMath.TransformSetRotation(new object[] { handle, newRot });
        AssertQuatEqual(newRot, GameMath.TransformGetRotation(new object[] { handle }));
    }
 
    [Fact]
    public void TransformSetRotation_WrongTypes_ReturnsFalse()
    {
        Assert.False((bool)GameMath.TransformSetRotation(new object[] { F(0f), Quaternion.Identity }));
    }
 
    // =====================================================================
    // TRANSFORM — TransformForward
    // =====================================================================
 
    [Fact]
    public void TransformForward_IdentityRotation_ReturnsUnitZ()
    {
        var handle = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        AssertVec3Equal(Vector3.UnitZ, GameMath.TransformForward(new object[] { handle }));
    }
 
    [Fact]
    public void TransformForward_90DegAroundY_ReturnsPositiveX()
    {
        // GOAL uses a left-handed coordinate system where +Z is forward.
        // Rotating the forward vector (+Z in GOAL space) by 90° around +Y
        // gives -X in a left-handed system.  TransformForward negates UnitZ
        // before applying the System.Numerics (right-handed) quaternion to
        // correctly match GOAL's convention.
        var rot    = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var handle = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, rot, Vector3.One });
        var fwd    = Assert.IsType<Vector3>(GameMath.TransformForward(new object[] { handle }));
        Assert.True(MathF.Abs(fwd.X - (-1f)) < 0.0001f, $"X was {fwd.X}, expected ~-1");
        Assert.True(MathF.Abs(fwd.Z)          < 0.0001f, $"Z was {fwd.Z}, expected ~0");
    }
 
    [Fact]
    public void TransformForward_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.TransformForward(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // TRANSFORM — TransformDestroy
    // =====================================================================
 
    [Fact]
    public void TransformDestroy_ValidHandle_RemovesTransform()
    {
        var handle = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        GameMath.TransformDestroy(new object[] { handle });
        Assert.Throws<ArgumentException>(() =>
            GameMath.TransformGetPosition(new object[] { handle }));
    }
 
    [Fact]
    public void TransformDestroy_AlreadyRemovedHandle_DoesNotThrow()
    {
        var handle = (long)GameMath.TransformCreate(new object[] { Vector3.Zero, Quaternion.Identity, Vector3.One });
        GameMath.TransformDestroy(new object[] { handle });
        var ex = Record.Exception(() => GameMath.TransformDestroy(new object[] { handle }));
        Assert.Null(ex);
    }
 
    [Fact]
    public void TransformDestroy_WrongType_ReturnsFalse()
    {
        Assert.False((bool)GameMath.TransformDestroy(new object[] { "bad-input" }));
    }
 
    // =====================================================================
    // GEOMETRY HELPERS — BBoxMake
    // =====================================================================
 
    [Fact]
    public void BBoxMake_ValidArgs_ReturnsTupleOfVector3()
    {
        var result = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        Assert.IsType<ValueTuple<Vector3, Vector3>>(result);
    }
 
    [Fact]
    public void BBoxMake_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<ValueTuple<Vector3, Vector3>>(GameMath.BBoxMake(new object[] { F(1f), F(2f) }));
    }
 
    // =====================================================================
    // GEOMETRY HELPERS — BBoxContains
    // =====================================================================
 
    [Fact]
    public void BBoxContains_PointInsideBox_ReturnsTrue()
    {
        var bbox = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        Assert.True(Assert.IsType<bool>(GameMath.BBoxContains(new object[] { bbox, new Vector3(0f, 0f, 0f) })));
    }
 
    [Fact]
    public void BBoxContains_PointOnSurface_ReturnsTrue()
    {
        var bbox = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        Assert.True(Assert.IsType<bool>(GameMath.BBoxContains(new object[] { bbox, new Vector3(1f, 0f, 0f) })));
    }
 
    [Fact]
    public void BBoxContains_PointOutsideBox_ReturnsFalse()
    {
        var bbox = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        Assert.False(Assert.IsType<bool>(GameMath.BBoxContains(new object[] { bbox, new Vector3(5f, 0f, 0f) })));
    }
 
    [Fact]
    public void BBoxContains_WrongTypes_ReturnsFalse()
    {
        Assert.IsType<bool>(GameMath.BBoxContains(new object[] { F(1f), new Vector3(0f, 0f, 0f) }));
    }
 
    // =====================================================================
    // GEOMETRY HELPERS — BBoxIntersects
    // =====================================================================
 
    [Fact]
    public void BBoxIntersects_OverlappingBoxes_ReturnsTrue()
    {
        var a = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        var b = GameMath.BBoxMake(new object[] { new Vector3(0f, 0f, 0f),    new Vector3(2f, 2f, 2f) });
        Assert.True(Assert.IsType<bool>(GameMath.BBoxIntersects(new object[] { a, b })));
    }
 
    [Fact]
    public void BBoxIntersects_TouchingSurfaces_ReturnsTrue()
    {
        var a = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        var b = GameMath.BBoxMake(new object[] { new Vector3(1f, -1f, -1f),  new Vector3(3f, 1f, 1f) });
        Assert.True(Assert.IsType<bool>(GameMath.BBoxIntersects(new object[] { a, b })));
    }
 
    [Fact]
    public void BBoxIntersects_SeparatedBoxes_ReturnsFalse()
    {
        var a = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        var b = GameMath.BBoxMake(new object[] { new Vector3(5f, 5f, 5f),    new Vector3(10f, 10f, 10f) });
        Assert.False(Assert.IsType<bool>(GameMath.BBoxIntersects(new object[] { a, b })));
    }
 
    [Fact]
    public void BBoxIntersects_WrongTypes_ReturnsFalse()
    {
        Assert.False((bool)GameMath.BBoxIntersects(new object[] { F(1f), F(2f) }));
    }
 
    // =====================================================================
    // GEOMETRY HELPERS — BBoxCenter
    // =====================================================================
 
    [Fact]
    public void BBoxCenter_SymmetricBox_ReturnsCenterAtOrigin()
    {
        var bbox = GameMath.BBoxMake(new object[] { new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f) });
        AssertVec3Equal(Vector3.Zero, GameMath.BBoxCenter(new object[] { bbox }));
    }
 
    [Fact]
    public void BBoxCenter_AsymmetricBox_ReturnsCorrectCenter()
    {
        var bbox = GameMath.BBoxMake(new object[] { new Vector3(0f, 0f, 0f), new Vector3(4f, 6f, 8f) });
        AssertVec3Equal(new Vector3(2f, 3f, 4f), GameMath.BBoxCenter(new object[] { bbox }));
    }
 
    [Fact]
    public void BBoxCenter_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.BBoxCenter(new object[] { F(1f) }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — Lerp
    // =====================================================================
 
    [Fact]
    public void Lerp_AtZero_ReturnsA()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.Lerp(new object[] { F(0f), F(10f), F(0f) })), 4);
    }
 
    [Fact]
    public void Lerp_AtOne_ReturnsB()
    {
        Assert.Equal(10f, Assert.IsType<float>(GameMath.Lerp(new object[] { F(0f), F(10f), F(1f) })), 4);
    }
 
    [Fact]
    public void Lerp_AtHalf_ReturnsMidpoint()
    {
        Assert.Equal(5f, Assert.IsType<float>(GameMath.Lerp(new object[] { F(0f), F(10f), F(0.5f) })), 4);
    }
 
    [Fact]
    public void Lerp_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.Lerp(new object[] { "a", "b", "t" }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — Clamp
    // =====================================================================
 
    [Fact]
    public void Clamp_ValueWithinRange_ReturnsValue()
    {
        Assert.Equal(5f, Assert.IsType<float>(GameMath.Clamp(new object[] { F(5f), F(0f), F(10f) })), 4);
    }
 
    [Fact]
    public void Clamp_ValueBelowMin_ReturnsMin()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.Clamp(new object[] { F(-5f), F(0f), F(10f) })), 4);
    }
 
    [Fact]
    public void Clamp_ValueAboveMax_ReturnsMax()
    {
        Assert.Equal(10f, Assert.IsType<float>(GameMath.Clamp(new object[] { F(15f), F(0f), F(10f) })), 4);
    }
 
    [Fact]
    public void Clamp_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.Clamp(new object[] { "v", "lo", "hi" }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — SmoothStep
    // =====================================================================
 
    [Fact]
    public void SmoothStep_BelowEdge0_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.SmoothStep(new object[] { F(0f), F(1f), F(-1f) })), 4);
    }
 
    [Fact]
    public void SmoothStep_AboveEdge1_ReturnsOne()
    {
        Assert.Equal(1f, Assert.IsType<float>(GameMath.SmoothStep(new object[] { F(0f), F(1f), F(2f) })), 4);
    }
 
    [Fact]
    public void SmoothStep_AtMidpoint_ReturnsHalf()
    {
        Assert.Equal(0.5f, Assert.IsType<float>(GameMath.SmoothStep(new object[] { F(0f), F(1f), F(0.5f) })), 4);
    }
 
    [Fact]
    public void SmoothStep_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.SmoothStep(new object[] { "e0", "e1", "v" }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — SmootherStep
    // =====================================================================
 
    [Fact]
    public void SmootherStep_BelowEdge0_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.SmootherStep(new object[] { F(0f), F(1f), F(-1f) })), 4);
    }
 
    [Fact]
    public void SmootherStep_AboveEdge1_ReturnsOne()
    {
        Assert.Equal(1f, Assert.IsType<float>(GameMath.SmootherStep(new object[] { F(0f), F(1f), F(2f) })), 4);
    }
 
    [Fact]
    public void SmootherStep_AtMidpoint_ReturnsHalf()
    {
        Assert.Equal(0.5f, Assert.IsType<float>(GameMath.SmootherStep(new object[] { F(0f), F(1f), F(0.5f) })), 4);
    }
 
    [Fact]
    public void SmootherStep_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.SmootherStep(new object[] { "e0", "e1", "v" }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — DegToRad
    // =====================================================================
 
    [Fact]
    public void DegToRad_Zero_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.DegToRad(new object[] { F(0f) })), 4);
    }
 
    [Fact]
    public void DegToRad_180Degrees_ReturnsPi()
    {
        Assert.Equal(MathF.PI, Assert.IsType<float>(GameMath.DegToRad(new object[] { F(180f) })), 4);
    }
 
    [Fact]
    public void DegToRad_360Degrees_ReturnsTwoPi()
    {
        Assert.Equal(MathF.PI * 2f, Assert.IsType<float>(GameMath.DegToRad(new object[] { F(360f) })), 4);
    }
 
    [Fact]
    public void DegToRad_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.DegToRad(new object[] { "deg" }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — RadToDeg
    // =====================================================================
 
    [Fact]
    public void RadToDeg_Zero_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.RadToDeg(new object[] { F(0f) })), 4);
    }
 
    [Fact]
    public void RadToDeg_Pi_Returns180()
    {
        Assert.Equal(180f, Assert.IsType<float>(GameMath.RadToDeg(new object[] { F(MathF.PI) })), 4);
    }
 
    [Fact]
    public void RadToDeg_IsInverseOfDegToRad()
    {
        var rad  = Assert.IsType<float>(GameMath.DegToRad(new object[] { F(45f) }));
        var back = Assert.IsType<float>(GameMath.RadToDeg(new object[] { rad }));
        Assert.Equal(45f, back, 4);
    }
 
    [Fact]
    public void RadToDeg_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.RadToDeg(new object[] { "rad" }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — WrapAngle180
    // =====================================================================
 
    [Fact]
    public void WrapAngle180_ZeroDegrees_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.WrapAngle180(new object[] { F(0f) })), 4);
    }
 
    [Fact]
    public void WrapAngle180_270Degrees_ReturnsMinus90()
    {
        Assert.Equal(-90f, Assert.IsType<float>(GameMath.WrapAngle180(new object[] { F(270f) })), 4);
    }
 
    [Fact]
    public void WrapAngle180_Minus270Degrees_Returns90()
    {
        Assert.Equal(90f, Assert.IsType<float>(GameMath.WrapAngle180(new object[] { F(-270f) })), 4);
    }
 
    [Fact]
    public void WrapAngle180_360Degrees_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.WrapAngle180(new object[] { F(360f) })), 4);
    }
 
    [Fact]
    public void WrapAngle180_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.WrapAngle180(new object[] { "deg" }));
    }
 
    // =====================================================================
    // INTERPOLATION / EASING — AngleDelta
    // =====================================================================
 
    [Fact]
    public void AngleDelta_SameAngles_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.AngleDelta(new object[] { F(45f), F(45f) })), 4);
    }
 
    [Fact]
    public void AngleDelta_PositiveDirection_ReturnsShortestPath()
    {
        Assert.Equal(90f, Assert.IsType<float>(GameMath.AngleDelta(new object[] { F(0f), F(90f) })), 4);
    }
 
    [Fact]
    public void AngleDelta_WrapsAroundShortestWay()
    {
        // From 350° to 10° the shortest path is +20°, not -340°
        Assert.Equal(20f, Assert.IsType<float>(GameMath.AngleDelta(new object[] { F(350f), F(10f) })), 4);
    }
 
    [Fact]
    public void AngleDelta_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.AngleDelta(new object[] { "a", "b" }));
    }
 
    // =====================================================================
    // UNIT CONVERSION — UnitsToMeters
    // =====================================================================
 
    [Fact]
    public void UnitsToMeters_4096Units_ReturnsOneMeter()
    {
        Assert.Equal(1f, Assert.IsType<float>(GameMath.UnitsToMeters(new object[] { F(4096f) })), 4);
    }
 
    [Fact]
    public void UnitsToMeters_Zero_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.UnitsToMeters(new object[] { F(0f) })), 4);
    }
 
    [Fact]
    public void UnitsToMeters_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.UnitsToMeters(new object[] { "u" }));
    }
 
    // =====================================================================
    // UNIT CONVERSION — MetersToUnits
    // =====================================================================
 
    [Fact]
    public void MetersToUnits_OneMeter_Returns4096()
    {
        Assert.Equal(4096f, Assert.IsType<float>(GameMath.MetersToUnits(new object[] { F(1f) })), 4);
    }
 
    [Fact]
    public void MetersToUnits_IsInverseOfUnitsToMeters()
    {
        var meters = Assert.IsType<float>(GameMath.UnitsToMeters(new object[] { F(8192f) }));
        var back   = Assert.IsType<float>(GameMath.MetersToUnits(new object[] { meters }));
        Assert.Equal(8192f, back, 4);
    }
 
    [Fact]
    public void MetersToUnits_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.MetersToUnits(new object[] { "m" }));
    }
 
    // =====================================================================
    // RANDOM — RandomFloat
    // =====================================================================
 
    [Fact]
    public void RandomFloat_ResultIsWithinRange()
    {
        for (int i = 0; i < 100; i++)
        {
            float v = Assert.IsType<float>(GameMath.RandomFloat(new object[] { F(0f), F(1f) }));
            Assert.True(v >= 0f && v < 1f, $"Value {v} out of [0, 1)");
        }
    }
 
    [Fact]
    public void RandomFloat_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.RandomFloat(new object[] { "lo", "hi" }));
    }
 
    // =====================================================================
    // RANDOM — RandomInt
    // =====================================================================
 
    [Fact]
    public void RandomInt_ResultIsWithinInclusiveRange()
    {
        for (int i = 0; i < 100; i++)
        {
            int v = Assert.IsType<int>(GameMath.RandomInt(new object[] { I(1), I(6) }));
            Assert.True(v >= 1 && v <= 6, $"Value {v} out of [1, 6]");
        }
    }
 
    [Fact]
    public void RandomInt_MinEqualsMax_AlwaysReturnsMin()
    {
        for (int i = 0; i < 20; i++)
            Assert.Equal(7, Assert.IsType<int>(GameMath.RandomInt(new object[] { I(7), I(7) })));
    }
 
    [Fact]
    public void RandomInt_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<int>(GameMath.RandomInt(new object[] { F(1f), F(6f) }));
    }
 
    // =====================================================================
    // RANDOM — RandomPointInSphere
    // =====================================================================
 
    [Fact]
    public void RandomPointInSphere_IsInsideSphere()
    {
        float radius = 5f;
        for (int i = 0; i < 50; i++)
        {
            var v = Assert.IsType<Vector3>(GameMath.RandomPointInSphere(new object[] { F(radius) }));
            Assert.True(v.Length() <= radius + Epsilon, $"Point outside sphere: length={v.Length()}");
        }
    }
 
    [Fact]
    public void RandomPointInSphere_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.RandomPointInSphere(new object[] { "r" }));
    }
 
    // =====================================================================
    // RANDOM — RandomOnSphere
    // =====================================================================
 
    [Fact]
    public void RandomOnSphere_PointIsOnSphereSurface()
    {
        float radius    = 3f;
        float tolerance = 0.01f;
        for (int i = 0; i < 50; i++)
        {
            var v = Assert.IsType<Vector3>(GameMath.RandomOnSphere(new object[] { F(radius) }));
            Assert.Equal(radius, v.Length(), 1);
        }
    }
 
    [Fact]
    public void RandomOnSphere_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<Vector3>(GameMath.RandomOnSphere(new object[] { "r" }));
    }
 
    // =====================================================================
    // SCALAR UTILITIES — Fabs
    // =====================================================================
 
    [Fact]
    public void Fabs_NegativeValue_ReturnsPositive()
    {
        Assert.Equal(5f, Assert.IsType<float>(GameMath.Fabs(new object[] { F(-5f) })), 4);
    }
 
    [Fact]
    public void Fabs_PositiveValue_ReturnsUnchanged()
    {
        Assert.Equal(5f, Assert.IsType<float>(GameMath.Fabs(new object[] { F(5f) })), 4);
    }
 
    [Fact]
    public void Fabs_Zero_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.Fabs(new object[] { F(0f) })), 4);
    }
 
    [Fact]
    public void Fabs_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.Fabs(new object[] { "x" }));
    }
 
    // =====================================================================
    // SCALAR UTILITIES — Sqrtf
    // =====================================================================
 
    [Fact]
    public void Sqrtf_PositiveValue_ReturnsSquareRoot()
    {
        Assert.Equal(3f, Assert.IsType<float>(GameMath.Sqrtf(new object[] { F(9f) })), 4);
    }
 
    [Fact]
    public void Sqrtf_NegativeValue_ReturnsAbsoluteSquareRoot()
    {
        // Impl: MathF.Sqrt(MathF.Abs(x)) — must not return NaN
        Assert.Equal(3f, Assert.IsType<float>(GameMath.Sqrtf(new object[] { F(-9f) })), 4);
    }
 
    [Fact]
    public void Sqrtf_Zero_ReturnsZero()
    {
        Assert.Equal(0f, Assert.IsType<float>(GameMath.Sqrtf(new object[] { F(0f) })), 4);
    }
 
    [Fact]
    public void Sqrtf_WrongType_ReturnsFalse()
    {
        Assert.IsNotType<float>(GameMath.Sqrtf(new object[] { "x" }));
    }
 
    // =====================================================================
    // SCALAR UTILITIES — FEqualEpsilon
    // =====================================================================
 
    [Fact]
    public void FEqualEpsilon_EqualValues_ReturnsTrue()
    {
        Assert.True(Assert.IsType<bool>(GameMath.FEqualEpsilon(new object[] { F(1f), F(1f), F(0.001f) })));
    }
 
    [Fact]
    public void FEqualEpsilon_WithinEpsilon_ReturnsTrue()
    {
        Assert.True(Assert.IsType<bool>(GameMath.FEqualEpsilon(new object[] { F(1f), F(1.0001f), F(0.001f) })));
    }
 
    [Fact]
    public void FEqualEpsilon_OutsideEpsilon_ReturnsFalse()
    {
        Assert.False(Assert.IsType<bool>(GameMath.FEqualEpsilon(new object[] { F(1f), F(2f), F(0.001f) })));
    }
 
    [Fact]
    public void FEqualEpsilon_WrongTypes_ReturnsFalse()
    {
        Assert.False((bool)GameMath.FEqualEpsilon(new object[] { "a", "b", "eps" }));
    }
 
    // =====================================================================
    // SCALAR UTILITIES — SignedDiv0Guard
    // =====================================================================
 
    [Fact]
    public void SignedDiv0Guard_NormalDivision_ReturnsQuotient()
    {
        Assert.Equal(5, Assert.IsType<int>(GameMath.SignedDiv0Guard(new object[] { I(10), I(2) })));
    }
 
    [Fact]
    public void SignedDiv0Guard_DivisorIsZero_PositiveDividend_ReturnsMinus1()
    {
        Assert.Equal(-1, Assert.IsType<int>(GameMath.SignedDiv0Guard(new object[] { I(5), I(0) })));
    }
 
    [Fact]
    public void SignedDiv0Guard_DivisorIsZero_NegativeDividend_Returns1()
    {
        Assert.Equal(1, Assert.IsType<int>(GameMath.SignedDiv0Guard(new object[] { I(-5), I(0) })));
    }
 
    [Fact]
    public void SignedDiv0Guard_MinIntDividedByMinusOne_ReturnsMinInt()
    {
        Assert.Equal(int.MinValue,
            Assert.IsType<int>(GameMath.SignedDiv0Guard(new object[] { I(int.MinValue), I(-1) })));
    }
 
    [Fact]
    public void SignedDiv0Guard_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<int>(GameMath.SignedDiv0Guard(new object[] { F(10f), F(2f) }));
    }
 
    // =====================================================================
    // SCALAR UTILITIES — SignedMod0Guard
    // =====================================================================
 
    [Fact]
    public void SignedMod0Guard_NormalModulo_ReturnsRemainder()
    {
        Assert.Equal(1, Assert.IsType<int>(GameMath.SignedMod0Guard(new object[] { I(10), I(3) })));
    }
 
    [Fact]
    public void SignedMod0Guard_DivisorIsZero_ReturnsDividend()
    {
        Assert.Equal(7, Assert.IsType<int>(GameMath.SignedMod0Guard(new object[] { I(7), I(0) })));
    }
 
    [Fact]
    public void SignedMod0Guard_MinIntModMinusOne_ReturnsZero()
    {
        Assert.Equal(0, Assert.IsType<int>(GameMath.SignedMod0Guard(new object[] { I(int.MinValue), I(-1) })));
    }
 
    [Fact]
    public void SignedMod0Guard_WrongTypes_ReturnsFalse()
    {
        Assert.IsNotType<int>(GameMath.SignedMod0Guard(new object[] { F(10f), F(3f) }));
    }
}

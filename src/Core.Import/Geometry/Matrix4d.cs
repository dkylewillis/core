namespace Core.Import.Geometry;

/// <summary>4x4 affine transform, row-major, matching AutoCAD Matrix3d / COREX matrix4.</summary>
public readonly struct Matrix4d
{
    public readonly double M00, M01, M02, M03;
    public readonly double M10, M11, M12, M13;
    public readonly double M20, M21, M22, M23;
    public readonly double M30, M31, M32, M33;

    public Matrix4d(
        double m00, double m01, double m02, double m03,
        double m10, double m11, double m12, double m13,
        double m20, double m21, double m22, double m23,
        double m30, double m31, double m32, double m33)
    {
        M00 = m00; M01 = m01; M02 = m02; M03 = m03;
        M10 = m10; M11 = m11; M12 = m12; M13 = m13;
        M20 = m20; M21 = m21; M22 = m22; M23 = m23;
        M30 = m30; M31 = m31; M32 = m32; M33 = m33;
    }

    public static Matrix4d Identity { get; } = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    public static Matrix4d FromArray(double[] m)
    {
        if (m.Length != 16) throw new ArgumentException("matrix4 requires 16 values.");
        return new Matrix4d(
            m[0], m[1], m[2], m[3],
            m[4], m[5], m[6], m[7],
            m[8], m[9], m[10], m[11],
            m[12], m[13], m[14], m[15]);
    }

    public double[] ToArray() =>
    [
        M00, M01, M02, M03,
        M10, M11, M12, M13,
        M20, M21, M22, M23,
        M30, M31, M32, M33
    ];

    /// <summary>Compose this * other (apply other first, then this).</summary>
    public Matrix4d Multiply(Matrix4d o) => new(
        M00 * o.M00 + M01 * o.M10 + M02 * o.M20 + M03 * o.M30,
        M00 * o.M01 + M01 * o.M11 + M02 * o.M21 + M03 * o.M31,
        M00 * o.M02 + M01 * o.M12 + M02 * o.M22 + M03 * o.M32,
        M00 * o.M03 + M01 * o.M13 + M02 * o.M23 + M03 * o.M33,
        M10 * o.M00 + M11 * o.M10 + M12 * o.M20 + M13 * o.M30,
        M10 * o.M01 + M11 * o.M11 + M12 * o.M21 + M13 * o.M31,
        M10 * o.M02 + M11 * o.M12 + M12 * o.M22 + M13 * o.M32,
        M10 * o.M03 + M11 * o.M13 + M12 * o.M23 + M13 * o.M33,
        M20 * o.M00 + M21 * o.M10 + M22 * o.M20 + M23 * o.M30,
        M20 * o.M01 + M21 * o.M11 + M22 * o.M21 + M23 * o.M31,
        M20 * o.M02 + M21 * o.M12 + M22 * o.M22 + M23 * o.M32,
        M20 * o.M03 + M21 * o.M13 + M22 * o.M23 + M23 * o.M33,
        M30 * o.M00 + M31 * o.M10 + M32 * o.M20 + M33 * o.M30,
        M30 * o.M01 + M31 * o.M11 + M32 * o.M21 + M33 * o.M31,
        M30 * o.M02 + M31 * o.M12 + M32 * o.M22 + M33 * o.M32,
        M30 * o.M03 + M31 * o.M13 + M32 * o.M23 + M33 * o.M33);

    public (double X, double Y, double Z) TransformPoint(double x, double y, double z) => (
        M00 * x + M01 * y + M02 * z + M03,
        M10 * x + M11 * y + M12 * z + M13,
        M20 * x + M21 * y + M22 * z + M23);

    public (double X, double Y, double Z) TransformPoint(double[] p) =>
        TransformPoint(p[0], p[1], p.Length > 2 ? p[2] : 0);
}

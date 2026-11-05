using System.Numerics;

namespace MKWSatellite.App.Rendering;

/// <summary>What the course view needs from a camera, top-down or perspective.</summary>
public interface IViewCamera
{
    /// <summary>World-to-clip matrix (row-vector convention; uploaded untransposed to GLSL).</summary>
    Matrix4x4 ViewProjection { get; }
    /// <summary>Approximate world units per screen pixel at the point of interest (sizes markers).</summary>
    float UnitsPerPixel { get; }
    Vector2 ViewportPos { get; set; }
    Vector2 ViewportSize { get; set; }
    /// <summary>Window position of a world point, or null if it is behind the camera.</summary>
    Vector2? Project(Vector3 world);
}

/// <summary>
/// Perspective camera orbiting a target point. Yaw 0 looks along -Z; pitch is the angle above the horizon.
/// Depth uses System.Numerics' [0, 1] clip range, which GL accepts (half its depth range, plenty for courses).
/// </summary>
public sealed class OrbitCamera : IViewCamera
{
    public Vector3 Target { get; set; }
    public float Yaw { get; set; } = 0.6f;
    public float Pitch { get; set; } = 0.75f;
    public float Distance { get; set; } = 500f;
    public float FieldOfView { get; set; } = 50f * MathF.PI / 180f;
    public Vector2 ViewportPos { get; set; }
    public Vector2 ViewportSize { get; set; } = new(1, 1);

    public Vector3 Eye => Target + Distance * new Vector3(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw));
    public Vector3 Forward => Vector3.Normalize(Target - Eye);

    public Matrix4x4 ViewProjection
    {
        get
        {
            var view = Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY);
            var near = MathF.Max(0.05f, Distance * 0.002f);
            var far = Distance * 20 + 30000;
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, ViewportSize.X / MathF.Max(1, ViewportSize.Y), near, far);
            return view * proj;
        }
    }

    public float UnitsPerPixel => 2 * Distance * MathF.Tan(FieldOfView / 2) / MathF.Max(1, ViewportSize.Y);

    public Vector2? Project(Vector3 world)
    {
        var c = Vector4.Transform(new Vector4(world, 1), ViewProjection);
        if (c.W <= 1e-4f) return null;
        var ndc = new Vector2(c.X / c.W, c.Y / c.W);
        return ViewportPos + new Vector2((ndc.X + 1) / 2 * ViewportSize.X, (1 - ndc.Y) / 2 * ViewportSize.Y);
    }

    public void Orbit(Vector2 deltaPixels)
    {
        Yaw -= deltaPixels.X * 0.005f;
        Pitch = Math.Clamp(Pitch + deltaPixels.Y * 0.005f, -1.55f, 1.55f);
    }

    /// <summary>Moves the target so the world under the cursor follows the mouse (at target distance).</summary>
    public void Pan(Vector2 deltaPixels)
    {
        var f = Forward;
        var right = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
        var up = Vector3.Cross(right, f);
        Target += (-deltaPixels.X * right + deltaPixels.Y * up) * UnitsPerPixel;
    }

    public void Zoom(float factor) => Distance = Math.Clamp(Distance / factor, 1f, 50000f);

    /// <summary>Moves the target horizontally (forward/right in camera terms) and vertically.</summary>
    public void Fly(Vector3 local, float seconds)
    {
        var f = Forward;
        var flat = Vector3.Normalize(new Vector3(f.X, 0, f.Z) + new Vector3(1e-6f, 0, 0));
        var right = Vector3.Normalize(Vector3.Cross(flat, Vector3.UnitY));
        var speed = MathF.Max(20f, Distance * 0.8f) * seconds;
        Target += (flat * local.Z + right * local.X + Vector3.UnitY * local.Y) * speed;
    }

    public void Frame(Vector3 min, Vector3 max)
    {
        Target = (min + max) / 2;
        Distance = MathF.Max(50f, Vector3.Distance(min, max) * 0.75f);
    }
}

namespace UnityEngine
{
public static class Mathf
{
    public const float PI = System.MathF.PI;

    public static int CeilToInt(float value) => (int)System.MathF.Ceiling(value);
    public static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);
    public static float Cos(float value) => System.MathF.Cos(value);
    public static float Max(float a, float b) => System.MathF.Max(a, b);
    public static float Min(float a, float b) => System.MathF.Min(a, b);
    public static float Sqrt(float value) => System.MathF.Sqrt(value);
}

public readonly struct Vector2
{
    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float X { get; }
    public float Y { get; }

    public static float Distance(Vector2 a, Vector2 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return System.MathF.Sqrt(dx * dx + dy * dy);
    }
}
}

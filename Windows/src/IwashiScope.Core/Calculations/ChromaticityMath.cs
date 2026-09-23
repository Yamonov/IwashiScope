using IwashiScope.Core.Models;
namespace IwashiScope.Core.Calculations;

internal static class ChromaticityMath
{
    public static Vector3 Scale(Vector3 v, double s) => new(v.First * s, v.Second * s, v.Third * s);
    public static Vector3 Add(Vector3 a, Vector3 b) => new(a.First + b.First, a.Second + b.Second, a.Third + b.Third);
    public static double Sum(Vector3 v) => v.First + v.Second + v.Third;
    public static double Component(Vector3 v, int i) => i == 0 ? v.First : i == 1 ? v.Second : v.Third;
    public static bool Nonnegative(Vector3 v) => v.IsFinite && v.First >= 0 && v.Second >= 0 && v.Third >= 0;
    public static double Dot(Vector3 a, Vector3 b) => a.First * b.First + a.Second * b.Second + a.Third * b.Third;
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Second * b.Third - a.Third * b.Second, a.Third * b.First - a.First * b.Third, a.First * b.Second - a.Second * b.First);
    public static Vector3 Combine(Vector3 x, Vector3 y, Vector3 z, Vector3 v) => Add(Add(Scale(x, v.First), Scale(y, v.Second)), Scale(z, v.Third));
    public static Vector3 Solve(Vector3 x, Vector3 y, Vector3 z, Vector3 v)
    {
        var determinant = Dot(x, Cross(y, z));
        return new(Dot(v, Cross(y, z)) / determinant, Dot(x, Cross(v, z)) / determinant, Dot(x, Cross(y, v)) / determinant);
    }
}

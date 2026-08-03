namespace IwashiScope.Core.Calculations;

public static class LabABChartScale
{
    public static double ResolveLimit(double a, double b)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b))
        {
            return 100;
        }

        var magnitude = Math.Max(Math.Abs(a), Math.Abs(b));
        return magnitude switch
        {
            <= 5 => 5,
            <= 10 => 10,
            <= 25 => 25,
            <= 50 => 50,
            _ => 100,
        };
    }
}

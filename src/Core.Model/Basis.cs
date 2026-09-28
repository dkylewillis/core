namespace Core.Model;

public enum Basis
{
    Native,
    Rule,
    Ai
}

public static class BasisExtensions
{
    public static string ToWire(this Basis basis) => basis switch
    {
        Basis.Native => "native",
        Basis.Rule => "rule",
        Basis.Ai => "ai",
        _ => throw new ArgumentOutOfRangeException(nameof(basis))
    };

    public static Basis ParseBasis(string value) => value switch
    {
        "native" => Basis.Native,
        "rule" => Basis.Rule,
        "ai" => Basis.Ai,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static Basis LeastCertain(Basis a, Basis b)
    {
        static int Rank(Basis x) => x switch { Basis.Native => 0, Basis.Rule => 1, Basis.Ai => 2, _ => 3 };
        return Rank(a) >= Rank(b) ? a : b;
    }
}

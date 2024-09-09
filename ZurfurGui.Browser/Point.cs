using static ZurfurGui.Helpers;

namespace ZurfurGui;

public class Point
{
    public double X { get; }
    public double Y { get; }
    public Point(double width, double height) { X = width; Y = height; }
    public bool Equals(Point v) => X == v.X && Y == v.Y;
    public override bool Equals(object? obj) => obj is Point v && Equals(v);
    public static bool operator ==(Point a, Point b) => a.Equals(b);
    public static bool operator !=(Point a, Point b) => !a.Equals(b);
    public override string ToString() => FormattableString.Invariant($"{X},{Y}");
    public override int GetHashCode()
    {
        var h = X.GetHashCode();
        h += HashMix(h) + Y.GetHashCode();
        return HashMix(h);
    }
}

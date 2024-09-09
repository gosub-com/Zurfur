using static ZurfurGui.Helpers;

namespace ZurfurGui;

public readonly struct Size : IEquatable<Size>
{
    public double Width { get; }
    public double Height { get; }
    public Size(double width, double height) { Width = width; Height = height; }
    public bool Equals(Size v) => Width == v.Width && Height == v.Height;
    public override bool Equals(object? obj) => obj is Size v && Equals(v);
    public static bool operator ==(Size a, Size b) => a.Equals(b);
    public static bool operator !=(Size a, Size b) => !a.Equals(b);
    public override string ToString() => FormattableString.Invariant($"{Width},{Height}");
    public override int GetHashCode()
    {
        var h = Width.GetHashCode();
        h += HashMix(h) + Height.GetHashCode();
        return HashMix(h);
    }
}

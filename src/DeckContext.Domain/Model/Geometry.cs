namespace DeckContext.Domain.Model;

public sealed record NativeGeometry(
    long X,
    long Y,
    long Width,
    long Height);

public sealed record NormalizedGeometry(
    double X,
    double Y,
    double Width,
    double Height);

namespace DeckContext.Domain.Model;

public enum GeometryCoordinateSpace
{
    Slide,
    ParentGroup,
}

public sealed record NativeGeometry(
    long X,
    long Y,
    long Width,
    long Height,
    GeometryCoordinateSpace CoordinateSpace);

public sealed record NormalizedGeometry(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record GroupTransformContext(
    long ChildOffsetX,
    long ChildOffsetY,
    long ChildExtentWidth,
    long ChildExtentHeight,
    long? RotationUnits,
    double? RotationDegrees,
    bool? FlipHorizontal,
    bool? FlipVertical);

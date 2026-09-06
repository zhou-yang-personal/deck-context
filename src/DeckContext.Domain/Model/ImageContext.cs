using DeckContext.Domain.Extraction;

namespace DeckContext.Domain.Model;

public enum ImageContentInterpretationStatus
{
    NotConfigured,
    Succeeded,
    Failed,
}

public enum ImageTextQuality
{
    Unknown,
    None,
    Low,
    Medium,
    High,
}

public sealed record ImageTextAssessmentContext(
    double? MeanConfidence,
    ImageTextQuality Quality,
    bool IncludeInMarkdown,
    string? Reason,
    bool WasTruncated);

public sealed record ImageCropContext(
    int LeftRaw,
    int TopRaw,
    int RightRaw,
    int BottomRaw,
    double LeftFraction,
    double TopFraction,
    double RightFraction,
    double BottomFraction);

public sealed record ImageTransformContext(
    long? RotationUnits,
    double? RotationDegrees,
    bool? FlipHorizontal,
    bool? FlipVertical);

public sealed record ImageContentInterpretationContext(
    ImageContentInterpretationStatus Status,
    string? ProviderId,
    string? Text,
    string? Description,
    ImageTextAssessmentContext? TextAssessment = null);

public sealed record ImageContext(
    string? RelationshipId,
    string? PartUri,
    string? ExternalUri,
    string? ContentType,
    string? FileExtension,
    string? SuggestedFileName,
    long? SizeBytes,
    string? Sha256,
    string? AlternativeText,
    string? Title,
    ImageCropContext? Crop,
    ImageTransformContext Transform,
    ImageContentInterpretationContext Interpretation,
    ExtractionStatus Status);

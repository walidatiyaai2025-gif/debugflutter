namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum ProductFlavorDetectionStatus
{
    Succeeded = 0,
    NoFlavors,
    Partial,
    GradleDslUnavailable,
    AppBuildScriptUnavailable,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    Ambiguous
}

public enum ProductFlavorField
{
    Dimension = 0,
    ApplicationId,
    ApplicationIdSuffix,
    VersionNameSuffix
}

public enum ProductFlavorValueSourceKind
{
    StaticGradle = 0,
    InferredSingleDimension
}

public sealed record ProductFlavorValue(
    ProductFlavorField Field,
    ProductFlavorValueSourceKind SourceKind,
    string Value,
    string ScriptPath);

public sealed record ProductFlavorEvidence(
    string FlavorName,
    ProductFlavorField Field,
    ProductFlavorValueSourceKind SourceKind,
    string Value,
    string ScriptPath);

public sealed record AndroidProductFlavor(
    string Name,
    ProductFlavorValue? Dimension,
    ProductFlavorValue? ApplicationId,
    ProductFlavorValue? ApplicationIdSuffix,
    ProductFlavorValue? VersionNameSuffix,
    IReadOnlyList<ProductFlavorField> UnresolvedFields,
    string ScriptPath);

public sealed record ProductFlavorDetectionResult(
    ProductFlavorDetectionStatus Status,
    GradleDslDetectionResult GradleDsl,
    IReadOnlyList<string> DeclaredDimensions,
    IReadOnlyList<AndroidProductFlavor> Flavors,
    IReadOnlyList<ProductFlavorEvidence> Evidence,
    int UnresolvedFlavorDeclarations,
    bool HasUnresolvedDimensionDeclarations,
    string Message)
{
    public bool IsSuccess => Status is
        ProductFlavorDetectionStatus.Succeeded or
        ProductFlavorDetectionStatus.NoFlavors or
        ProductFlavorDetectionStatus.Partial;
}

public interface IProductFlavorDetector
{
    ProductFlavorDetectionResult Detect(GradleDslDetectionResult gradleDsl);
}

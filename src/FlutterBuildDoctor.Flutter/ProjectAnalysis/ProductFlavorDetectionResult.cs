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

public sealed record ProductFlavorDetectionResult
{
    public ProductFlavorDetectionResult(
        ProductFlavorDetectionStatus status,
        GradleDslDetectionResult gradleDsl,
        IReadOnlyList<string> declaredDimensions,
        IReadOnlyList<AndroidProductFlavor> flavors,
        IReadOnlyList<ProductFlavorEvidence> evidence,
        int unresolvedFlavorDeclarations,
        bool hasUnresolvedDimensionDeclarations,
        string message)
    {
        ArgumentNullException.ThrowIfNull(gradleDsl);
        ArgumentNullException.ThrowIfNull(declaredDimensions);
        ArgumentNullException.ThrowIfNull(flavors);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(message);

        DeclaredDimensions = declaredDimensions;
        Evidence = evidence;
        UnresolvedFlavorDeclarations = unresolvedFlavorDeclarations;
        HasUnresolvedDimensionDeclarations = hasUnresolvedDimensionDeclarations;
        Message = message;
        GradleDsl = gradleDsl;

        var dimensionCannotBeInferred = declaredDimensions.Count > 1 || hasUnresolvedDimensionDeclarations;
        if (!dimensionCannotBeInferred || flavors.Count == 0)
        {
            Flavors = flavors;
            Status = status;
            return;
        }

        var normalized = new List<AndroidProductFlavor>(flavors.Count);
        var addedUnresolvedDimension = false;

        foreach (var flavor in flavors)
        {
            if (flavor.Dimension is not null ||
                flavor.UnresolvedFields.Contains(ProductFlavorField.Dimension))
            {
                normalized.Add(flavor);
                continue;
            }

            var unresolved = flavor.UnresolvedFields
                .Append(ProductFlavorField.Dimension)
                .Distinct()
                .OrderBy(field => field)
                .ToArray();

            normalized.Add(flavor with { UnresolvedFields = unresolved });
            addedUnresolvedDimension = true;
        }

        Flavors = normalized;
        Status = addedUnresolvedDimension && status == ProductFlavorDetectionStatus.Succeeded
            ? ProductFlavorDetectionStatus.Partial
            : status;
    }

    public ProductFlavorDetectionStatus Status { get; }
    public GradleDslDetectionResult GradleDsl { get; }
    public IReadOnlyList<string> DeclaredDimensions { get; }
    public IReadOnlyList<AndroidProductFlavor> Flavors { get; }
    public IReadOnlyList<ProductFlavorEvidence> Evidence { get; }
    public int UnresolvedFlavorDeclarations { get; }
    public bool HasUnresolvedDimensionDeclarations { get; }
    public string Message { get; }

    public bool IsSuccess => Status is
        ProductFlavorDetectionStatus.Succeeded or
        ProductFlavorDetectionStatus.NoFlavors or
        ProductFlavorDetectionStatus.Partial;
}

public interface IProductFlavorDetector
{
    ProductFlavorDetectionResult Detect(GradleDslDetectionResult gradleDsl);
}

using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class LocalPropertiesDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-local-properties-" + Guid.NewGuid().ToString("N"));

    public LocalPropertiesDetectorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "android"));
    }

    [Fact]
    public void Detect_ValidSdkPaths_ReturnsSucceeded()
    {
        var androidSdk = CreateAndroidSdk("android sdk");
        var flutterSdk = CreateFlutterSdk("flutter sdk");
        WriteLocalProperties($"""
            sdk.dir={Escape(androidSdk)}
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Succeeded, result.Status);
        Assert.True(result.AndroidSdk.IsValid);
        Assert.True(result.FlutterSdk.IsValid);
        Assert.Equal(Path.GetFullPath(androidSdk), result.AndroidSdk.NormalizedPath);
        Assert.Equal(Path.GetFullPath(flutterSdk), result.FlutterSdk.NormalizedPath);
        Assert.True(result.AndroidSdk.HasExpectedLayout);
        Assert.True(result.FlutterSdk.HasExpectedLayout);
    }

    [Fact]
    public void Detect_JavaPropertiesEscapedColonAndBackslashes_AreDecoded()
    {
        var androidSdk = CreateAndroidSdk("android-colon");
        var flutterSdk = CreateFlutterSdk("flutter-colon");
        WriteLocalProperties($"""
            sdk.dir={Escape(androidSdk, escapeColon: true)}
            flutter.sdk:{Escape(flutterSdk, escapeColon: true)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Succeeded, result.Status);
        Assert.Equal(Path.GetFullPath(androidSdk), result.AndroidSdk.NormalizedPath);
        Assert.Equal(Path.GetFullPath(flutterSdk), result.FlutterSdk.NormalizedPath);
    }

    [Fact]
    public void Detect_UnicodeEscapeInRelevantValue_IsDecodedBeforePathValidation()
    {
        var androidSdk = CreateAndroidSdk("android sdk unicode");
        var flutterSdk = CreateFlutterSdk("flutter sdk unicode");
        var androidEncoded = Escape(androidSdk).Replace(" ", "\\u0020", StringComparison.Ordinal);
        var flutterEncoded = Escape(flutterSdk).Replace(" ", "\\u0020", StringComparison.Ordinal);
        WriteLocalProperties($"""
            sdk.dir={androidEncoded}
            flutter.sdk={flutterEncoded}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Succeeded, result.Status);
        Assert.Equal(Path.GetFullPath(androidSdk), result.AndroidSdk.NormalizedPath);
        Assert.Equal(Path.GetFullPath(flutterSdk), result.FlutterSdk.NormalizedPath);
    }

    [Fact]
    public void Detect_ContinuationLine_IsCombinedUsingJavaPropertiesRules()
    {
        var androidSdk = CreateAndroidSdk("androidsdk");
        var flutterSdk = CreateFlutterSdk("fluttersdk");
        var androidForward = androidSdk.Replace('\\', '/');
        var split = androidForward.Length - 3;
        var first = androidForward[..split];
        var second = androidForward[split..];

        WriteLocalProperties($"""
            sdk.dir={first}\
              {second}
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Succeeded, result.Status);
        Assert.Equal(Path.GetFullPath(androidSdk), result.AndroidSdk.NormalizedPath);
    }

    [Fact]
    public void Detect_MissingRelevantKey_ReturnsPartialWithTypedMissingStatus()
    {
        var androidSdk = CreateAndroidSdk("android-only");
        WriteLocalProperties($"sdk.dir={Escape(androidSdk)}");

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Partial, result.Status);
        Assert.True(result.AndroidSdk.IsValid);
        Assert.Equal(LocalPropertiesPathStatus.MissingKey, result.FlutterSdk.Status);
    }

    [Fact]
    public void Detect_EmptyRelevantValue_IsTypedExplicitly()
    {
        var flutterSdk = CreateFlutterSdk("flutter-empty-peer");
        WriteLocalProperties($"""
            sdk.dir=
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Partial, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.EmptyValue, result.AndroidSdk.Status);
        Assert.True(result.FlutterSdk.IsValid);
    }

    [Fact]
    public void Detect_NonexistentConfiguredPath_IsTypedExplicitly()
    {
        var missing = Path.Combine(_root, "missing-sdk");
        var flutterSdk = CreateFlutterSdk("flutter-existing");
        WriteLocalProperties($"""
            sdk.dir={Escape(missing)}
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Partial, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.DirectoryMissing, result.AndroidSdk.Status);
        Assert.Equal(Path.GetFullPath(missing), result.AndroidSdk.NormalizedPath);
    }

    [Fact]
    public void Detect_ExistingDirectoryWithoutSdkLayout_IsUnrecognized()
    {
        var notSdk = Path.Combine(_root, "not-sdk");
        Directory.CreateDirectory(notSdk);
        var flutterSdk = CreateFlutterSdk("flutter-layout-peer");
        WriteLocalProperties($"""
            sdk.dir={Escape(notSdk)}
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Partial, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.UnrecognizedLayout, result.AndroidSdk.Status);
        Assert.True(result.AndroidSdk.Exists);
        Assert.False(result.AndroidSdk.HasExpectedLayout);
    }

    [Fact]
    public void Detect_RelativePath_IsInvalidInsteadOfResolvedImplicitly()
    {
        var flutterSdk = CreateFlutterSdk("flutter-relative-peer");
        WriteLocalProperties($"""
            sdk.dir=../Android/sdk
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Partial, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.InvalidPath, result.AndroidSdk.Status);
        Assert.Null(result.AndroidSdk.NormalizedPath);
    }

    [Fact]
    public void Detect_RepeatedIdenticalRelevantValues_AreAcceptedWithOccurrenceEvidence()
    {
        var androidSdk = CreateAndroidSdk("android-duplicate");
        var flutterSdk = CreateFlutterSdk("flutter-duplicate");
        WriteLocalProperties($"""
            sdk.dir={Escape(androidSdk)}
            sdk.dir={Escape(androidSdk)}
            flutter.sdk={Escape(flutterSdk)}
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Succeeded, result.Status);
        Assert.Equal(2, result.AndroidSdk.OccurrenceCount);
        Assert.Equal(2, result.FlutterSdk.OccurrenceCount);
        Assert.Equal(2, result.AndroidSdk.Evidence.Count);
        Assert.Equal(2, result.FlutterSdk.Evidence.Count);
    }

    [Fact]
    public void Detect_ConflictingRelevantValues_ReturnsAmbiguousWithoutSelectingOne()
    {
        var first = CreateAndroidSdk("android-one");
        var second = CreateAndroidSdk("android-two");
        var flutterSdk = CreateFlutterSdk("flutter-conflict-peer");
        WriteLocalProperties($"""
            sdk.dir={Escape(first)}
            sdk.dir={Escape(second)}
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Ambiguous, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.Ambiguous, result.AndroidSdk.Status);
        Assert.Null(result.AndroidSdk.ConfiguredValue);
        Assert.Null(result.AndroidSdk.NormalizedPath);
        Assert.Equal(2, result.AndroidSdk.Evidence.Count);
    }

    [Fact]
    public void Detect_MalformedRelevantUnicodeEscape_IsInvalidWithoutRawValueExposure()
    {
        var flutterSdk = CreateFlutterSdk("flutter-malformed-peer");
        WriteLocalProperties($"""
            sdk.dir=\u12
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();

        Assert.Equal(LocalPropertiesDetectionStatus.Partial, result.Status);
        Assert.Equal(LocalPropertiesPathStatus.InvalidPath, result.AndroidSdk.Status);
        Assert.Null(result.AndroidSdk.ConfiguredValue);
        Assert.Empty(result.AndroidSdk.Evidence);
    }

    [Fact]
    public void Detect_UnrelatedSecretsAreIgnoredAndNeverReturned()
    {
        var androidSdk = CreateAndroidSdk("android-secret-test");
        var flutterSdk = CreateFlutterSdk("flutter-secret-test");
        const string secret = "SUPER-SECRET-LOCAL-PASSWORD";
        WriteLocalProperties($"""
            store.password={secret}
            api.token={secret}
            sdk.dir={Escape(androidSdk)}
            flutter.sdk={Escape(flutterSdk)}
            """);

        var result = Detect();
        var visible = string.Join(
            "|",
            result.Message,
            result.AndroidSdk.Message,
            result.FlutterSdk.Message,
            string.Join("|", result.AndroidSdk.Evidence.Select(item => item.DecodedValue)),
            string.Join("|", result.FlutterSdk.Evidence.Select(item => item.DecodedValue)));

        Assert.DoesNotContain(secret, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(LocalPropertiesDetectionResult).GetProperties(),
            property =>
                property.Name.Equals("RawContent", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Properties", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("AllProperties", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("UnknownProperties", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private LocalPropertiesDetectionResult Detect()
        => new LocalPropertiesDetector().Detect(SuccessfulRoot());

    private string CreateAndroidSdk(string name)
    {
        var root = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(root, "platform-tools"));
        return root;
    }

    private string CreateFlutterSdk(string name)
    {
        var root = Path.Combine(_root, name);
        var bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "flutter.bat"), "@echo off");
        File.WriteAllText(Path.Combine(bin, "flutter"), "#!/bin/sh");
        return root;
    }

    private void WriteLocalProperties(string content)
        => File.WriteAllText(Path.Combine(_root, "android", "local.properties"), content);

    private static string Escape(string value, bool escapeColon = false)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal);
        return escapeColon
            ? escaped.Replace(":", "\\:", StringComparison.Ordinal)
            : escaped;
    }

    private FlutterProjectRootResult SuccessfulRoot()
        => new(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            Path.Combine(_root, "pubspec.yaml"),
            Array.Empty<FlutterProjectCandidate>(),
            new[] { Path.Combine(_root, "pubspec.yaml") },
            "Test root.");
}

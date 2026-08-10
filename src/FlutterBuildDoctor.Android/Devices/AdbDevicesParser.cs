namespace FlutterBuildDoctor.Android.Devices;

public sealed class AdbDevicesParser : IAdbDevicesParser
{
    public IReadOnlyList<AndroidDeviceRecord> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<AndroidDeviceRecord>();

        var devices = new List<AndroidDeviceRecord>();
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
            {
                continue;
            }

            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 2)
                continue;

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 2; index < tokens.Length; index++)
            {
                var separator = tokens[index].IndexOf(':');
                if (separator <= 0 || separator == tokens[index].Length - 1)
                    continue;
                properties[tokens[index][..separator]] = tokens[index][(separator + 1)..];
            }

            devices.Add(new AndroidDeviceRecord(
                tokens[0],
                ParseState(tokens[1]),
                Get(properties, "product"),
                Get(properties, "model"),
                Get(properties, "device"),
                Get(properties, "transport_id"),
                properties,
                rawLine));
        }

        return devices;
    }

    private static AndroidDeviceState ParseState(string value)
        => value.ToLowerInvariant() switch
        {
            "device" => AndroidDeviceState.Online,
            "offline" => AndroidDeviceState.Offline,
            "unauthorized" => AndroidDeviceState.Unauthorized,
            "recovery" => AndroidDeviceState.Recovery,
            "bootloader" => AndroidDeviceState.Bootloader,
            _ => AndroidDeviceState.Unknown
        };

    private static string? Get(IReadOnlyDictionary<string, string> properties, string key)
        => properties.TryGetValue(key, out var value) ? value : null;
}

public sealed class AndroidDeviceMetadataProjector : IAndroidDeviceMetadataProjector
{
    public AndroidDeviceMetadata Project(AndroidDeviceRecord device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var displayName = string.IsNullOrWhiteSpace(device.Model)
            ? device.Serial
            : device.Model.Replace('_', ' ');
        var isEmulator = device.Serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) ||
                         (device.Product?.Contains("sdk_gphone", StringComparison.OrdinalIgnoreCase) ?? false) ||
                         (device.Device?.Contains("emulator", StringComparison.OrdinalIgnoreCase) ?? false);

        return new AndroidDeviceMetadata(
            device.Serial,
            displayName,
            device.State,
            isEmulator,
            device.Product,
            device.Device,
            device.TransportId);
    }
}

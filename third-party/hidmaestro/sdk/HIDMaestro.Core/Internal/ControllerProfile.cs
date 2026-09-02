namespace HIDMaestro.Internal;

/// <summary>Internal data passed through the plain ROOT\HIDClass deployment path.</summary>
internal sealed class ControllerProfile
{
    public string Id { get; init; } = "generic-mouse";
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public string ProductString { get; init; } = "HIDMaestro Virtual Mouse";
    public string? ManufacturerString { get; init; }
    public byte[]? Descriptor { get; init; }
    public int InputReportSize { get; init; }
    public int ButtonCount { get; init; }

    public ushort VendorId => Vid;
    public ushort ProductId => Pid;
    public string DisplayName => ProductString;
    public bool HasDescriptor => Descriptor is { Length: > 0 };
    public byte[]? GetDescriptorBytes() => Descriptor;
}

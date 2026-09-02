using HIDMaestro.Internal;

namespace HIDMaestro;

/// <summary>Immutable configuration for a virtual HID mouse.</summary>
public sealed class HMProfile
{
    internal ControllerProfile Inner { get; }

    internal HMProfile(ControllerProfile inner) => Inner = inner;

    public string Id => Inner.Id;
    public bool IsDeployable => Inner.HasDescriptor;
    public int ButtonCount => Inner.ButtonCount;
}

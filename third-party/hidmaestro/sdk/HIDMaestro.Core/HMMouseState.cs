using System;
using System.Buffers.Binary;

namespace HIDMaestro;

/// <summary>Buttons exposed by a generic HID mouse.</summary>
[Flags]
public enum HMMouseButton : byte
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Middle = 1 << 2,
    Back = 1 << 3,
    Forward = 1 << 4,
    Button6 = 1 << 5,
    Button7 = 1 << 6,
    Button8 = 1 << 7,
}

/// <summary>One relative input frame for a profile created by
/// <see cref="HMProfileBuilder.GenericMouse"/>.</summary>
public struct HMMouseState
{
    internal const byte ReportId = 1;
    internal const int ReportSize = 8;

    /// <summary>Buttons held during this frame.</summary>
    public HMMouseButton Buttons;

    /// <summary>Horizontal movement since the previous frame.</summary>
    public short DeltaX;

    /// <summary>Vertical movement since the previous frame.</summary>
    public short DeltaY;

    /// <summary>Vertical wheel detents since the previous frame, from -127 to 127.</summary>
    public sbyte Wheel;

    /// <summary>Horizontal wheel detents since the previous frame, from -127 to 127.</summary>
    public sbyte HorizontalWheel;

    internal readonly void WriteReport(Span<byte> report, int buttonCount)
    {
        HMMouseReport.Validate(report, buttonCount);
        report[0] = ReportId;
        report[1] = HMMouseReport.Buttons(Buttons, buttonCount);
        BinaryPrimitives.WriteInt16LittleEndian(report[2..], DeltaX);
        BinaryPrimitives.WriteInt16LittleEndian(report[4..], DeltaY);
        report[6] = HMMouseReport.Wheel(Wheel);
        report[7] = HMMouseReport.Wheel(HorizontalWheel);
    }
}

/// <summary>One absolute input frame for a profile created by
/// <see cref="HMProfileBuilder.GenericMouse"/>.</summary>
public struct HMAbsoluteMouseState
{
    internal const byte ReportId = 2;
    internal const int ReportSize = 8;

    /// <summary>Buttons held during this frame.</summary>
    public HMMouseButton Buttons;

    /// <summary>Horizontal position in the descriptor range 0 to 32767.</summary>
    public ushort X;

    /// <summary>Vertical position in the descriptor range 0 to 32767.</summary>
    public ushort Y;

    internal readonly void WriteReport(Span<byte> report, int buttonCount)
    {
        HMMouseReport.Validate(report, buttonCount);
        report.Clear();
        report[0] = ReportId;
        report[1] = HMMouseReport.Buttons(Buttons, buttonCount);
        BinaryPrimitives.WriteUInt16LittleEndian(report[2..], Math.Min(X, (ushort)32767));
        BinaryPrimitives.WriteUInt16LittleEndian(report[4..], Math.Min(Y, (ushort)32767));
    }
}

internal static class HMMouseReport
{
    internal static void Validate(Span<byte> report, int buttonCount)
    {
        if (report.Length < HMMouseState.ReportSize)
            throw new ArgumentException("A generic mouse report requires eight bytes.", nameof(report));
        if (buttonCount < 1 || buttonCount > 8)
            throw new ArgumentOutOfRangeException(nameof(buttonCount),
                "A generic mouse must expose between 1 and 8 buttons.");
    }

    internal static byte Buttons(HMMouseButton buttons, int buttonCount)
    {
        byte mask = buttonCount == 8 ? byte.MaxValue : (byte)((1 << buttonCount) - 1);
        return (byte)((byte)buttons & mask);
    }

    internal static byte Wheel(sbyte value) =>
        unchecked((byte)(value == sbyte.MinValue ? -127 : value));
}

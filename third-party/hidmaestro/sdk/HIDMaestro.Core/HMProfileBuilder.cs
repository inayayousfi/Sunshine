using System;
using System.Collections.Generic;
using HIDMaestro.Internal;

namespace HIDMaestro;

/// <summary>Builds the generic mouse profile used by Sunshine.</summary>
public sealed class HMProfileBuilder
{
    private readonly int _buttonCount;
    private ushort _vid;
    private ushort _pid;
    private string _productString = "HIDMaestro Virtual Mouse";
    private string? _manufacturerString;

    private HMProfileBuilder(int buttonCount) => _buttonCount = buttonCount;

    /// <summary>Creates a standard HID mouse profile with relative and absolute reports.</summary>
    public static HMProfileBuilder GenericMouse(int buttonCount = 5)
    {
        if (buttonCount < 1 || buttonCount > 8)
            throw new ArgumentOutOfRangeException(nameof(buttonCount),
                "A generic mouse must expose between 1 and 8 buttons.");
        return new HMProfileBuilder(buttonCount);
    }

    public HMProfileBuilder Vid(ushort vid) { _vid = vid; return this; }
    public HMProfileBuilder Pid(ushort pid) { _pid = pid; return this; }
    public HMProfileBuilder ProductString(string value) { _productString = value; return this; }
    public HMProfileBuilder ManufacturerString(string value) { _manufacturerString = value; return this; }

    /// <summary>Builds a deployable generic mouse profile.</summary>
    public HMProfile Build()
    {
        if (_vid == 0) throw new InvalidOperationException("VID must be set (use .Vid(0x045E)).");
        if (_pid == 0) throw new InvalidOperationException("PID must be set (use .Pid(0x028E)).");

        return new HMProfile(new ControllerProfile
        {
            Id = "generic-mouse",
            Vid = _vid,
            Pid = _pid,
            ProductString = _productString,
            ManufacturerString = _manufacturerString,
            Descriptor = BuildDescriptor(_buttonCount),
            InputReportSize = HMMouseState.ReportSize,
            ButtonCount = _buttonCount,
        });
    }

    private static byte[] BuildDescriptor(int buttonCount)
    {
        var descriptor = new List<byte>();
        AddMouseReport(descriptor, HMMouseState.ReportId, buttonCount, absolute: false);
        AddMouseReport(descriptor, HMAbsoluteMouseState.ReportId, buttonCount, absolute: true);
        return descriptor.ToArray();
    }

    private static void AddMouseReport(List<byte> descriptor, byte reportId, int buttonCount, bool absolute)
    {
        descriptor.AddRange([0x05, 0x01, 0x09, 0x02, 0xA1, 0x01, 0x09, 0x01, 0xA1, 0x00, 0x85, reportId]);
        descriptor.AddRange([0x05, 0x09, 0x19, 0x01, 0x29, (byte)buttonCount, 0x15, 0x00,
            0x25, 0x01, 0x95, (byte)buttonCount, 0x75, 0x01, 0x81, 0x02]);
        if (buttonCount < 8)
            descriptor.AddRange([0x95, (byte)(8 - buttonCount), 0x75, 0x01, 0x81, 0x03]);

        if (absolute)
        {
            descriptor.AddRange([0x05, 0x01, 0x09, 0x30, 0x09, 0x31, 0x15, 0x00,
                0x26, 0xFF, 0x7F, 0x75, 0x10, 0x95, 0x02, 0x81, 0x02,
                0x75, 0x08, 0x95, 0x02, 0x81, 0x03]);
        }
        else
        {
            descriptor.AddRange([0x05, 0x01, 0x09, 0x30, 0x09, 0x31, 0x16, 0x00, 0x80,
                0x26, 0xFF, 0x7F, 0x75, 0x10, 0x95, 0x02, 0x81, 0x06,
                0x09, 0x38, 0x15, 0x81, 0x25, 0x7F, 0x75, 0x08, 0x95, 0x01, 0x81, 0x06,
                0x05, 0x0C, 0x0A, 0x38, 0x02, 0x15, 0x81, 0x25, 0x7F,
                0x75, 0x08, 0x95, 0x01, 0x81, 0x06]);
        }
        descriptor.AddRange([0xC0, 0xC0]);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HIDMaestro.Internal;

/// <summary>Applies the mouse display name to its root node and HID child.</summary>
internal static class DeviceProperties
{
    private static DEVPROPKEY DeviceDescription = new()
    {
        fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20,
            0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        pid = 2,
    };
    private static DEVPROPKEY FriendlyName = new()
    {
        fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20,
            0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        pid = 14,
    };
    private static DEVPROPKEY BusReportedDescription = new()
    {
        fmtid = new Guid(0x540b947e, 0x8b40, 0x45bc, 0xa8, 0xa2,
            0x6a, 0x0b, 0x89, 0x4c, 0xbd, 0xa2),
        pid = 4,
    };
    private const uint DevpropTypeString = 0x12;

    public static bool SetAllNamingProperties(string rootInstanceId, string name)
    {
        if (CM_Locate_DevNodeW(out uint root, rootInstanceId, 0) != 0)
            return false;

        byte[] value = Encoding.Unicode.GetBytes(name + "\0");
        Apply(root, value);
        if (CM_Get_Child(out uint child, root, 0) == 0)
            Apply(child, value);
        return true;
    }

    private static void Apply(uint device, byte[] value)
    {
        uint size = (uint)value.Length;
        CM_Set_DevNode_PropertyW(device, ref BusReportedDescription,
            DevpropTypeString, value, size, 0);
        CM_Set_DevNode_PropertyW(device, ref FriendlyName,
            DevpropTypeString, value, size, 0);
        CM_Set_DevNode_PropertyW(device, ref DeviceDescription,
            DevpropTypeString, value, size, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(
        out uint device, string instanceId, uint flags);
    [DllImport("CfgMgr32.dll")]
    private static extern uint CM_Get_Child(out uint child, uint device, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Set_DevNode_PropertyW(
        uint device, ref DEVPROPKEY propertyKey, uint propertyType,
        byte[] buffer, uint bufferSize, uint flags);
}

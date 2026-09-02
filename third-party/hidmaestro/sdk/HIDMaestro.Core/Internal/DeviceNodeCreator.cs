using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace HIDMaestro.Internal;

/// <summary>Creates the proven plain ROOT\HIDClass UMDF2 device node.</summary>
internal static class DeviceNodeCreator
{
    private static readonly System.Collections.Generic.HashSet<string> DeviceOverrides =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DeviceOverridesLock = new();
    private static readonly Guid HidClassGuid =
        new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
    private static DEVPROPKEY BusTypeGuidKey = new()
    {
        fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20,
            0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        pid = 21,
    };

    public readonly struct Result
    {
        public bool Success { get; }
        public string? InstanceId { get; }
        public Result(bool success, string? instanceId)
        {
            Success = success;
            InstanceId = instanceId;
        }
    }

    public static Result CreateDeviceNode(
        ControllerProfile profile, string infPath, int controllerIndex)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string hardwareId = $"root\\VID_{profile.VendorId:X4}&PID_{profile.ProductId:X4}";
        string hardwareIds = $"{hardwareId}\0root\\HIDMaestro\0\0";

        Guid classGuid = HidClassGuid;
        IntPtr deviceInfoSet = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (deviceInfoSet == new IntPtr(-1)) return new Result(false, null);

        try
        {
            int deviceInfoSize = IntPtr.Size == 8 ? 32 : 28;
            byte[] deviceInfo = new byte[deviceInfoSize];
            BitConverter.GetBytes(deviceInfoSize).CopyTo(deviceInfo, 0);
            GCHandle pinned = GCHandle.Alloc(deviceInfo, GCHandleType.Pinned);
            try
            {
                IntPtr pointer = pinned.AddrOfPinnedObject();
                if (!SetupDiCreateDeviceInfoW(deviceInfoSet, "HIDClass", ref classGuid,
                        profile.ProductString, IntPtr.Zero, 1, pointer))
                    return new Result(false, null);

                byte[] hardwareIdBytes = Encoding.Unicode.GetBytes(hardwareIds);
                if (!SetupDiSetDeviceRegistryPropertyW(deviceInfoSet, pointer, 1,
                        hardwareIdBytes, (uint)hardwareIdBytes.Length))
                    return new Result(false, null);
                EnsureRemovableOverride(hardwareId);
                if (!SetupDiCallClassInstaller(0x19, deviceInfoSet, pointer))
                    return new Result(false, null);
            }
            finally
            {
                pinned.Free();
            }

            string? instanceId = ClaimNewInstance(controllerIndex);
            UpdateDriverForPlugAndPlayDevicesW(
                IntPtr.Zero, hardwareId, infPath, 0, out _);

            if (instanceId != null)
            {
                DeviceManager.WaitForHidChild(instanceId);
                SetUsbBusType(instanceId);
                try { DeviceProperties.SetAllNamingProperties(instanceId, profile.DisplayName); } catch { }
            }
            return new Result(true, instanceId);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static void EnsureRemovableOverride(string hardwareId)
    {
        lock (DeviceOverridesLock)
        {
            if (!DeviceOverrides.Add(hardwareId)) return;
        }
        try
        {
            string path = $@"SYSTEM\CurrentControlSet\Control\DeviceOverrides\{hardwareId.Replace('\\', '#')}\*";
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(path);
            key.SetValue("Removable", 1, RegistryValueKind.DWord);
        }
        catch
        {
            lock (DeviceOverridesLock) DeviceOverrides.Remove(hardwareId);
        }
    }

    private static string? ClaimNewInstance(int controllerIndex)
    {
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\ROOT\HIDClass");
            if (root == null) return null;
            foreach (string instance in root.GetSubKeyNames())
            {
                string instanceId = $@"ROOT\HIDClass\{instance}";
                if (CM_Locate_DevNodeW(out _, instanceId, 0) != 0) continue;
                if (!DeviceManager.IsHidMaestroOwned(instanceId)) continue;

                using RegistryKey parameters = Registry.LocalMachine.CreateSubKey(
                    $@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters");
                if (parameters.GetValue("ControllerIndex") != null) continue;
                parameters.SetValue("ControllerIndex", controllerIndex, RegistryValueKind.DWord);
                return instanceId;
            }
        }
        catch { }
        return null;
    }

    private static void SetUsbBusType(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint device, instanceId, 0) != 0) return;
        byte[] usbGuid = new Guid("9d7debbc-c85d-11d1-9eb4-006008c3a19a").ToByteArray();
        CM_Set_DevNode_PropertyW(device, ref BusTypeGuidKey, 0x0D,
            usbGuid, (uint)usbGuid.Length, 0);
        if (CM_Get_Child(out uint child, device, 0) == 0)
            CM_Set_DevNode_PropertyW(child, ref BusTypeGuidKey, 0x0D,
                usbGuid, (uint)usbGuid.Length, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("SetupAPI.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr parent);
    [DllImport("SetupAPI.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiCreateDeviceInfoW")]
    private static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string name,
        ref Guid classGuid, string description, IntPtr parent, int flags, IntPtr data);
    [DllImport("SetupAPI.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiSetDeviceRegistryPropertyW")]
    private static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, IntPtr data,
        int property, byte[] buffer, uint size);
    [DllImport("SetupAPI.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(int function, IntPtr set, IntPtr data);
    [DllImport("SetupAPI.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr parent,
        string hardwareId, string infPath, int flags, out bool rebootRequired);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint device, string instanceId, uint flags);
    [DllImport("CfgMgr32.dll")]
    private static extern uint CM_Get_Child(out uint child, uint device, uint flags);
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Set_DevNode_PropertyW(uint device, ref DEVPROPKEY propertyKey,
        uint propertyType, byte[] buffer, uint size, uint flags);
}

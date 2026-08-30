using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace HIDMaestro.Internal;

/// <summary>Coordinates plain ROOT\HIDClass mouse setup and cleanup.</summary>
internal static class DeviceOrchestrator
{
    private const string RegistryBase = @"SOFTWARE\HIDMaestro";
    private const uint EventModifyState = 0x0002;
    private static readonly object GatesLock = new();
    private static readonly Dictionary<int, ManualResetEventSlim> TeardownGates = new();
    private static readonly bool DiagnosticsEnabled =
        Environment.GetEnvironmentVariable("HIDMAESTRO_DIAG") == "1";
    private static readonly object DiagnosticsLock = new();
    private static StreamWriter? s_diagnostics;
    private static bool s_ghostsCleaned;

    private static string RegistryPath(int index) => $@"{RegistryBase}\Controller{index}";

    internal static void LogDiag(string message)
    {
        if (!DiagnosticsEnabled) return;
        lock (DiagnosticsLock)
        {
            try
            {
                if (s_diagnostics == null)
                {
                    string directory = Path.Combine(Path.GetTempPath(), "HIDMaestro");
                    Directory.CreateDirectory(directory);
                    var stream = new FileStream(Path.Combine(directory, "teardown_diag.log"),
                        FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    s_diagnostics = new StreamWriter(stream) { AutoFlush = true };
                }
                s_diagnostics.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] tid={Environment.CurrentManagedThreadId} {message}");
            }
            catch { }
        }
    }

    public static string? SetupController(
        int controllerIndex, ControllerProfile profile, string infPath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (controllerIndex < 0) throw new ArgumentOutOfRangeException(nameof(controllerIndex));
        if (!profile.HasDescriptor)
            throw new ArgumentException($"Profile '{profile.Id}' has no HID descriptor.", nameof(profile));

        WaitForPriorTeardown(controllerIndex);
        if (!s_ghostsCleaned)
        {
            SweepMouseDevices(removePackages: false, excludeIndex: null);
            try { DeviceManager.RemoveAccumulatedHmPhantoms(); } catch { }
            s_ghostsCleaned = true;
        }

        SharedMemoryIO.EnsureInputMapping(controllerIndex);
        WriteInstanceConfig(controllerIndex, profile);

        if (!DriverBuilder.IsDriverInstalled() && !DriverBuilder.FullDeploy())
            throw new InvalidOperationException(
                "Driver install failed. Run elevated and check pnputil output.");

        DeviceNodeCreator.Result result =
            DeviceNodeCreator.CreateDeviceNode(profile, infPath, controllerIndex);
        if (!result.Success || result.InstanceId == null)
            throw new InvalidOperationException(
                $"DeviceNodeCreator.CreateDeviceNode failed for profile '{profile.Id}' at index {controllerIndex}.");

        string? child = DeviceManager.GetHidChildId(result.InstanceId);
        if (child != null)
        {
            try
            {
                using RegistryKey parameters = Registry.LocalMachine.CreateSubKey(
                    $@"SYSTEM\CurrentControlSet\Enum\{child}\Device Parameters");
                parameters.SetValue("ControllerIndex", controllerIndex, RegistryValueKind.DWord);
            }
            catch { }
        }
        try { DeviceProperties.SetAllNamingProperties(result.InstanceId, profile.DisplayName); } catch { }
        return result.InstanceId;
    }

    private static void WriteInstanceConfig(int index, ControllerProfile profile)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(RegistryPath(index));
        key.SetValue("DeviceInstanceId",
            $@"ROOT\VID_{profile.VendorId:X4}&PID_{profile.ProductId:X4}&IG_00\{index:D4}",
            RegistryValueKind.String);
        key.SetValue("FunctionMode", 0, RegistryValueKind.DWord);
        key.SetValue("ReportDescriptor", profile.GetDescriptorBytes()!, RegistryValueKind.Binary);
        key.SetValue("VendorId", (int)profile.VendorId, RegistryValueKind.DWord);
        key.SetValue("ProductId", (int)profile.ProductId, RegistryValueKind.DWord);
        key.SetValue("VersionNumber", 0x0100, RegistryValueKind.DWord);
        key.SetValue("ProductString", profile.ProductString, RegistryValueKind.String);
        key.SetValue("InputReportByteLength", profile.InputReportSize, RegistryValueKind.DWord);
        key.SetValue("DeviceDescription", profile.DisplayName, RegistryValueKind.String);
    }

    public static void TeardownController(int controllerIndex, string? instanceId)
    {
        ManualResetEventSlim gate = GetGate(controllerIndex);
        gate.Reset();
        try
        {
            try { SharedMemoryIO.DestroyController(controllerIndex); } catch { }

            var children = new List<string>();
            if (!string.IsNullOrEmpty(instanceId))
            {
                try { children = DeviceManager.GetAllHidChildIds(instanceId); } catch { }
                try
                {
                    DeviceManager.RemoveDevice(instanceId, timeoutMs: 120_000,
                        forceFallbacks: true);
                }
                catch (Exception error)
                {
                    LogDiag($"parent removal failed for {instanceId}: {error.Message}");
                }
            }

            foreach (string child in children)
            {
                try
                {
                    DeviceManager.RemoveDevice(child, timeoutMs: 120_000,
                        fast: true, forceFallbacks: true);
                }
                catch { }
            }

            RemoveMouseInstances(controllerIndex);
            try { Registry.LocalMachine.DeleteSubKeyTree(RegistryPath(controllerIndex), false); } catch { }
            try { DeviceManager.RemoveOrphanHidChildren(); } catch { }
        }
        finally
        {
            gate.Set();
        }
    }

    internal static void RemoveAllVirtualControllers(bool preserveInstall)
    {
        bool sameDriver = preserveInstall && DriverBuilder.WillTakeFastPath();
        SweepMouseDevices(removePackages: !preserveInstall, excludeIndex: null);
        try { SharedMemoryIO.Cleanup(); } catch { }

        if (!sameDriver)
        {
            WaitForHidMaestroHosts();
            DrainOrphanedWudfHosts();
        }

        if (!preserveInstall)
        {
            try { Registry.LocalMachine.DeleteSubKeyTree(RegistryBase, false); } catch { }
        }
    }

    private static void SweepMouseDevices(bool removePackages, int? excludeIndex)
    {
        HashSet<int>? indices = CollectMouseIndices(excludeIndex);
        SignalStopEventsAndDrain(indices);

        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\ROOT\HIDClass");
            if (root != null)
            {
                foreach (string instance in root.GetSubKeyNames())
                {
                    string id = $@"ROOT\HIDClass\{instance}";
                    if (!DeviceManager.IsHidMaestroOwned(id)) continue;
                    if (excludeIndex.HasValue && ReadControllerIndex(root, instance) == excludeIndex)
                        continue;
                    try
                    {
                        DeviceManager.RemoveDevice(id, timeoutMs: 5000,
                            fast: true, forceFallbacks: true);
                    }
                    catch { }
                }
            }
        }
        catch { }

        try { DeviceManager.RemoveOrphanHidChildren(); } catch { }
        if (removePackages)
        {
            try { PnputilHelper.RemoveAllHidMaestroPackages(); } catch { }
        }
    }

    private static void RemoveMouseInstances(int controllerIndex)
    {
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\ROOT\HIDClass");
            if (root == null) return;
            foreach (string instance in root.GetSubKeyNames())
            {
                if (ReadControllerIndex(root, instance) != controllerIndex) continue;
                string id = $@"ROOT\HIDClass\{instance}";
                if (!DeviceManager.IsHidMaestroOwned(id)) continue;
                try
                {
                    DeviceManager.RemoveDevice(id, timeoutMs: 120_000,
                        forceFallbacks: true);
                }
                catch { }
            }
        }
        catch { }
    }

    private static int? ReadControllerIndex(RegistryKey root, string instance)
    {
        try
        {
            using RegistryKey? parameters = root.OpenSubKey($@"{instance}\Device Parameters");
            return parameters?.GetValue("ControllerIndex") is int index ? index : null;
        }
        catch { return null; }
    }

    private static HashSet<int>? CollectMouseIndices(int? excludeIndex)
    {
        var indices = new HashSet<int>();
        bool unindexed = false;
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\ROOT\HIDClass");
            if (root == null) return indices;
            foreach (string instance in root.GetSubKeyNames())
            {
                string id = $@"ROOT\HIDClass\{instance}";
                if (!DeviceManager.IsHidMaestroOwned(id)) continue;
                int? index = ReadControllerIndex(root, instance);
                if (!index.HasValue) unindexed = true;
                else if (index != excludeIndex) indices.Add(index.Value);
            }
        }
        catch { unindexed = true; }
        return unindexed ? null : indices;
    }

    private static void SignalStopEventsAndDrain(HashSet<int>? targets)
    {
        targets ??= CollectRegistryIndices();
        bool signaled = false;
        foreach (int index in targets)
        {
            IntPtr handle = OpenEventW(EventModifyState, false,
                $@"Global\HIDMaestroStopEvent{index}");
            if (handle == IntPtr.Zero) continue;
            SetEvent(handle);
            CloseHandle(handle);
            signaled = true;
        }
        if (signaled) Thread.Sleep(TimeoutScale.Apply(500));
    }

    private static HashSet<int> CollectRegistryIndices()
    {
        var indices = new HashSet<int>();
        for (int index = 0; index < 16; index++) indices.Add(index);
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(RegistryBase);
            if (root != null)
            {
                foreach (string name in root.GetSubKeyNames())
                    if (name.StartsWith("Controller", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(name[10..], out int index))
                        indices.Add(index);
            }
        }
        catch { }
        return indices;
    }

    private static ManualResetEventSlim GetGate(int index)
    {
        lock (GatesLock)
        {
            if (!TeardownGates.TryGetValue(index, out ManualResetEventSlim? gate))
            {
                gate = new ManualResetEventSlim(true);
                TeardownGates[index] = gate;
            }
            return gate;
        }
    }

    private static void WaitForPriorTeardown(int index)
    {
        GetGate(index).Wait(TimeoutScale.Apply(120_000));
    }

    private static void WaitForHidMaestroHosts()
    {
        foreach (Process process in Process.GetProcessesByName("WUDFHost"))
        {
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    if (!module.ModuleName.Equals("HIDMaestro.dll", StringComparison.OrdinalIgnoreCase))
                        continue;
                    process.WaitForExit(TimeoutScale.Apply(10_000));
                    break;
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private static void DrainOrphanedWudfHosts()
    {
        foreach (Process process in Process.GetProcessesByName("WUDFHost"))
        {
            try
            {
                bool hostsOurs = false;
                bool hostsOtherDriver = false;
                foreach (ProcessModule module in process.Modules)
                {
                    string name = module.ModuleName ?? "";
                    string path = module.FileName ?? "";
                    if (name.Equals("HIDMaestro.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        hostsOurs = true;
                        continue;
                    }
                    if (path.Contains(@"\DriverStore\FileRepository\", StringComparison.OrdinalIgnoreCase)
                        || path.Contains(@"\System32\drivers\umdf\", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!name.Equals("Mshidumdf.dll", StringComparison.OrdinalIgnoreCase))
                            hostsOtherDriver = true;
                    }
                }
                if (hostsOurs && !hostsOtherDriver)
                {
                    process.Kill();
                    process.WaitForExit(TimeoutScale.Apply(2000));
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenEventW(uint desiredAccess, bool inheritHandle, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

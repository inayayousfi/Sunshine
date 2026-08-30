using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace HIDMaestro.Internal;

/// <summary>Owns the input section and event shared with the UMDF2 mouse driver.</summary>
internal static class SharedMemoryIO
{
    private const int DataCapacity = 256;
    private const int GipDataLength = 14;
    private const int ExtendedDataCapacity = 80;
    private const int DataOffset = 8;
    private const int ExtendedSizeOffset = DataOffset + DataCapacity + GipDataLength;
    private const int ExtendedDataOffset = ExtendedSizeOffset + 4;
    private const int SharedInputSize = ExtendedDataOffset + ExtendedDataCapacity;

    private const string Sddl =
        "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;LS)(A;;GR;;;WD)";
    private const uint PageReadWrite = 0x04;
    private const uint FileMapRead = 0x02;
    private const uint FileMapWrite = 0x04;
    private const uint EventModifyState = 0x0002;
    private const uint Synchronize = 0x00100000;

    private static readonly Dictionary<int, IntPtr> InputHandles = new();
    private static readonly Dictionary<int, IntPtr> InputViews = new();
    private static readonly Dictionary<int, IntPtr> InputEvents = new();

    public static IntPtr EnsureInputMapping(int controllerIndex)
    {
        lock (InputViews)
        {
            if (InputViews.TryGetValue(controllerIndex, out IntPtr existing))
                return existing;

            (IntPtr handle, IntPtr view) = CreateSection(
                $@"Global\HIDMaestroInput{controllerIndex}", SharedInputSize);
            Marshal.Copy(new byte[SharedInputSize], 0, view, SharedInputSize);

            IntPtr inputEvent = CreateNamedEvent(
                $@"Global\HIDMaestroInputEvent{controllerIndex}");
            InputHandles[controllerIndex] = handle;
            InputViews[controllerIndex] = view;
            InputEvents[controllerIndex] = inputEvent;
            return view;
        }
    }

    public static IntPtr GetInputEvent(int controllerIndex)
    {
        lock (InputViews)
            return InputEvents.TryGetValue(controllerIndex, out IntPtr value)
                ? value : IntPtr.Zero;
    }

    /// <summary>Publishes a complete report through the driver's verbatim report area.</summary>
    public static void WriteInputFrame(
        IntPtr view, IntPtr eventHandle, ref uint seqNo,
        byte[] data, int dataLen,
        int dataOffset = 0, byte[]? extendedData = null, int extendedLen = 0)
    {
        uint pending = seqNo + 1;
        Marshal.WriteInt32(view, 0, (int)pending);
        Thread.MemoryBarrier();

        Marshal.WriteInt32(view, 4, dataLen);
        if (dataLen > 0)
            Marshal.Copy(data, dataOffset, view + DataOffset, dataLen);
        if (extendedData != null && extendedLen > 0)
        {
            int copyLength = Math.Min(extendedLen, ExtendedDataCapacity);
            Marshal.Copy(extendedData, 0, view + ExtendedDataOffset, copyLength);
            Marshal.WriteInt32(view, ExtendedSizeOffset, copyLength);
        }
        else
        {
            Marshal.WriteInt32(view, ExtendedSizeOffset, 0);
        }

        Thread.MemoryBarrier();
        seqNo = pending + 1;
        Marshal.WriteInt32(view, 0, (int)seqNo);
        if (eventHandle != IntPtr.Zero)
            SetEvent(eventHandle);
    }

    public static void DestroyController(int controllerIndex)
    {
        lock (InputViews)
        {
            if (InputViews.Remove(controllerIndex, out IntPtr view) && view != IntPtr.Zero)
                UnmapViewOfFile(view);
            if (InputHandles.Remove(controllerIndex, out IntPtr handle) && handle != IntPtr.Zero)
                CloseHandle(handle);
            if (InputEvents.Remove(controllerIndex, out IntPtr inputEvent) && inputEvent != IntPtr.Zero)
                CloseHandle(inputEvent);
        }
    }

    public static void Cleanup()
    {
        lock (InputViews)
        {
            foreach (IntPtr view in InputViews.Values)
                if (view != IntPtr.Zero) UnmapViewOfFile(view);
            foreach (IntPtr handle in InputHandles.Values)
                if (handle != IntPtr.Zero) CloseHandle(handle);
            foreach (IntPtr inputEvent in InputEvents.Values)
                if (inputEvent != IntPtr.Zero) CloseHandle(inputEvent);
            InputViews.Clear();
            InputHandles.Clear();
            InputEvents.Clear();
        }
    }

    private static (IntPtr handle, IntPtr view) CreateSection(string name, int size)
    {
        IntPtr attributes = CreateSecurityAttributes(out IntPtr descriptor);
        IntPtr handle;
        try
        {
            handle = CreateFileMappingW(new IntPtr(-1), attributes,
                PageReadWrite, 0, (uint)size, name);
        }
        finally
        {
            Marshal.FreeHGlobal(attributes);
            LocalFree(descriptor);
        }
        if (handle == IntPtr.Zero) throw new Win32Exception();

        IntPtr view = MapViewOfFile(handle, FileMapWrite | FileMapRead,
            0, 0, (UIntPtr)size);
        if (view != IntPtr.Zero) return (handle, view);

        int error = Marshal.GetLastWin32Error();
        CloseHandle(handle);
        throw new Win32Exception(error);
    }

    private static IntPtr CreateNamedEvent(string name)
    {
        IntPtr attributes = CreateSecurityAttributes(out IntPtr descriptor);
        IntPtr value;
        try
        {
            value = CreateEventExW(attributes, name, 0, EventModifyState | Synchronize);
        }
        finally
        {
            Marshal.FreeHGlobal(attributes);
            LocalFree(descriptor);
        }
        if (value == IntPtr.Zero) throw new Win32Exception();
        return value;
    }

    private static IntPtr CreateSecurityAttributes(out IntPtr descriptor)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                Sddl, 1, out descriptor, IntPtr.Zero))
            throw new Win32Exception();

        SECURITY_ATTRIBUTES attributes = new()
        {
            nLength = (uint)Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = descriptor,
        };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_ATTRIBUTES>());
        Marshal.StructureToPtr(attributes, pointer, false);
        return pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileMappingW(IntPtr file, IntPtr attributes,
        uint protect, uint maximumSizeHigh, uint maximumSizeLow, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access,
        uint offsetHigh, uint offsetLow, UIntPtr bytesToMap);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr address);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string descriptor, uint revision, out IntPtr securityDescriptor, IntPtr size);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEventExW(IntPtr attributes, string name,
        uint flags, uint desiredAccess);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(IntPtr handle);
}

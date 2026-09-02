using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HIDMaestro;

namespace HIDMaestro.NativeMouse;

internal sealed class NativeMouseHandle : IDisposable
{
    private readonly object _sync = new();
    private readonly HMContext _context;
    private readonly HMController _mouse;
    private HMMouseButton _buttons;
    private bool _disposed;

    internal NativeMouseHandle()
    {
        _context = new HMContext();
        try
        {
            _context.InstallDriver();
            HMProfile profile = HMProfileBuilder.GenericMouse()
                .Vid(0x1209)
                .Pid(0x0003)
                .ManufacturerString("HIDMaestro")
                .ProductString("HIDMaestro Sunshine Mouse")
                .Build();
            _mouse = _context.CreateController(profile);
        }
        catch
        {
            _context.Dispose();
            throw;
        }
    }

    internal void MoveRelative(int x, int y)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _mouse.SubmitMouseState(new HMMouseState
            {
                Buttons = _buttons,
                DeltaX = (short)Math.Clamp(x, short.MinValue, short.MaxValue),
                DeltaY = (short)Math.Clamp(y, short.MinValue, short.MaxValue),
            });
        }
    }

    internal void MoveAbsolute(int x, int y)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _mouse.SubmitAbsoluteMouseState(new HMAbsoluteMouseState
            {
                Buttons = _buttons,
                X = (ushort)Math.Clamp(x, 0, 32767),
                Y = (ushort)Math.Clamp(y, 0, 32767),
            });
        }
    }

    internal void SetButton(uint button, bool pressed)
    {
        HMMouseButton mask = button switch
        {
            1 => HMMouseButton.Left,
            2 => HMMouseButton.Right,
            3 => HMMouseButton.Middle,
            4 => HMMouseButton.Back,
            5 => HMMouseButton.Forward,
            _ => throw new ArgumentOutOfRangeException(nameof(button), "Mouse button must be from 1 to 5."),
        };

        lock (_sync)
        {
            ThrowIfDisposed();
            if (pressed)
                _buttons |= mask;
            else
                _buttons &= ~mask;
            _mouse.SubmitMouseState(new HMMouseState { Buttons = _buttons });
        }
    }

    internal void Scroll(int vertical, int horizontal)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _mouse.SubmitMouseState(new HMMouseState
            {
                Buttons = _buttons,
                Wheel = ClampWheel(vertical),
                HorizontalWheel = ClampWheel(horizontal),
            });
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _mouse.Dispose();
            _context.Dispose();
        }
    }

    private static sbyte ClampWheel(int value) => (sbyte)Math.Clamp(value, -127, 127);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

public static class NativeMouseExports
{
    [ThreadStatic]
    private static nint s_lastError;

    [UnmanagedCallersOnly(EntryPoint = "hidmaestro_mouse_create", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Create()
    {
        try
        {
            ClearError();
            var target = new NativeMouseHandle();
            return GCHandle.ToIntPtr(GCHandle.Alloc(target));
        }
        catch (Exception ex)
        {
            SetError(ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "hidmaestro_mouse_destroy", CallConvs = [typeof(CallConvCdecl)])]
    public static int Destroy(nint handle)
    {
        try
        {
            ClearError();
            GCHandle gcHandle = RequireHandle(handle);
            ((NativeMouseHandle)gcHandle.Target!).Dispose();
            gcHandle.Free();
            return 0;
        }
        catch (Exception ex)
        {
            SetError(ex);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "hidmaestro_mouse_relative", CallConvs = [typeof(CallConvCdecl)])]
    public static int Relative(nint handle, int x, int y) =>
        Invoke(handle, mouse => mouse.MoveRelative(x, y));

    [UnmanagedCallersOnly(EntryPoint = "hidmaestro_mouse_absolute", CallConvs = [typeof(CallConvCdecl)])]
    public static int Absolute(nint handle, int x, int y) =>
        Invoke(handle, mouse => mouse.MoveAbsolute(x, y));

    [UnmanagedCallersOnly(EntryPoint = "hidmaestro_mouse_button", CallConvs = [typeof(CallConvCdecl)])]
    public static int Button(nint handle, uint button, int pressed) =>
        Invoke(handle, mouse => mouse.SetButton(button, pressed != 0));

    [UnmanagedCallersOnly(EntryPoint = "hidmaestro_mouse_scroll", CallConvs = [typeof(CallConvCdecl)])]
    public static int Scroll(nint handle, int vertical, int horizontal) =>
        Invoke(handle, mouse => mouse.Scroll(vertical, horizontal));

    [UnmanagedCallersOnly(EntryPoint = "hidmaestro_mouse_last_error", CallConvs = [typeof(CallConvCdecl)])]
    public static nint LastError() => s_lastError;

    private static int Invoke(nint handle, Action<NativeMouseHandle> action)
    {
        try
        {
            ClearError();
            action((NativeMouseHandle)RequireHandle(handle).Target!);
            return 0;
        }
        catch (Exception ex)
        {
            SetError(ex);
            return -1;
        }
    }

    private static GCHandle RequireHandle(nint handle)
    {
        if (handle == 0)
            throw new ArgumentException("Mouse handle is null.", nameof(handle));
        GCHandle gcHandle = GCHandle.FromIntPtr(handle);
        if (gcHandle.Target is not NativeMouseHandle)
            throw new ArgumentException("Mouse handle is invalid.", nameof(handle));
        return gcHandle;
    }

    private static void SetError(Exception error)
    {
        ClearError();
        s_lastError = Marshal.StringToCoTaskMemUTF8(error.ToString());
    }

    private static void ClearError()
    {
        if (s_lastError == 0)
            return;
        Marshal.FreeCoTaskMem(s_lastError);
        s_lastError = 0;
    }
}

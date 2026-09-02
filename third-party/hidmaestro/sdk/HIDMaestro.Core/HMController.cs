using System;
using HIDMaestro.Internal;

namespace HIDMaestro;

/// <summary>A live virtual HID mouse created by <see cref="HMContext"/>.</summary>
public sealed class HMController : IDisposable
{
    private readonly HMContext _context;
    private readonly IntPtr _inputView;
    private readonly IntPtr _inputEvent;
    private readonly IntPtr _inputSpaceEvent;
    private readonly byte[] _report = new byte[HMMouseState.ReportSize];
    private bool _disposed;

    internal int Index { get; }
    internal string? InstanceId { get; }
    public HMProfile Profile { get; }

    internal HMController(HMContext context, int index, HMProfile profile, string? instanceId)
    {
        _context = context;
        Index = index;
        InstanceId = instanceId;
        Profile = profile;
        _inputView = SharedMemoryIO.EnsureInputMapping(index);
        _inputEvent = SharedMemoryIO.GetInputEvent(index);
        _inputSpaceEvent = SharedMemoryIO.GetInputSpaceEvent(index);
    }

    /// <summary>Submits one relative mouse report.</summary>
    public void SubmitMouseState(in HMMouseState state)
    {
        ThrowIfDisposed();
        state.WriteReport(_report, Profile.ButtonCount);
        SubmitReport();
    }

    /// <summary>Submits one absolute mouse report.</summary>
    public void SubmitAbsoluteMouseState(in HMAbsoluteMouseState state)
    {
        ThrowIfDisposed();
        state.WriteReport(_report, Profile.ButtonCount);
        SubmitReport();
    }

    private void SubmitReport()
    {
        SharedMemoryIO.WriteInputFrame(
            _inputView, _inputEvent, _inputSpaceEvent,
            Array.Empty<byte>(), 0,
            extendedData: _report, extendedLen: _report.Length);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>Removes the virtual mouse and releases its shared-memory channel.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context.OnControllerDisposing(this);
    }
}

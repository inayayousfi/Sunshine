using System;
using System.Collections.Generic;
using HIDMaestro.Internal;

namespace HIDMaestro;

/// <summary>Owns the HIDMaestro mouse driver and virtual mouse instances.</summary>
public sealed class HMContext : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<int, HMController> _controllers = new();
    private bool _disposed;

    /// <summary>Creates a context and starts best-effort driver payload prewarming.</summary>
    public HMContext()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try { _ = EmbeddedManifest.Sha256Hex; } catch { }
            try { DriverBuilder.EnsureExtracted(); } catch { }
            try { DriverBuilder.IsDriverInstalled(); } catch { }
        });
    }

    /// <summary>Installs or updates the embedded UMDF2 driver.</summary>
    public void InstallDriver()
    {
        ThrowIfDisposed();
        DeviceOrchestrator.RemoveAllVirtualControllers(preserveInstall: true);
        if (!DriverBuilder.FullDeploy())
            throw new InvalidOperationException(
                "Driver install failed. Run elevated and check pnputil output.");
    }

    /// <summary>Creates a virtual mouse from a mouse profile.</summary>
    public HMController CreateController(HMProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsDeployable)
            throw new ArgumentException($"Profile '{profile.Id}' has no HID descriptor and cannot be deployed.", nameof(profile));
        ThrowIfDisposed();

        int index;
        lock (_lock)
        {
            index = 0;
            while (_controllers.ContainsKey(index)) index++;
        }

        string infPath = System.IO.Path.Combine(DriverBuilder.BuildDir, "hidmaestro.inf");
        string? instanceId;
        try
        {
            instanceId = DeviceOrchestrator.SetupController(index, profile.Inner, infPath);
        }
        catch
        {
            try { DeviceOrchestrator.TeardownController(index, null); } catch { }
            throw;
        }

        var controller = new HMController(this, index, profile, instanceId);
        lock (_lock) _controllers[index] = controller;
        return controller;
    }

    internal void OnControllerDisposing(HMController controller)
    {
        lock (_lock) _controllers.Remove(controller.Index);
        DeviceOrchestrator.TeardownController(controller.Index, controller.InstanceId);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>Disposes every mouse owned by this context. Safe to call repeatedly.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        HMController[] controllers;
        lock (_lock)
        {
            controllers = new HMController[_controllers.Count];
            _controllers.Values.CopyTo(controllers, 0);
            _controllers.Clear();
        }

        foreach (HMController controller in controllers)
        {
            try { controller.Dispose(); } catch { }
        }
    }
}

# HIDMaestro Mouse Backend

This fork replaces Sunshine's Windows mouse backend with an in-tree snapshot of
[HIDMaestro](https://github.com/inayayousfi/HIDMaestro). Keyboard, touch, pen,
gamepad, and every non-Windows input path retain their existing backends. The
vendored source is pinned in `third-party/hidmaestro`; building and deploying
this branch does not clone or track another repository or feature branch.

## Behavior

Windows mouse input is sent only through `HIDMaestro.NativeMouse.dll`. Sunshine
loads the DLL from its application directory and creates the virtual mouse during
startup. A missing DLL, missing export, driver installation failure, or virtual
mouse creation failure aborts Sunshine startup. There is no `SendInput` or
libvirtualhid fallback for mouse events.

The virtual device supports signed 16-bit relative movement, absolute movement,
five buttons, vertical scrolling, and horizontal AC Pan. Relative and absolute
reports use separate HID Mouse application collections because the Windows mouse
class driver rejects both pointer modes in one collection.

## Build And Install

Run the following from an Administrator PowerShell prompt on the Windows x64
host:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\windows_hidmaestro_build.ps1
```

The script installs missing prerequisites, builds the vendored HIDMaestro source
and the current Sunshine checkout, runs the Sunshine test executable, backs up
the current installation, and deploys the fork. Temporary build and staging
files are written under `C:\hmb`. Existing Sunshine configuration, applications,
certificates, and Moonlight pairings are left in place.

If a prerequisite requires a reboot, the script stops before changing Sunshine
and prints the exact rerun command. It never reboots automatically.

## Restore

The latest deployment writes a restore command to:

```text
%ProgramData%\Sunshine-HIDMaestro\restore.ps1
```

Run it from Administrator PowerShell to stop the service, restore the complete
pre-deployment installation, and restart Sunshine:

```powershell
powershell.exe -ExecutionPolicy Bypass -File "$env:ProgramData\Sunshine-HIDMaestro\restore.ps1"
```

## Runtime Diagnostics

Startup and submission failures are written to the normal Sunshine log with an
`HIDMaestro` message. Startup failure is intentional: continuing would silently
route mouse input through an injection path that protected games can reject.

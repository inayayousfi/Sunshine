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

Mouse reports cross the SDK/driver boundary through an ordered 64-slot shared
memory queue. The producer waits up to one second when the queue is full instead
of overwriting unread input; a stalled driver is reported as an explicit
submission error. The driver completes the first pending report immediately and
paces an existing backlog at one report per millisecond. This preserves every
submitted movement while avoiding the burst collapse observed when the complete
backlog was handed to the Windows mouse stack at once.

## Build And Install

Run the following from an Administrator PowerShell prompt on the Windows x64
host:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\windows_hidmaestro_build.ps1
```

The script installs missing prerequisites, builds the vendored HIDMaestro source,
the pinned libvirtualhid Windows driver and broker, and the current Sunshine
checkout. It runs every Sunshine test except
`EncoderVariants/EncoderTest.ValidateEncoder/*`, whose encoder probe terminates
the non-interactive Windows test process before Google Test can report a result.
It then stops the affected services, backs up both installations and the active
HIDMaestro driver package, deploys the matched components, and checks that
Sunshine remains running. Temporary build and staging files are written under
`C:\hmb\sunshine-hidmaestro`. Existing Sunshine configuration, applications,
certificates, and Moonlight pairings are left in place.

If a prerequisite requires a reboot, the script stops before changing Sunshine
and prints the exact rerun command. It never reboots automatically.

## Restore

The latest deployment writes a restore command to:

```text
%ProgramData%\Sunshine-HIDMaestro\restore.ps1
```

Run it from Administrator PowerShell to restore the pre-deployment Sunshine and
libvirtualhid program files, HIDMaestro driver package, driver manifest hash, and
original service states. The restore package includes the libvirtualhid installer
scripts and HIDMaestro DriverStore files, so it does not depend on the source
checkout remaining available. Sunshine's live `config` directory is excluded
from mirror operations and remains unchanged during both deployment and restore:

```powershell
powershell.exe -ExecutionPolicy Bypass -File "$env:ProgramData\Sunshine-HIDMaestro\restore.ps1"
```

## Runtime Diagnostics

Startup and submission failures are written to the normal Sunshine log with an
`HIDMaestro` message. Startup failure is intentional: continuing would silently
route mouse input through an injection path that protected games can reject.

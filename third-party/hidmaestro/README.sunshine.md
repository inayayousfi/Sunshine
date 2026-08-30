# Sunshine Vendoring Notes

This directory is a mouse-focused source snapshot derived from
[inayayousfi/HIDMaestro](https://github.com/inayayousfi/HIDMaestro) commit
`d042641b09a56dd8e1f1c9d7812c41dd3913cfd4`.

Sunshine vendors the required driver and SDK sources so its Windows HID mouse
backend can be built and deployed from one repository and one branch. The build
does not fetch a moving HIDMaestro branch. Gamepad profiles, XUSB, OpenVR,
USB/IP payloads, examples, and investigation artifacts from the source project
are intentionally omitted. `sdk/HIDMaestro.NativeMouse` is the native C ABI
consumed by Sunshine.

HIDMaestro is distributed under the MIT License in `LICENSE`.

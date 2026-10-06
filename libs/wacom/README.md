Bundled Wacom STU runtime files used by the GBEX Front Desk Agent native STU-430 capture path.

Source:
- Wacom official STU SDK samples repository: https://github.com/Wacom-Developer/stu-sdk-samples
- `Interop.wgssSTU.dll` copied from `extra samples/csharp/TestDemoButtons`
- `wgssSTU.dll` copied from `extra samples/csharp/TestDemoButtons-SxS`

Purpose:
- The agent is published as `win-x86` to match the common Wacom STU COM/runtime path used by the official C# samples.
- `app.manifest` binds COM activation to the bundled DLL's embedded assembly identity `wgssSTU`, version `2.4.0.0`, following Wacom's `TestDemoButtons-SxS` example. No separately installed SDK or registry ProgID lookup is required.
- CI runs the published x86 app with `--wacom-sdk-self-test` on Windows without installing a Wacom SDK. A release is blocked unless UsbDevices, Tablet and ProtocolHelper activate successfully.
- This SDK activation test does not replace testing capture with a physical STU-430.

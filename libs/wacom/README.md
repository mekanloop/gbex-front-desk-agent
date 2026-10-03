Bundled Wacom STU runtime files used by the GBEX Front Desk Agent native STU-430 capture path.

Source:
- Wacom official STU SDK samples repository: https://github.com/Wacom-Developer/stu-sdk-samples
- `Interop.wgssSTU.dll` copied from `extra samples/csharp/TestDemoButtons`
- `wgssSTU.dll` copied from `extra samples/csharp/TestDemoButtons-SxS`

Purpose:
- The agent is published as `win-x86` to match the common Wacom STU COM/runtime path used by the official C# samples.
- If the target PC already has Wacom STU SDK/COM installed, that registration may still be used by Windows COM.

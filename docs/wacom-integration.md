# STU-430 capture integration

The production path uses Wacom's native STU SDK COM library in the x86
Windows agent. `app.manifest` selects the bundled registration-free runtime.
No signature drawing canvas or SigCaptX browser service participates in capture.

## Official basis

- [Wacom API guide](https://developer-docs.wacom.com/docs/stu-sdk/windows-sdk/api-guide/)
- [Wacom deployment guide](https://developer-docs.wacom.com/docs/stu-sdk/windows-sdk/deployment/)
- [Official DemoButtons source](https://github.com/Wacom-Developer/stu-sdk-samples/tree/master/samples/csharp/DemoButtons)
- [Signature SDK vs STU SDK](https://developer-support.wacom.com/hc/en-us/articles/9354442487063-Differences-between-STU-and-Signature-SDKs)

## Capture sequence

1. The web panel starts capture for its active account and capture session.
2. The agent enumerates STU devices through `UsbDevices`, connects through
   `Tablet.usbConnect(device, true)`, and reads capability/information.
   It retries brief USB contention four times over about one second, as the
   official sample recommends. It does not stop other applications/services.
3. The agent encodes a tablet-sized prompt and buttons with `ProtocolHelper`,
   sends it using `writeImage`, subscribes to pen events, and enables inking.
4. Real pen coordinates are copied out of COM events and marshalled to the
   UI thread. Hover and button presses cannot validate a blank signature.
   The PC window is a status display, not a drawing surface.
5. A pen down/up on the same tablet button confirms, clears, or cancels.
   Only meaningful ink is rendered to a transparent PNG with the pad's ratio.
6. The agent posts the PNG to the existing signature API with the staff session,
   account ID and capture session ID. It reads that account/session's signature
   records back and checks the exact returned signature ID before notifying
   the web panel. This verifies the record, not contract PDF generation.
7. The pad is disconnected and events released on every completed/cancelled
   capture. Temporary PNGs are deleted after the upload attempt. Timeout is
   two minutes. There is no mouse/touch signature fallback.

The application must be listening while the customer signs. Visible ink on a
tablet by itself is not evidence that this application received any pen events.

## Automated verification

- Build and publish a self-contained Windows x86 executable.
- Launch the actual published executable on a clean Windows runner and activate
  UsbDevices, Tablet and ProtocolHelper without a separate SDK installer.
- Exercise the production ink processor with synthetic reports: empty input,
  hover-only movement, buttons and drag-off, two separate strokes, PNG dimensions
  and visible pixels, clear, and ten successive synthetic captures.
- Check that upload receipts with another account/session, or absent from the
  server readback, cannot be treated as success.

These checks do not establish hardware or production API success.

## Physical acceptance: not yet performed on the front desk STU-430

- Pad displays this application's prompt and OK/TEMIZLE/IPTAL buttons.
- Sign on the pad and confirm; the matching preview appears in the panel.
- Reload the same sale; the signature record remains associated correctly.
- Clear/cancel and empty confirm behave correctly; repeat ten times.
- Unplug during capture, reconnect and retry without restarting.
- A contract generated from the active sale contains that customer's signature.

Do not label a release hardware-verified until these checks are completed.

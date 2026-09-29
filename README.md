# GBEX Front Desk Agent

Windows front desk application for GBEX counter sales.

## What this app does

- Opens the live GBEX front desk panel in a native Windows shell.
- Detects USB-attached front desk devices inside the same app.
- Scans ID documents through Windows WIA-compatible scanners.
- Uploads scanned ID files to the logged-in GBEX front desk session.
- Watches the scanner output folder and auto-uploads new ID files when the scanner is not exposed as WIA.
- Detects Wacom STU signature pads and exposes status in the app.
- Captures Wacom STU signatures through Wacom STU-SigCaptX when it is installed on the Windows PC.
- Uploads signature images to the logged-in GBEX front desk session.
- Opens an in-app signature capture screen when Wacom STU SDK is not installed, so the sales workflow is not blocked.
- Keeps Wacom integration inside the same native app; no separate GBEX bridge/service is required.
- Keeps device work native; there is no separate local bridge/service for operators to run.

## First production target

- URL: `https://app.gbex.com.tr/admin/front-desk`
- Scanner: Yumi YC-3040 DN. Direct scan works if installed as a Windows WIA scanner; otherwise configure the scanner software to save files to a folder and use `Tarama Klasörü İzle`.
- Signature pad: Wacom STU-430. Device detection is native. True STU screen pen capture uses Wacom STU-SigCaptX/SDK on the PC; the app also has a built-in signature capture fallback for immediate operation.

## Build

The Windows app must be built on Windows:

```powershell
dotnet restore Gbex.FrontDesk.Agent.Windows.sln
dotnet build Gbex.FrontDesk.Agent.Windows.sln -c Release
dotnet publish src/Gbex.FrontDesk.Agent.Windows/Gbex.FrontDesk.Agent.Windows.csproj -c Release -r win-x64 --self-contained true -o publish/GbexFrontDeskAgent
```

GitHub Actions builds the installer and publishes `GbexFrontDeskAgentSetup.exe` on every push to `main`.

## Operator notes

1. Install scanner drivers first.
2. Install Wacom STU drivers/SDK before signature capture is used.
3. Start GBEX Front Desk Agent.
4. Log in with the front desk staff account.
5. Use the native toolbar to scan identity documents, watch the scanner output folder, capture/upload signatures, and check USB device status.

## Device integration notes

- Yumi YC-3040 DN: direct capture uses Windows WIA. If the device is TWAIN/vendor-software only, use the watched-folder mode and set the scanner software output folder to the same folder.
- Wacom STU-430: device detection is native. `İmza Al` first opens the bundled SigCaptX capture page and talks to the local Wacom SigCaptX service. The target Windows PC must have Wacom STU-SigCaptX installed and running for true device-screen capture. If SigCaptX is not available, the app falls back to the in-app signature capture screen and uploads the saved signature through the same GBEX API.

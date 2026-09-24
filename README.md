# GBEX Front Desk Agent

Windows front desk application for GBEX counter sales.

## What this app does

- Opens the live GBEX front desk panel in a native Windows shell.
- Detects USB-attached front desk devices inside the same app.
- Scans ID documents through Windows WIA-compatible scanners.
- Uploads scanned ID files to the logged-in GBEX front desk session.
- Detects Wacom STU signature pads and exposes status in the app.
- Uploads signature images to the logged-in GBEX front desk session.
- Keeps Wacom integration inside the same native app; no separate GBEX bridge/service is required.
- Keeps device work native; there is no separate local bridge/service for operators to run.

## First production target

- URL: `https://app.gbex.com.tr/admin/front-desk`
- Scanner: Yumi YC-3040 DN, if installed as a Windows WIA scanner.
- Signature pad: Wacom STU-430, with Wacom drivers/SDK installed on the PC.

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
5. Use the native toolbar to scan identity documents, upload signatures, and check USB device status.

## Device integration notes

- Yumi YC-3040 DN: must appear in Windows as a WIA scanner. The app calls Windows WIA directly and uploads the scanned document to GBEX.
- Wacom STU-430: device detection is native. Real-time pen capture requires Wacom's STU SDK/driver on the target Windows PC. The app does not require a separate GBEX bridge process.

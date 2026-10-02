# GBEX Front Desk Agent

Windows front desk application for GBEX counter sales.

## What this app does

- Opens the live GBEX front desk panel in a native Windows shell.
- Detects USB-attached front desk devices inside the same app.
- Receives active workflow commands from the web panel (`Kimliği cihazdan tara`, `Wacom ile imza al`).
- Scans ID documents through Windows WIA-compatible scanners when the device is exposed by Windows.
- Watches only the secure GBEX scan folder when the scanner/printer works through SMB scan-to-folder.
- Uploads scanned ID files only to the active GBEX capture session; files are not uploaded without an active sale command.
- Detects Wacom STU signature pads and exposes status in the app.
- Captures Wacom STU signatures through Wacom STU-SigCaptX when it is installed on the Windows PC.
- Uploads signature images only to the active GBEX capture session.
- Blocks signature completion when Wacom STU/SigCaptX is not available. The mouse/touch fallback is not accepted as a Wacom device signature.
- Keeps Wacom integration inside the same native app; no separate GBEX bridge/service is required.
- Keeps device work native; there is no separate local bridge/service for operators to run.

## First production target

- URL: `https://panel.gbex.com.tr/admin/front-desk`
- Scanner: Yumi YC-3040 DN. Direct scan works if installed as a Windows WIA scanner. If the printer scans to SMB, the app prepares and watches the secure GBEX folder: `%ProgramData%\GBEX\FrontDesk\Scans\Incoming`.
- Signature pad: Wacom STU-430. Device detection is native. True STU screen pen capture uses Wacom STU-SigCaptX/SDK on the PC; if it is missing, the app shows a blocking error.

## Build

The Windows app must be built on Windows:

```powershell
dotnet restore Gbex.FrontDesk.Agent.Windows.sln
dotnet build Gbex.FrontDesk.Agent.Windows.sln -c Release
dotnet publish src/Gbex.FrontDesk.Agent.Windows/Gbex.FrontDesk.Agent.Windows.csproj -c Release -r win-x64 --self-contained true -o publish/GbexFrontDeskAgent
```

GitHub Actions builds both:

- `GbexFrontDeskAgentSetup.exe` — recommended for customer PCs.
- `GbexFrontDeskAgent-win-x64.zip` — portable fallback package.

## Operator notes

1. Install scanner drivers first.
2. Install Wacom STU drivers/SDK before signature capture is used.
3. Start GBEX Front Desk Agent.
4. Log in with the front desk staff account.
5. Run `Otomatik Cihaz Kur` once. The app creates the secure GBEX scan folder and attempts to share it as `GBEXSCAN$`.
6. Use the web workflow buttons only: `Kimliği cihazdan tara` and `Wacom ile imza al`.
7. Operators must not choose files manually; the app binds device output to the active customer sale.

## Troubleshooting

- Prefer the setup installer. Do not run the app directly from inside the ZIP archive.
- If Windows blocks the downloaded file, open file properties and unblock it.
- If the app fails to start, inspect:
  `%LOCALAPPDATA%\GBEX\FrontDeskAgent\front-desk-agent.log`
- For true Wacom STU screen capture, install Wacom STU-SigCaptX on the customer Windows PC and confirm the local SigCaptX service is running.

## Device integration notes

- Yumi YC-3040 DN: direct capture uses Windows WIA. If the device is SMB scan-to-folder only, configure the device destination to the app-created `GBEXSCAN$` share. The app will upload the first new PDF/JPG only after the active web workflow requests an ID scan.
- Wacom STU-430: device detection is native. The web workflow command opens the bundled SigCaptX capture page and talks to the local Wacom SigCaptX service. The target Windows PC must have Wacom STU-SigCaptX installed and running for true device-screen capture. If SigCaptX is not available, the app returns a blocking error and does not open the in-app mouse/touch canvas.

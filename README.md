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
- Captures Wacom STU-430 signatures through the native Wacom STU SDK/COM path.
- Uploads signature images only to the active GBEX capture session.
- Blocks signature completion when native Wacom capture is not available. The mouse/touch fallback is not accepted as a Wacom device signature.
- Keeps Wacom integration inside the same native app; no separate GBEX bridge/service is required.
- Keeps device work native; there is no separate local bridge/service for operators to run.

## First production target

- URL: `https://panel.gbex.com.tr/admin/front-desk`
- Scanner: Yumi YC-3040 DN. Direct scan works if installed as a Windows WIA scanner. If the printer scans to SMB, the app prepares and watches the secure GBEX folder: `%ProgramData%\GBEX\FrontDesk\Scans\Incoming`.
- Signature pad: Wacom STU-430. Device detection and true pen capture use native Wacom STU SDK/COM. The customer signs on the STU-430 screen, not on the PC screen.

## Build

The Windows app must be built on Windows:

```powershell
dotnet restore Gbex.FrontDesk.Agent.Windows.sln
dotnet build Gbex.FrontDesk.Agent.Windows.sln -c Release
dotnet publish src/Gbex.FrontDesk.Agent.Windows/Gbex.FrontDesk.Agent.Windows.csproj -c Release -r win-x86 --self-contained true -o publish/GbexFrontDeskAgent
```

GitHub Actions builds both:

- `GbexFrontDeskAgentSetup.exe` — recommended for customer PCs.
- `GbexFrontDeskAgent-win-x86.zip` — portable fallback package.

## Operator notes

1. Install scanner drivers first.
2. Install/repair Wacom STU SDK/COM if Windows does not expose the STU runtime.
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
- For true Wacom STU screen capture, use GBEX Front Desk Agent v1.1.4 or newer. It is published as win-x86 to match the common Wacom STU COM runtime.

## Device integration notes

- Yumi YC-3040 DN: direct capture uses Windows WIA. If the device is SMB scan-to-folder only, configure the device destination to the app-created `GBEXSCAN$` share. The app will upload the first new PDF/JPG only after the active web workflow requests an ID scan.
- Wacom STU-430: device detection and signature capture are native. The web workflow command opens a small operator status dialog, draws the prompt/buttons on the STU-430 screen, captures raw pen data from the device, renders a PNG, and uploads it to the active sale. SigCaptX/WebView is not used for production signature capture.

namespace Gbex.FrontDesk.Agent.Windows.Services;

public sealed class WacomSignatureService
{
    public bool IsSdkAvailable()
    {
        // Wacom STU SDK normally registers COM classes named wgssSTU.*.
        // Driver/device detection is handled by DeviceMonitor; this check is
        // for actual native signature capture capability.
        return Type.GetTypeFromProgID("wgssSTU.UsbDevices") is not null;
    }

    public string StatusText(bool deviceDetected)
    {
        if (!deviceDetected)
        {
            return "Wacom STU cihazı USB'de görünmüyor.";
        }

        return IsSdkAvailable()
            ? "Wacom STU cihazı algılandı; native Wacom STU SDK hazır. İmza doğrudan STU-430 cihaz ekranından alınır."
            : "Wacom STU cihazı algılandı ancak native Wacom STU SDK/COM hazır değil. GBEX Agent 32-bit Wacom modülüyle kurulmalı; SigCaptX/WebView fallback kullanılmaz.";
    }

    public Task<string> CaptureSignatureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSdkAvailable())
        {
            throw new InvalidOperationException(
                "Wacom STU SDK/COM bu Windows hesabında hazır değil. GBEX Agent 32-bit Wacom modülüyle kurulmalı veya Wacom STU SDK kurulumu onarılmalı."
            );
        }

        throw new NotSupportedException("Bu eski servis artık kullanılmıyor; imza WacomStu430CaptureService ile native alınır.");
    }
}

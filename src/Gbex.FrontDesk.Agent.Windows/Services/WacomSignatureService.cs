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
            ? "Wacom STU cihazı algılandı; klasik Wacom STU SDK mevcut. İmza alma gerçek cihaz ekranı üzerinden denenir."
            : "Wacom STU cihazı algılandı ancak gerçek cihaz ekranından imza için Wacom STU-SigCaptX/SDK kurulumu gerekir. Fallback imza ekranı kullanılmaz.";
    }

    public Task<string> CaptureSignatureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSdkAvailable())
        {
            throw new InvalidOperationException(
                "Wacom STU SDK/driver bu Windows hesabında hazır değil. STU-430 USB cihazı için Wacom STU SDK/driver kurulunca bu buton native imza yakalama modülünü kullanacak. Şimdilik 'İmza Yükle' ile imza görseli sisteme aktarılabilir."
            );
        }

        throw new NotSupportedException(
            "Wacom STU SDK bulundu fakat imza yakalama akışı için Wacom'un wgssSTU .NET/COM bileşenlerinin hedef PC'deki kesin sürümüyle bağlama yapılması gerekiyor. Bu uygulama ayrı bridge kullanmaz; entegrasyon native SDK üzerinden tamamlanacak."
        );
    }
}

using System.Runtime.InteropServices;

namespace Gbex.FrontDesk.Agent.Windows.Services;

public sealed class WacomSignatureService
{
    public string StatusText(bool deviceDetected)
    {
        // Use the same typed activation as capture, including registration-free COM.
        wgssSTU.UsbDevices? devices = null;
        try
        {
            devices = new wgssSTU.UsbDevices();
            return devices.Count > 0
                ? $"Wacom STU SDK hazır; USB üzerinden {devices.Count} imza pedi bulundu. İmza cihaz ekranından alınır."
                : "Wacom STU SDK hazır; bağlı imza pedi bulunamadı. USB bağlantısını kontrol edin.";
        }
        catch (Exception ex)
        {
            return $"Wacom imza bileşeni yüklenemedi (0x{ex.HResult:X8}). Front Desk Agent kurulumunu güncelleyin.";
        }
        finally
        {
            if (devices is not null) Marshal.ReleaseComObject(devices);
        }
    }

    public static int VerifyBundledSdk()
    {
        object? devices = null;
        object? tablet = null;
        object? helper = null;
        try
        {
            devices = new wgssSTU.UsbDevices();
            tablet = new wgssSTU.Tablet();
            helper = new wgssSTU.ProtocolHelper();
            Console.WriteLine("WACOM_SDK_OK: UsbDevices, Tablet and ProtocolHelper activated.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WACOM_SDK_LOAD_FAILED: {ex}");
            return 1;
        }
        finally
        {
            foreach (var value in new[] { helper, tablet, devices })
                if (value is not null) Marshal.ReleaseComObject(value);
        }
    }
}

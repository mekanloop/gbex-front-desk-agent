using System.IO;
using System.Runtime.InteropServices;

namespace Gbex.FrontDesk.Agent.Windows.Services;

public sealed class WiaScannerService
{
    public bool IsWiaAvailable()
    {
        return Type.GetTypeFromProgID("WIA.DeviceManager") is not null;
    }

    public bool HasWiaScanner()
    {
        try
        {
            var managerType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (managerType is null)
            {
                return false;
            }

            dynamic manager = Activator.CreateInstance(managerType)
                ?? throw new InvalidOperationException("WIA DeviceManager başlatılamadı.");

            foreach (dynamic info in manager.DeviceInfos)
            {
                if ((int)info.Type == 1)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> ScanIdentityDocumentAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var managerType = Type.GetTypeFromProgID("WIA.DeviceManager")
                ?? throw new InvalidOperationException("Windows WIA servisi bulunamadı. Tarayıcı sürücüsünü kurun.");
            dynamic manager = Activator.CreateInstance(managerType)
                ?? throw new InvalidOperationException("WIA DeviceManager başlatılamadı.");

            dynamic? scannerInfo = null;
            foreach (dynamic info in manager.DeviceInfos)
            {
                // WIA scanner device type is 1.
                if ((int)info.Type == 1)
                {
                    scannerInfo = info;
                    break;
                }
            }

            if (scannerInfo is null)
            {
                throw new InvalidOperationException("WIA uyumlu tarayıcı bulunamadı. Yumi sürücüsünün Windows'a scanner olarak kurulduğunu kontrol edin.");
            }

            dynamic device = scannerInfo.Connect();
            dynamic item = device.Items[1];
            dynamic image = item.Transfer("{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}"); // PNG

            var target = Path.Combine(
                Path.GetTempPath(),
                $"gbex-identity-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.png"
            );

            if (File.Exists(target)) File.Delete(target);
            image.SaveFile(target);

            try
            {
                Marshal.FinalReleaseComObject(image);
                Marshal.FinalReleaseComObject(item);
                Marshal.FinalReleaseComObject(device);
            }
            catch
            {
                // COM cleanup best effort.
            }

            return target;
        }, cancellationToken);
    }
}

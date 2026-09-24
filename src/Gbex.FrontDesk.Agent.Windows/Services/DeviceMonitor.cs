using System.Management;
using Gbex.FrontDesk.Agent.Windows.Models;

namespace Gbex.FrontDesk.Agent.Windows.Services;

public sealed class DeviceMonitor
{
    public IReadOnlyList<DetectedDevice> DetectDevices()
    {
        var devices = new List<DetectedDevice>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, Status, PNPClass FROM Win32_PnPEntity"
            );

            foreach (ManagementObject item in searcher.Get())
            {
                var name = Convert.ToString(item["Name"]) ?? "";
                var id = Convert.ToString(item["DeviceID"]) ?? "";
                var status = Convert.ToString(item["Status"]) ?? "";
                var pnpClass = Convert.ToString(item["PNPClass"]) ?? "";
                var combined = $"{name} {id} {pnpClass}".ToLowerInvariant();

                if (combined.Contains("yumi") || combined.Contains("yc-3040") || combined.Contains("scanner") || pnpClass.Equals("Image", StringComparison.OrdinalIgnoreCase))
                {
                    devices.Add(new DetectedDevice(name, id, "scanner", status));
                }
                else if (combined.Contains("wacom") || combined.Contains("stu-430") || combined.Contains("056a"))
                {
                    devices.Add(new DetectedDevice(name, id, "signature", status));
                }
            }
        }
        catch (Exception ex)
        {
            devices.Add(new DetectedDevice("Windows cihaz listesi okunamadı", ex.GetType().Name, "error", ex.Message));
        }

        return devices;
    }
}

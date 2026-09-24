namespace Gbex.FrontDesk.Agent.Windows.Models;

public sealed record DetectedDevice(
    string Name,
    string DeviceId,
    string Kind,
    string Status
);

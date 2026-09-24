using System.Reflection;
using System.Windows;

namespace Gbex.FrontDesk.Agent.Windows;

public partial class App : Application
{
    public static string AgentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}

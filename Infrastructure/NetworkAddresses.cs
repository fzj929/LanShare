using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanShare.Infrastructure;

public static class NetworkAddresses
{
    public static string[] GetLocal(int port)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Where(a => !a.StartsWith("127.") && !a.StartsWith("169.254.") && !a.StartsWith("198.18.") && !a.StartsWith("198.19."))
                .Distinct().Select(ip => $"http://{ip}:{port}").ToArray();
        }
        catch { return []; }
    }
}

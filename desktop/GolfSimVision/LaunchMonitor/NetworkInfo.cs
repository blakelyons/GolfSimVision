using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GolfSimVision.LaunchMonitor;

/// <summary>
/// Auto-detects the LAN IP for display in the UI -- ADR-0002's explicit fix for V1's
/// single worst field failure ("Blake typing 192.168.5.90 when the PC was on
/// 192.168.4.90"). The installer firewall rule this same ADR calls for is a packaging
/// concern this project has no installer for yet (ADR-0008's code signing/self-update
/// work is the same category, explicitly deferred to pre-paid-release) -- out of scope
/// here, not forgotten.
/// </summary>
public static class NetworkInfo
{
    public static string? GetLocalIPv4()
    {
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToList();

        foreach (var ni in active)
        {
            var address = ni.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (address is not null) return address.Address.ToString();
        }
        return null;
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace HotPotatoLauncher.Networking
{
    public static class UpnpManager
    {
        public static async Task<bool> TryOpenPortAsync(int port)
        {
            try
            {
                var discoverer = new NatDiscoverer();
                var cts = new CancellationTokenSource(5000); // 5 second timeout
                var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);

                await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, "Hot Potato Server"));
                return true;
            }
            catch
            {
                return false; // UPnP failed or not supported
            }
        }

        public static async Task<string> GetPublicIpAsync()
        {
            try
            {
                var discoverer = new NatDiscoverer();
                var cts = new CancellationTokenSource(5000);
                var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
                var ip = await device.GetExternalIPAsync();
                return ip.ToString();
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }
}
#nullable enable
using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using HotPotatoLauncher.Core;

namespace HotPotatoLauncher.Networking
{
    public class NetworkFactory
    {
        private PlayitManager? _playit;
        private Action<string> _logger;

        public NetworkFactory(Action<string> logger)
        {
            _logger = logger;
        }

        public async Task<string> InitializeAsync(NetworkType mode)
        {
            // Reset previous tunnel
            if (_playit != null) { _playit.Stop(); _playit = null; }

            switch (mode)
            {
                case NetworkType.Automatic:
                    _logger("🌐 Mode: Automatic. Attempting UPnP (Port Forwarding)...");
                    if (await UpnpManager.TryOpenPortAsync(25565))
                    {
                        string ip = await UpnpManager.GetPublicIpAsync();
                        _logger($"✅ UPnP Success! Direct IP: {ip}");
                        return ip;
                    }
                    else
                    {
                        _logger("⚠️ UPnP Failed (CGNAT detected?). Fallback to Playit.gg Tunnel...");
                        return await StartPlayit();
                    }

                case NetworkType.PlayitOnly:
                    _logger("🚇 Mode: Force Tunnel.");
                    return await StartPlayit();

                case NetworkType.Radmin:
                    _logger("🛡️ Mode: Radmin VPN (Local/VPN).");
                    string radminIp = GetRadminIp();
                    if (radminIp == "127.0.0.1") _logger("❌ Could not find Radmin IP (26.x.x.x). Is Radmin turned on?");
                    else _logger($"✅ Radmin IP Found: {radminIp}");
                    return radminIp;

                default:
                    return "127.0.0.1";
            }
        }

        private string GetRadminIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // Radmin usually usually has 'Radmin' in description or an IP starting with 26.
                    var props = ni.GetIPProperties();
                    foreach (var unicast in props.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = unicast.Address.ToString();
                            if (ip.StartsWith("26.")) return ip;
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1"; // Not found
        }

        private async Task<string> StartPlayit()
        {
            _playit = new PlayitManager();
            _playit.OnLog += _logger;
            return await _playit.StartTunnelAsync();
        }

        public void Shutdown()
        {
            if (_playit != null)
            {
                _playit.Stop();
                _logger("🛑 Tunnel Closed.");
            }
        }
    }
}
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HotPotatoLauncher.Core;

namespace HotPotatoLauncher.Networking
{
    public class PlayitManager
    {
        private Process? _process;
        public event Action<string>? OnLog;

        public async Task<string> StartTunnelAsync()
        {
            string exePath = Path.Combine(AppPaths.ToolsDir, "playit.exe");
            await EnsureDownloadedAsync(exePath);

            var info = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true, // Must be true to show the window
                CreateNoWindow = false, // FIX: SHOW THE WINDOW so you can see the link!
                WindowStyle = ProcessWindowStyle.Normal
            };

            OnLog?.Invoke("⏳ Starting Playit Tunnel...");
            OnLog?.Invoke("⚠️ A black Playit console window will open.");
            OnLog?.Invoke("👉 Check that window for the 'Claim URL' or the IP address!");

            _process = Process.Start(info);

            // Since we popped out the window, we can't scrape the text automatically 
            // as easily (UseShellExecute limit), but the user is guaranteed to see it.
            // We will assume success and ask the user to verify.

            return "Check Playit Window";
        }

        public void Stop()
        {
            try { _process?.Kill(); } catch { }
        }

        private async Task EnsureDownloadedAsync(string path)
        {
            if (File.Exists(path)) return;

            OnLog?.Invoke("⬇️ Downloading Playit.gg Agent...");
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync("https://github.com/playit-cloud/playit-agent/releases/latest/download/playit-Windows-x86_64.exe");
            await File.WriteAllBytesAsync(path, bytes);
            OnLog?.Invoke("✅ Playit Installed.");
        }
    }
}
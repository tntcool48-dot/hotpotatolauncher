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
        private int? _processId; // FIX 4.4: Track PID for reliable cleanup
        public event Action<string>? OnLog;

        public async Task<string> StartTunnelAsync()
        {
            string exePath = Path.Combine(AppPaths.ToolsDir, "playit.exe");
            await EnsureDownloadedAsync(exePath);

            var info = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true, // Must be true to show the window
                CreateNoWindow = false, // Show the window so user can see the link
                WindowStyle = ProcessWindowStyle.Normal
            };

            OnLog?.Invoke("⏳ Starting Playit Tunnel...");
            OnLog?.Invoke("⚠️ A black Playit console window will open.");
            OnLog?.Invoke("👉 Check that window for the 'Claim URL' or the IP address!");

            _process = Process.Start(info);
            if (_process != null)
            {
                _processId = _process.Id;
                OnLog?.Invoke($"🔧 Playit started (PID: {_processId})");
            }

            return "Check Playit Window";
        }

        // FIX 4.4: PID-based kill for reliable cleanup
        public void Stop()
        {
            // Try graceful kill via stored reference
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    OnLog?.Invoke("🛑 Playit process terminated.");
                    return;
                }
            }
            catch { }

            // Fallback: kill by PID if reference is stale
            if (_processId.HasValue)
            {
                try
                {
                    var proc = Process.GetProcessById(_processId.Value);
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                        OnLog?.Invoke($"🛑 Playit process (PID: {_processId}) killed via fallback.");
                    }
                }
                catch { /* Process already exited or PID reused — safe to ignore */ }
            }

            _process = null;
            _processId = null;
        }

        private async Task EnsureDownloadedAsync(string path)
        {
            if (File.Exists(path)) return;

            OnLog?.Invoke("⬇️ Downloading Playit.gg Agent...");
            try
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync("https://github.com/playit-cloud/playit-agent/releases/latest/download/playit-Windows-x86_64.exe");
                await File.WriteAllBytesAsync(path, bytes);
                OnLog?.Invoke("✅ Playit Installed.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"❌ Playit download failed: {ex.Message}");
                throw;
            }
        }
    }
}
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HotPotatoLauncher.Core
{
    public class ServerManager
    {
        private readonly string _serverRoot;
        private readonly string _javaPath;
        private Process? _activeProcess;
        public event Action<string>? OnLogReceived;

        private DateTime _lastCpuTime;
        private TimeSpan _lastTotalProcessorTime;

        public ServerManager(string serverRoot, string javaPath)
        {
            _serverRoot = serverRoot;
            _javaPath = javaPath;
        }

        public static void KillZombieProcesses()
        {
            foreach (var p in Process.GetProcessesByName("java"))
            {
                try { if (p.MainModule != null && p.MainModule.FileName.StartsWith(AppPaths.BaseDir)) p.Kill(); } catch { }
            }
        }

        public async Task InjectConfigurationAsync(string displayIp, PotatoProfile profile)
        {
            Directory.CreateDirectory(_serverRoot);
            await File.WriteAllTextAsync(Path.Combine(_serverRoot, "eula.txt"), "eula=true");

            string propFile = Path.Combine(_serverRoot, "server.properties");
            var currentProps = new Dictionary<string, string>();

            if (File.Exists(propFile))
            {
                foreach (var line in await File.ReadAllLinesAsync(propFile))
                {
                    var split = line.Split('=');
                    if (split.Length == 2) currentProps[split[0].Trim()] = split[1].Trim();
                }
            }

            // --- STANDARD SETTINGS ---
            currentProps["server-ip"] = ""; // Fixes "Unknown Host" crash
            currentProps["server-port"] = "25565";
            currentProps["online-mode"] = profile.OnlineMode.ToString().ToLower();
            currentProps["white-list"] = profile.UseWhitelist.ToString().ToLower();
            currentProps["max-players"] = profile.MaxPlayers.ToString();
            currentProps["level-name"] = "world";
            currentProps["view-distance"] = profile.ViewDistance.ToString();
            currentProps["simulation-distance"] = profile.SimDistance.ToString();

            // --- NEW: WORLD GEN SETTINGS ---
            if (!string.IsNullOrWhiteSpace(profile.WorldSeed))
                currentProps["level-seed"] = profile.WorldSeed;
            else
                currentProps.Remove("level-seed"); // Remove if empty so it's random

            currentProps["gamemode"] = profile.GameMode.ToLower();
            currentProps["difficulty"] = profile.Difficulty.ToLower();
            currentProps["hardcore"] = profile.Hardcore.ToString().ToLower();

            var outLines = currentProps.Select(k => $"{k.Key}={k.Value}").ToList();
            await File.WriteAllLinesAsync(propFile, outLines);

            await GenerateSecurityJsonAsync(profile.FriendUsernames, profile.OnlineMode);
        }

        private async Task GenerateSecurityJsonAsync(List<string> users, bool onlineMode)
        {
            var whitelist = new List<object>();
            using var http = new HttpClient();
            foreach (var user in users)
            {
                string uuid = Guid.NewGuid().ToString();
                if (onlineMode)
                {
                    try
                    {
                        string json = await http.GetStringAsync($"https://api.mojang.com/users/profiles/minecraft/{user}");
                        var doc = JsonDocument.Parse(json);
                        string id = doc.RootElement.GetProperty("id").GetString()!;
                        uuid = $"{id.Substring(0, 8)}-{id.Substring(8, 4)}-{id.Substring(12, 4)}-{id.Substring(16, 4)}-{id.Substring(20)}";
                    }
                    catch { }
                }
                whitelist.Add(new { uuid, name = user });
            }
            string jsonOut = JsonSerializer.Serialize(whitelist, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(_serverRoot, "whitelist.json"), jsonOut);
        }

        public async Task StartServerProcess(int ramGb, ServerType type, string customJar, bool showConsole)
        {
            string jarName = customJar;
            if (string.IsNullOrWhiteSpace(jarName)) jarName = "server.jar";

            string args = $"-Xmx{ramGb}G -Xms{ramGb}G -jar {jarName} nogui";

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = _javaPath,
                Arguments = args,
                WorkingDirectory = _serverRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = !showConsole,
                WindowStyle = showConsole ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
            };

            _activeProcess = Process.Start(info);
            _lastCpuTime = DateTime.UtcNow;
            _lastTotalProcessorTime = _activeProcess.TotalProcessorTime;

            using var p = _activeProcess;
            p.OutputDataReceived += (s, e) => { if (e.Data != null) OnLogReceived?.Invoke(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) OnLogReceived?.Invoke(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();
            _activeProcess = null;
        }

        // --- NUCLEAR COMMAND FIX (Byte Stream) ---
        public async Task SendCommandAsync(string command)
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                string cleanCommand = command.Trim();
                byte[] commandBytes = System.Text.Encoding.UTF8.GetBytes(cleanCommand + "\n");
                await _activeProcess.StandardInput.BaseStream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await _activeProcess.StandardInput.BaseStream.FlushAsync();
            }
        }

        public (double cpu, long ram) GetResourceUsage()
        {
            try
            {
                if (_activeProcess == null || _activeProcess.HasExited) return (0, 0);

                _activeProcess.Refresh();
                long ram = _activeProcess.WorkingSet64 / 1024 / 1024;

                var now = DateTime.UtcNow;
                var currentTotalProcessorTime = _activeProcess.TotalProcessorTime;
                double cpuUsage = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds /
                                  (now - _lastCpuTime).TotalMilliseconds /
                                  Environment.ProcessorCount * 100;

                _lastCpuTime = now;
                _lastTotalProcessorTime = currentTotalProcessorTime;

                return (Math.Max(0, cpuUsage), ram);
            }
            catch { return (0, 0); }
        }

        public async Task StopServerAsync()
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                try { await SendCommandAsync("stop"); } catch { }
                try { await _activeProcess.WaitForExitAsync(new System.Threading.CancellationTokenSource(10000).Token); }
                catch { _activeProcess.Kill(); }
            }
        }
    }
}
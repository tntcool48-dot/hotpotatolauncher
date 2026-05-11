#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

        // --- FIX 1.4: Defensive zombie process killing ---
        public static void KillZombieProcesses()
        {
            foreach (var p in Process.GetProcessesByName("java"))
            {
                try
                {
                    // MainModule access throws Win32Exception for SYSTEM/Admin processes
                    if (p.MainModule != null && p.MainModule.FileName.StartsWith(AppPaths.BaseDir))
                        p.Kill();
                }
                catch (System.ComponentModel.Win32Exception) { /* Access denied — skip SYSTEM/Admin Java processes */ }
                catch (InvalidOperationException) { /* Process already exited */ }
                catch (NotSupportedException) { /* Remote process */ }
                catch { /* Any other unexpected error — skip safely */ }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
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
                    // Handle properties that may contain '=' in their value (e.g., motd)
                    int eqIndex = line.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string key = line.Substring(0, eqIndex).Trim();
                        string value = line.Substring(eqIndex + 1).Trim();
                        currentProps[key] = value;
                    }
                }
            }

            // --- STANDARD SETTINGS ---
            currentProps["server-ip"] = ""; // Fixes "Unknown Host" crash
            currentProps["server-port"] = profile.ServerPort.ToString(); // FIX 2.4: Configurable port
            currentProps["online-mode"] = profile.OnlineMode.ToString().ToLower();
            currentProps["white-list"] = profile.UseWhitelist.ToString().ToLower();
            currentProps["max-players"] = profile.MaxPlayers.ToString();
            currentProps["level-name"] = "world";
            currentProps["view-distance"] = profile.ViewDistance.ToString();
            currentProps["simulation-distance"] = profile.SimDistance.ToString();

            // --- WORLD GEN SETTINGS ---
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

        // --- FIX 2.2: Proper offline UUID generation ---
        private static string GenerateOfflineUuid(string username)
        {
            // Minecraft offline mode uses UUID v3: MD5 hash of "OfflinePlayer:" + username
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));

            // Set version to 3 (bits 4-7 of byte 6)
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            // Set variant to IETF (bits 6-7 of byte 8)
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            // Bypass C# Guid constructor to preserve Big-Endian formatting required by Java/Minecraft
            return $"{Convert.ToHexString(hash, 0, 4)}-{Convert.ToHexString(hash, 4, 2)}-{Convert.ToHexString(hash, 6, 2)}-{Convert.ToHexString(hash, 8, 2)}-{Convert.ToHexString(hash, 10, 6)}".ToLower();
        }

        // --- FIX 2.3: Merge whitelist instead of overwriting ---
        private async Task GenerateSecurityJsonAsync(List<string> users, bool onlineMode)
        {
            string whitelistPath = Path.Combine(_serverRoot, "whitelist.json");

            // Read existing whitelist entries (from in-game /whitelist add, etc.)
            var existingEntries = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(whitelistPath))
            {
                try
                {
                    string existingJson = await File.ReadAllTextAsync(whitelistPath);
                    var existingList = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(existingJson);
                    if (existingList != null)
                    {
                        foreach (var entry in existingList)
                        {
                            if (entry.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name))
                            {
                                existingEntries[name] = entry;
                            }
                        }
                    }
                }
                catch { /* Corrupt file — start fresh */ }
            }

            // Resolve UUIDs for UI-provided users (these win on conflict)
            using var http = new HttpClient();
            foreach (var user in users)
            {
                if (string.IsNullOrWhiteSpace(user)) continue;

                string uuid;
                if (onlineMode)
                {
                    try
                    {
                        string json = await http.GetStringAsync($"https://api.mojang.com/users/profiles/minecraft/{user}");
                        var doc = JsonDocument.Parse(json);
                        string id = doc.RootElement.GetProperty("id").GetString()!;
                        uuid = $"{id.Substring(0, 8)}-{id.Substring(8, 4)}-{id.Substring(12, 4)}-{id.Substring(16, 4)}-{id.Substring(20)}";
                    }
                    catch
                    {
                        // API failure fallback: use proper offline UUID instead of random GUID
                        uuid = GenerateOfflineUuid(user);
                        OnLogReceived?.Invoke($"⚠️ Could not fetch UUID for '{user}', using offline UUID.");
                    }
                }
                else
                {
                    // Offline mode: deterministic UUID
                    uuid = GenerateOfflineUuid(user);
                }

                // UI-provided users overwrite existing entries for that username
                existingEntries[user] = new { uuid, name = user };
            }

            // Write merged whitelist
            var mergedList = existingEntries.Values.ToList();
            string jsonOut = JsonSerializer.Serialize(mergedList, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(whitelistPath, jsonOut);
        }

        // --- FIX 1.2: Modern Forge run.bat support ---
        public async Task StartServerProcess(int ramGb, ServerType type, string customJar, bool showConsole, bool forceUpgrade = false)
        {
            string jarName = customJar;
            if (string.IsNullOrWhiteSpace(jarName)) jarName = "server.jar";

            // Check for modern Forge run.bat (Forge 1.17+ generates this)
            string runBatPath = Path.Combine(_serverRoot, "run.bat");
            bool useRunBat = (type == ServerType.Forge && File.Exists(runBatPath));

            ProcessStartInfo info;

            if (useRunBat)
            {
                // Modern Forge: Write RAM args to user_jvm_args.txt, then execute run.bat
                string jvmArgsPath = Path.Combine(_serverRoot, "user_jvm_args.txt");
                var jvmLines = new List<string>
                {
                    "# JVM Arguments — managed by Hot Potato Launcher",
                    $"-Xmx{ramGb}G",
                    $"-Xms{ramGb}G"
                };
                await File.WriteAllLinesAsync(jvmArgsPath, jvmLines);

                string batArgs = "nogui";
                if (forceUpgrade) batArgs += " --forceUpgrade"; // FIX 2.6

                info = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c run.bat {batArgs}",
                    WorkingDirectory = _serverRoot,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = !showConsole,
                };
            }
            else
            {
                // Legacy / Vanilla / Paper / Fabric: java -jar
                string extraArgs = forceUpgrade ? " --forceUpgrade" : ""; // FIX 2.6
                string args = $"-Xmx{ramGb}G -Xms{ramGb}G -jar \"{jarName}\" nogui{extraArgs}";

                info = new ProcessStartInfo
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
            }

            OnLogReceived?.Invoke(useRunBat ? "🔧 Launching via Forge run.bat..." : $"⚙️ Launching: {jarName}");

            _activeProcess = Process.Start(info);
            if (_activeProcess == null) throw new Exception("Failed to start server process.");

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

        // --- FIX 1.5: Defensive command sending ---
        public async Task SendCommandAsync(string command)
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                try
                {
                    string cleanCommand = command.Trim();
                    byte[] commandBytes = Encoding.UTF8.GetBytes(cleanCommand + "\n");
                    await _activeProcess.StandardInput.BaseStream.WriteAsync(commandBytes, 0, commandBytes.Length);
                    await _activeProcess.StandardInput.BaseStream.FlushAsync();
                }
                catch (InvalidOperationException) { /* Server shutting down or process disposed — stdin closed */ }
                catch (IOException) { /* Pipe broken during shutdown */ }
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
                catch { try { _activeProcess.Kill(); } catch { } }
            }
        }
    }
}
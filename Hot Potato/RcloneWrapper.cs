#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HotPotatoLauncher.Core
{
    public class RcloneWrapper
    {
        private readonly string _localProfilePath;
        private readonly string _remoteVault;
        private readonly string _profileName;

        public event Action<string>? OnLogReceived;
        public event Action<double>? OnProgressUpdate; // For Loading Bar

        // FIX 4.3: Sanitize remote vault name to prevent command injection
        private static string SanitizeRemotePath(string path)
        {
            // Strip characters that could enable shell injection
            return Regex.Replace(path, @"[;&|`$(){}!<>\""']", "");
        }

        public RcloneWrapper(string localProfilePath, string remoteVault, string profileName)
        {
            _localProfilePath = localProfilePath;
            _remoteVault = SanitizeRemotePath(remoteVault);
            _profileName = SanitizeRemotePath(profileName);
        }

        // --- Safety Check (Prevents Deletion) ---
        public async Task<bool> CheckCloudHasFilesAsync()
        {
            // We use "rclone size" to just look at the folder stats
            string remotePath = $"{_remoteVault}Profiles/{_profileName}";
            var info = new ProcessStartInfo
            {
                FileName = AppPaths.RcloneExe,
                Arguments = $"size \"{remotePath}\" --exclude \"lock.json\"", // Returns "Total objects: 0" if empty, ignores lock
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = Process.Start(info);
                if (p == null) return false;
                string output = await p.StandardOutput.ReadToEndAsync();
                string error = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                // If rclone throws an error about a missing directory, the cloud is empty.
                if (error.Contains("directory not found") || error.Contains("error reading source")) return false;

                // If output is completely empty and it errored, it's empty.
                if (string.IsNullOrWhiteSpace(output)) return false;

                return !output.Contains("Total objects: 0");
            }
            catch (Exception ex)
            {
                OnLogReceived?.Invoke($"⚠️ Cloud check failed: {ex.Message}");
                return false; // Assume empty if check fails (safest option)
            }
        }

        public async Task<List<string>> GetCloudProfilesAsync()
        {
            var profiles = new List<string>();
            var info = new ProcessStartInfo
            {
                FileName = AppPaths.RcloneExe,
                Arguments = $"lsd \"{_remoteVault}Profiles/\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = Process.Start(info);
                if (p == null) return profiles;

                while (!p.StandardOutput.EndOfStream)
                {
                    string line = await p.StandardOutput.ReadLineAsync() ?? "";
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            string name = parts.Last();
                            if (!string.IsNullOrWhiteSpace(name)) profiles.Add(name);
                        }
                    }
                }
                await p.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                OnLogReceived?.Invoke($"⚠️ Cloud profile listing failed: {ex.Message}");
            }
            return profiles;
        }

        public async Task SyncProfileDownAsync()
        {
            string remotePath = $"{_remoteVault}Profiles/{_profileName}";
            OnLogReceived?.Invoke($"⬇️ Syncing Profile '{_profileName}' from Cloud...");
            try
            {
                // Use 'copy --update' to NEVER overwrite newer local chunks with older cloud chunks.
                // This prevents the cloud from rolling back the world if an upload was missed.
                await RunRclone($"copy \"{remotePath}\" \"{_localProfilePath}\" --update --create-empty-src-dirs");
            }
            catch (Exception ex)
            {
                OnLogReceived?.Invoke($"⚠️ Cloud profile download error: {ex.Message}");
            }
        }

        public async Task SyncProfileUpAsync()
        {
            if (Directory.Exists(_localProfilePath))
            {
                string remotePath = $"{_remoteVault}Profiles/{_profileName}";
                OnLogReceived?.Invoke($"⬆️ Uploading Profile '{_profileName}'...");
                try
                {
                    await RunRclone($"copy \"{_localProfilePath}\" \"{remotePath}\" --create-empty-src-dirs");
                }
                catch (Exception ex)
                {
                    OnLogReceived?.Invoke($"⚠️ Cloud upload error: {ex.Message}");
                }
            }
        }

        // --- FIX 1.3: Merge global config instead of blind overwrite ---
        public async Task MergeGlobalConfigDownAsync()
        {
            string remoteFile = $"{_remoteVault}profiles.json";
            string tempDir = Path.Combine(Path.GetTempPath(), "HotPotato_CloudSync");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, "profiles.json");

            try
            {
                // Download cloud profiles.json to temp location
                await RunRclone($"copy \"{remoteFile}\" \"{tempDir}\"");

                if (File.Exists(tempFile))
                {
                    // Load the cloud version
                    var cloudManager = JsonSerializer.Deserialize<ProfileManager>(
                        await File.ReadAllTextAsync(tempFile));

                    if (cloudManager != null)
                    {
                        // Load current local version and merge
                        var localManager = ProfileManager.Load();
                        localManager.MergeFromCloud(cloudManager);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLogReceived?.Invoke($"⚠️ Cloud config merge error: {ex.Message}");
            }
            finally
            {
                // Cleanup temp
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        public async Task SyncGlobalConfigUpAsync()
        {
            // Pushes local profiles.json to cloud
            string localFile = Path.Combine(AppPaths.BaseDir, "profiles.json");
            string remotePath = _remoteVault;
            if (File.Exists(localFile))
            {
                try
                {
                    await RunRclone($"copy \"{localFile}\" \"{remotePath}\"");
                }
                catch (Exception ex)
                {
                    OnLogReceived?.Invoke($"⚠️ Cloud config upload error: {ex.Message}");
                }
            }
        }

        public async Task<string> ReadRemoteFileAsync(string fileName)
        {
            string remotePath = $"{_remoteVault}Profiles/{_profileName}/{fileName}";
            var info = new ProcessStartInfo
            {
                FileName = AppPaths.RcloneExe,
                Arguments = $"cat \"{remotePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = Process.Start(info);
                if (p == null) return "";
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (p.ExitCode != 0) return "";
                return output;
            }
            catch
            {
                return "";
            }
        }

        public async Task WriteRemoteFileAsync(string fileName, string content)
        {
            string remotePath = $"{_remoteVault}Profiles/{_profileName}/{fileName}";
            string tempFile = Path.GetTempFileName();

            try
            {
                await File.WriteAllTextAsync(tempFile, content);
                await RunRclone($"copyto \"{tempFile}\" \"{remotePath}\"");
            }
            catch (Exception ex)
            {
                OnLogReceived?.Invoke($"⚠️ Could not write remote file '{fileName}': {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        private async Task RunRclone(string args)
        {
            if (!File.Exists(AppPaths.RcloneExe))
            {
                throw new Exception($"rclone.exe not found at '{AppPaths.RcloneExe}'. Please download rclone and place it in the Tools folder.");
            }
            var info = new ProcessStartInfo
            {
                FileName = AppPaths.RcloneExe,
                Arguments = $"{args} -P --tpslimit 2 --transfers 2 --checkers 4 --drive-chunk-size 16M",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var p = Process.Start(info);
            if (p == null) throw new Exception("Failed to start rclone process.");

            // Regex to grab the percentage (e.g. "Transferred: ... 42%")
            var progressRegex = new Regex(@"(\d+)%");

            DataReceivedEventHandler handler = (s, e) => {
                if (e.Data != null)
                {
                    string line = e.Data.Trim();

                    // 1. Check for Progress
                    var match = progressRegex.Match(line);
                    if (match.Success && line.Contains("Transferred"))
                    {
                        if (double.TryParse(match.Groups[1].Value, out double percent))
                        {
                            OnProgressUpdate?.Invoke(percent);
                        }
                    }
                    // 2. Log regular messages (Ignore the messy progress lines)
                    else if (!string.IsNullOrWhiteSpace(line) && !line.Contains("Transferred") && !line.Contains("ETA"))
                    {
                        OnLogReceived?.Invoke($"[Cloud] {line}");
                    }
                }
            };

            p.OutputDataReceived += handler;
            p.ErrorDataReceived += handler;

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();

            if (p.ExitCode != 0) throw new Exception("Cloud Sync Error (Check Logs)");
        }
    }
}
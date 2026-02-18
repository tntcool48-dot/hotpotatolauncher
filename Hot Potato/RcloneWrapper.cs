#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public RcloneWrapper(string localProfilePath, string remoteVault, string profileName)
        {
            _localProfilePath = localProfilePath;
            _remoteVault = remoteVault;
            _profileName = profileName;
        }

        // --- NEW: Safety Check (Prevents Deletion) ---
        public async Task<bool> CheckCloudHasFilesAsync()
        {
            // We use "rclone size" to just look at the folder stats
            string remotePath = $"{_remoteVault}Profiles/{_profileName}";
            var info = new ProcessStartInfo
            {
                FileName = AppPaths.RcloneExe,
                Arguments = $"size \"{remotePath}\"", // Returns "Total objects: 0" if empty
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = Process.Start(info);
                if (p == null) return false;
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                // If it says "Total objects: 0", then it's empty.
                return !output.Contains("Total objects: 0");
            }
            catch
            {
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
            catch { }
            return profiles;
        }

        public async Task SyncProfileDownAsync()
        {
            string remotePath = $"{_remoteVault}Profiles/{_profileName}";
            OnLogReceived?.Invoke($"⬇️ Syncing Profile '{_profileName}' from Cloud...");
            try
            {
                await RunRclone($"sync \"{remotePath}\" \"{_localProfilePath}\" --create-empty-src-dirs");
            }
            catch
            {
                OnLogReceived?.Invoke($"⚠️ Cloud profile error.");
            }
        }

        public async Task SyncProfileUpAsync()
        {
            if (Directory.Exists(_localProfilePath))
            {
                string remotePath = $"{_remoteVault}Profiles/{_profileName}";
                OnLogReceived?.Invoke($"⬆️ Uploading Profile '{_profileName}'...");
                await RunRclone($"copy \"{_localProfilePath}\" \"{remotePath}\" --create-empty-src-dirs");
            }
        }

        // --- NEW: Sync Global Settings (Fixes UUID/Inventory Issues) ---
        public async Task SyncGlobalConfigDownAsync()
        {
            // Pulls profiles.json from cloud to local
            string remoteFile = $"{_remoteVault}profiles.json";
            string localFile = Path.Combine(AppPaths.BaseDir, "profiles.json");
            try
            {
                // We use "copy" not sync, so we don't delete if missing
                await RunRclone($"copy \"{remoteFile}\" \"{AppPaths.BaseDir}\"");
            }
            catch { }
        }

        public async Task SyncGlobalConfigUpAsync()
        {
            // Pushes local profiles.json to cloud
            string localFile = Path.Combine(AppPaths.BaseDir, "profiles.json");
            string remotePath = _remoteVault;
            if (File.Exists(localFile))
            {
                await RunRclone($"copy \"{localFile}\" \"{remotePath}\"");
            }
        }

        public async Task<string> ReadRemoteFileAsync(string fileName)
        {
            string remotePath = $"{_remoteVault}Profiles/{_profileName}/{fileName}";
            string tempFile = Path.GetTempFileName();
            try
            {
                await RunRclone($"copyto \"{remotePath}\" \"{tempFile}\"");
                return await File.ReadAllTextAsync(tempFile);
            }
            catch { return ""; }
        }

        public async Task WriteRemoteFileAsync(string fileName, string content)
        {
            string remotePath = $"{_remoteVault}Profiles/{_profileName}/{fileName}";
            string tempFile = Path.GetTempFileName();

            await File.WriteAllTextAsync(tempFile, content);
            await RunRclone($"copyto \"{tempFile}\" \"{remotePath}\"");

            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        private async Task RunRclone(string args)
        {
            var info = new ProcessStartInfo
            {
                FileName = AppPaths.RcloneExe,
                Arguments = $"{args} -P", // -P enables progress stats
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var p = Process.Start(info);

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
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace HotPotatoLauncher.Core
{
    public static class DependencyManager
    {
        public static async Task EnsureDependenciesAsync(Action<string> logCallback)
        {
            try
            {
                if (!Directory.Exists(AppPaths.ToolsDir)) Directory.CreateDirectory(AppPaths.ToolsDir);
                
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "HotPotatoLauncher");

                // 1. Rclone
                if (!File.Exists(AppPaths.RcloneExe))
                {
                    logCallback?.Invoke("⏳ Downloading rclone.exe...");
                    string tempZip = Path.Combine(Path.GetTempPath(), "rclone.zip");
                    string tempExt = Path.Combine(Path.GetTempPath(), "rclone_ext");
                    var bytes = await client.GetByteArrayAsync("https://downloads.rclone.org/rclone-current-windows-amd64.zip");
                    await File.WriteAllBytesAsync(tempZip, bytes);
                    if (Directory.Exists(tempExt)) Directory.Delete(tempExt, true);
                    ZipFile.ExtractToDirectory(tempZip, tempExt);
                    string? exe = Directory.GetFiles(tempExt, "rclone.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (exe != null) File.Copy(exe, AppPaths.RcloneExe, true);
                    try { File.Delete(tempZip); Directory.Delete(tempExt, true); } catch { }
                    logCallback?.Invoke("✅ rclone.exe installed.");
                }

                string installersDir = Path.Combine(AppPaths.ToolsDir, "Installers");
                if (!Directory.Exists(installersDir)) Directory.CreateDirectory(installersDir);

                // 2. Paper (Dynamic)
                string paperPath = Path.Combine(installersDir, "default_paper.jar");
                if (!File.Exists(paperPath) || new FileInfo(paperPath).Length < 1024 * 1024)
                {
                    logCallback?.Invoke("⏳ Fetching latest Paper version...");
                    try {
                        string verJson = await client.GetStringAsync("https://api.papermc.io/v2/projects/paper");
                        using var doc1 = JsonDocument.Parse(verJson);
                        var versions = doc1.RootElement.GetProperty("versions");
                        string? latestVer = versions[versions.GetArrayLength() - 1].GetString();

                        string buildJson = await client.GetStringAsync($"https://api.papermc.io/v2/projects/paper/versions/{latestVer}");
                        using var doc2 = JsonDocument.Parse(buildJson);
                        var builds = doc2.RootElement.GetProperty("builds");
                        int latestBuild = builds[builds.GetArrayLength() - 1].GetInt32();

                        logCallback?.Invoke($"⏳ Downloading Paper {latestVer} (Build {latestBuild})...");
                        byte[] jar = await client.GetByteArrayAsync($"https://api.papermc.io/v2/projects/paper/versions/{latestVer}/builds/{latestBuild}/downloads/paper-{latestVer}-{latestBuild}.jar");
                        await File.WriteAllBytesAsync(paperPath, jar);
                        logCallback?.Invoke("✅ default_paper.jar downloaded.");
                    } catch { logCallback?.Invoke("⚠️ Failed to download Paper."); }
                }

                // 3. Fabric (Dynamic)
                string fabricPath = Path.Combine(installersDir, "default_fabric.jar");
                if (!File.Exists(fabricPath) || new FileInfo(fabricPath).Length < 1024 * 1024)
                {
                    logCallback?.Invoke("⏳ Fetching latest Fabric installer...");
                    try {
                        string fabJson = await client.GetStringAsync("https://meta.fabricmc.net/v2/versions/installer");
                        using var doc = JsonDocument.Parse(fabJson);
                        string? installerUrl = doc.RootElement[0].GetProperty("url").GetString();
                        byte[] jar = await client.GetByteArrayAsync(installerUrl!);
                        await File.WriteAllBytesAsync(fabricPath, jar);
                        logCallback?.Invoke("✅ default_fabric.jar downloaded.");
                    } catch { logCallback?.Invoke("⚠️ Failed to download Fabric."); }
                }

                // 4. Forge (Fallback Hardcoded)
                string forgePath = Path.Combine(installersDir, "default_forge.jar");
                if (!File.Exists(forgePath) || new FileInfo(forgePath).Length < 1024 * 1024)
                {
                    logCallback?.Invoke("⏳ Downloading Forge installer...");
                    try {
                        // Using a highly compatible modern forge version statically to avoid complex json parsing
                        string forgeUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.4-49.0.31/forge-1.20.4-49.0.31-installer.jar";
                        byte[] jar = await client.GetByteArrayAsync(forgeUrl);
                        await File.WriteAllBytesAsync(forgePath, jar);
                        logCallback?.Invoke("✅ default_forge.jar downloaded.");
                    } catch {
                        logCallback?.Invoke("⚠️ Failed to download Forge installer.");
                    }
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Failed to ensure dependencies: {ex.Message}");
            }
        }
    }
}

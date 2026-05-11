using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
                // Ensure Tools directory exists
                if (!Directory.Exists(AppPaths.ToolsDir))
                {
                    Directory.CreateDirectory(AppPaths.ToolsDir);
                }

                // 1. Download rclone if missing
                if (!File.Exists(AppPaths.RcloneExe))
                {
                    logCallback?.Invoke("⏳ Downloading rclone.exe (Cloud Sync Engine)...");
                    string rcloneZipUrl = "https://downloads.rclone.org/rclone-current-windows-amd64.zip";
                    string tempZipPath = Path.Combine(Path.GetTempPath(), "rclone.zip");
                    string tempExtractPath = Path.Combine(Path.GetTempPath(), "rclone_extracted");

                    using (var client = new HttpClient())
                    {
                        var bytes = await client.GetByteArrayAsync(rcloneZipUrl);
                        await File.WriteAllBytesAsync(tempZipPath, bytes);
                    }

                    if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
                    ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

                    // Find the extracted rclone.exe (it's inside a subfolder like rclone-v1.xx-windows-amd64)
                    string? extractedExe = Directory.GetFiles(tempExtractPath, "rclone.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (extractedExe != null)
                    {
                        File.Copy(extractedExe, AppPaths.RcloneExe, true);
                        logCallback?.Invoke("✅ rclone.exe installed successfully.");
                    }

                    // Cleanup
                    try { File.Delete(tempZipPath); } catch { }
                    try { Directory.Delete(tempExtractPath, true); } catch { }
                }

                // 2. Ensure Installers directory exists and create dummy jars if missing
                string installersDir = Path.Combine(AppPaths.ToolsDir, "Installers");
                if (!Directory.Exists(installersDir))
                {
                    Directory.CreateDirectory(installersDir);
                }

                string paperPath = Path.Combine(installersDir, "default_paper.jar");
                if (!File.Exists(paperPath))
                {
                    logCallback?.Invoke("⏳ Downloading default Paper 1.20.4 jar...");
                    try 
                    {
                        using var client = new HttpClient();
                        // Official PaperMC 1.20.4 direct download build 496
                        byte[] paperJar = await client.GetByteArrayAsync("https://api.papermc.io/v2/projects/paper/versions/1.20.4/builds/496/downloads/paper-1.20.4-496.jar");
                        await File.WriteAllBytesAsync(paperPath, paperJar);
                        logCallback?.Invoke("✅ default_paper.jar downloaded.");
                    }
                    catch { logCallback?.Invoke("⚠️ Failed to download Paper jar."); }
                }

                // Leave forge and fabric as text files for now
                string forgePath = Path.Combine(installersDir, "default_forge.jar");
                if (!File.Exists(forgePath))
                {
                    logCallback?.Invoke($"⏳ Creating dummy default_forge.jar...");
                    await File.WriteAllTextAsync(forgePath, "This is a dummy jar to prevent missing file crashes during the first run. Please download the real jar.");
                    logCallback?.Invoke($"✅ default_forge.jar created.");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Failed to ensure dependencies: {ex.Message}");
            }
        }
    }
}

using System;
using System.IO;

namespace HotPotatoLauncher.Core
{
    public static class AppPaths
    {
        // 1. The folder where the .exe runs (Release/net8.0-windows)
        public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

        // 2. The "Tools" folder (from your screenshot)
        public static string ToolsDir => Path.Combine(BaseDir, "Tools");

        // 3. Server Data stays at the root (keeps worlds separate from tools)
        public static string ServerDataDir => Path.Combine(BaseDir, "ServerData");

        // --- TOOLS ---

        // Rclone is inside Tools
        public static string RcloneExe => Path.Combine(ToolsDir, "rclone.exe");

        // ZeroTier is inside Tools (This was the missing error)
        public static string ZeroTierCli => Path.Combine(ToolsDir, "zerotier-cli.bat");

        // Java is inside Tools
        public static string GetJavaPath(string javaFolderName)
        {
            // Looks for: Tools/java17/bin/java.exe
            string pathInTools = Path.Combine(ToolsDir, javaFolderName, "bin", "java.exe");
            if (File.Exists(pathInTools)) return pathInTools;

            // Fallback: Tools/java17/java.exe
            string pathInToolsRoot = Path.Combine(ToolsDir, javaFolderName, "java.exe");
            if (File.Exists(pathInToolsRoot)) return pathInToolsRoot;

            return "java"; // Fallback to system java
        }
    }
}
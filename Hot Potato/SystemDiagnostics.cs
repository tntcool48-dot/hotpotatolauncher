using Microsoft.VisualBasic.Devices; // This will now work because of UseWindowsForms
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;

namespace HotPotatoLauncher.Core
{
    public static class SystemDiagnostics
    {
        public static void RunPreFlightChecks(int requiredRamMb)
        {
            // 1. Port Check
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            if (listeners.Any(x => x.Port == 25565))
                throw new Exception("Port 25565 is busy! Close other servers.");

            // 2. RAM Check
            var info = new ComputerInfo(); // This will now find the class
            ulong availableMb = info.AvailablePhysicalMemory / 1024 / 1024;
            if (availableMb < (ulong)(requiredRamMb * 1.2))
                throw new Exception($"Not enough RAM! Available: {availableMb}MB. Need: {requiredRamMb * 1.2}MB");

            // 3. Disk Check
            var drive = new DriveInfo(Path.GetPathRoot(AppPaths.ServerDataDir) ?? "C:\\");
            if (drive.AvailableFreeSpace < 30L * 1024 * 1024 * 1024)
                throw new Exception("Low Disk Space! Need 30GB free.");
        }
    }
}
using Microsoft.VisualBasic.Devices; // This will now work because of UseWindowsForms
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;

namespace HotPotatoLauncher.Core
{
    public static class SystemDiagnostics
    {
        /// <summary>
        /// FIX 3.4: Relaxed constraints. Returns warnings instead of throwing for RAM.
        /// FIX 2.4: Accepts port parameter instead of hardcoded 25565.
        /// </summary>
        public static List<string> RunPreFlightChecks(int requiredRamMb, int port = 25565)
        {
            var warnings = new List<string>();

            // 1. Port Check (still a hard block — can't bind two servers to one port)
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            if (listeners.Any(x => x.Port == port))
                throw new Exception($"Port {port} is busy! Close other servers or change the port.");

            // 2. RAM Check — FIX 3.4: Relaxed to a soft warning instead of hard block
            try
            {
                var info = new ComputerInfo();
                ulong availableMb = info.AvailablePhysicalMemory / 1024 / 1024;
                if (availableMb < (ulong)requiredRamMb)
                {
                    warnings.Add($"⚠️ Low RAM: Available {availableMb}MB, Requested {requiredRamMb}MB. Windows may use pagefile (slower).");
                }
            }
            catch { /* Can't read RAM info — skip check */ }

            // 3. Disk Check — FIX 3.4: Reduced from 30GB to 2GB
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(AppPaths.ServerDataDir) ?? "C:\\");
                if (drive.AvailableFreeSpace < 2L * 1024 * 1024 * 1024)
                    throw new Exception("Low Disk Space! Need at least 2GB free.");
            }
            catch (Exception ex) when (ex.Message.Contains("Disk"))
            {
                throw; // Re-throw disk space errors
            }
            catch { /* Can't read drive info — skip */ }

            return warnings;
        }
    }
}
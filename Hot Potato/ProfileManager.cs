using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotPotatoLauncher.Core
{
    // Network Modes
    public enum NetworkType
    {
        Automatic,   // Try UPnP -> Fallback to Playit
        PlayitOnly,  // Force Tunnel (CGNAT/Starlink)
        Radmin       // Legacy VPN
    }

    public enum ServerType { Vanilla, Forge, Fabric }

    public class PotatoProfile
    {
        public string ProfileName { get; set; } = "My Server";

        public string FolderName
        {
            get
            {
                string safe = ProfileName;
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                return safe.Replace(" ", "_");
            }
        }

        public bool ImportWorldMode { get; set; } = true;
        public bool IsCloudEnabled { get; set; } = true;

        // World Gen Settings
        public string WorldSeed { get; set; } = "";
        public string GameMode { get; set; } = "survival";
        public string Difficulty { get; set; } = "easy";
        public bool Hardcore { get; set; } = false;

        public string RemoteVaultName { get; set; } = "potato_vault:HotPotatoLauncher/";

        // Network
        public NetworkType NetworkMode { get; set; } = NetworkType.Automatic;
        public int ServerPort { get; set; } = 25565; // FIX 2.4: Configurable port

        public ServerType ModLoader { get; set; } = ServerType.Vanilla;
        public string JavaFolder { get; set; } = "java17";
        public string CustomJarName { get; set; } = "";

        public int AllocatedRam { get; set; } = 6;
        public int MaxPlayers { get; set; } = 10;
        public int ViewDistance { get; set; } = 10;
        public int SimDistance { get; set; } = 10;

        public bool UseWhitelist { get; set; } = false;
        public bool ShowConsole { get; set; } = false;
        public bool OnlineMode { get; set; } = true;

        // FIX 2.6: Force Upgrade toggle
        public bool ForceUpgrade { get; set; } = false;

        public List<string> FriendUsernames { get; set; } = new List<string>();
    }

    public class ProfileManager
    {
        public List<PotatoProfile> Profiles { get; set; } = new List<PotatoProfile>();
        public int LastUsedIndex { get; set; } = 0;
        public bool FirstRunCheck { get; set; } = true;
        public bool IsOmarMode { get; set; } = false;

        // FIX 3.1: ActiveProfile getter no longer mutates the collection.
        // Initialization is guaranteed in Load().
        [JsonIgnore]
        public PotatoProfile ActiveProfile
        {
            get
            {
                if (LastUsedIndex < 0 || LastUsedIndex >= Profiles.Count) LastUsedIndex = 0;
                return Profiles[LastUsedIndex];
            }
        }

        private static string ConfigPath => Path.Combine(AppPaths.BaseDir, "profiles.json");

        // FIX 3.1: Ensure Profiles list is never empty after load
        public static ProfileManager Load()
        {
            ProfileManager mgr;
            if (!File.Exists(ConfigPath))
            {
                mgr = new ProfileManager();
            }
            else
            {
                try
                {
                    mgr = JsonSerializer.Deserialize<ProfileManager>(File.ReadAllText(ConfigPath)) ?? new ProfileManager();
                }
                catch
                {
                    mgr = new ProfileManager();
                }
            }

            // Guarantee at least one profile exists (moved from ActiveProfile getter)
            if (mgr.Profiles.Count == 0)
            {
                mgr.Profiles.Add(new PotatoProfile { ProfileName = "Default" });
            }

            // Clamp index
            if (mgr.LastUsedIndex < 0 || mgr.LastUsedIndex >= mgr.Profiles.Count)
                mgr.LastUsedIndex = 0;

            mgr.Save();
            return mgr;
        }

        public void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, opts));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProfileManager] Save failed: {ex.Message}");
            }
        }

        public void AddProfile(string name)
        {
            Profiles.Add(new PotatoProfile { ProfileName = name });
            Save();
        }

        public void DeleteProfile(PotatoProfile p)
        {
            if (Profiles.Count > 1)
            {
                Profiles.Remove(p);
                LastUsedIndex = 0;
                Save();
            }
        }

        /// <summary>
        /// FIX 1.3: Merge cloud profiles into local, preserving locally-created profiles.
        /// </summary>
        public void MergeFromCloud(ProfileManager cloudManager)
        {
            foreach (var cloudProfile in cloudManager.Profiles)
            {
                bool existsLocally = Profiles.Exists(p =>
                    p.FolderName.Equals(cloudProfile.FolderName, StringComparison.OrdinalIgnoreCase));

                if (!existsLocally)
                {
                    Profiles.Add(cloudProfile);
                }
            }

            // Preserve local state — don't overwrite IsOmarMode, FirstRunCheck, LastUsedIndex
            Save();
        }
    }
}
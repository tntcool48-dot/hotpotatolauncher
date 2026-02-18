using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotPotatoLauncher.Core
{
    // NEW: The Hybrid Network Modes
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

        // --- NEW WORLD GEN SETTINGS ---
        public string WorldSeed { get; set; } = "";
        public string GameMode { get; set; } = "survival";
        public string Difficulty { get; set; } = "easy";
        public bool Hardcore { get; set; } = false;

        public string RemoteVaultName { get; set; } = "potato_vault:HotPotatoLauncher/";

        // NEW: Updated Network Mode default
        public NetworkType NetworkMode { get; set; } = NetworkType.Automatic;

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
        public bool IsTreeSaverEnabled { get; set; } = false;

        public List<string> FriendUsernames { get; set; } = new List<string>();
    }

    public class ProfileManager
    {
        public List<PotatoProfile> Profiles { get; set; } = new List<PotatoProfile>();
        public int LastUsedIndex { get; set; } = 0;
        public bool FirstRunCheck { get; set; } = true;
        public bool IsOmarMode { get; set; } = false;

        [JsonIgnore]
        public PotatoProfile ActiveProfile
        {
            get
            {
                if (Profiles.Count == 0) Profiles.Add(new PotatoProfile { ProfileName = "Default" });
                if (LastUsedIndex < 0 || LastUsedIndex >= Profiles.Count) LastUsedIndex = 0;
                return Profiles[LastUsedIndex];
            }
        }

        private static string ConfigPath => Path.Combine(AppPaths.BaseDir, "profiles.json");

        public static ProfileManager Load()
        {
            if (!File.Exists(ConfigPath))
            {
                var mgr = new ProfileManager();
                mgr.Profiles.Add(new PotatoProfile());
                mgr.Save();
                return mgr;
            }
            try { return JsonSerializer.Deserialize<ProfileManager>(File.ReadAllText(ConfigPath)) ?? new ProfileManager(); }
            catch { return new ProfileManager(); }
        }

        public void Save()
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, opts));
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
    }
}
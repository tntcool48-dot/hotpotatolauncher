#nullable enable
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace HotPotatoLauncher.Core
{
    public class LockInfo
    {
        public string User { get; set; } = "";
        public string Ip { get; set; } = "";
        public DateTime Time { get; set; }
    }

    public class LockManager
    {
        private readonly RcloneWrapper _rclone;
        private readonly string _user;
        private readonly string _ip;
        private const string LockFile = "lock.json";

        public LockManager(RcloneWrapper rclone, string user, string ip)
        {
            _rclone = rclone;
            _user = user;
            _ip = ip;
        }

        public async Task<bool> CanAcquireLockAsync()
        {
            try
            {
                string json = await _rclone.ReadRemoteFileAsync(LockFile);
                if (string.IsNullOrWhiteSpace(json)) return true; // No lock exists

                var lockInfo = JsonSerializer.Deserialize<LockInfo>(json);
                if (lockInfo == null) return true;

                // Check if it's me
                if (lockInfo.User == _user) return true;

                // Check if lock is stale (older than 6 hours)
                if ((DateTime.UtcNow - lockInfo.Time).TotalHours > 6) return true;

                return false; // Locked by someone else
            }
            catch
            {
                return true; // Assume safe if check fails (e.g., file not found)
            }
        }

        public async Task AcquireLockAsync()
        {
            var info = new LockInfo
            {
                User = _user,
                Ip = _ip,
                Time = DateTime.UtcNow
            };
            string json = JsonSerializer.Serialize(info);
            await _rclone.WriteRemoteFileAsync(LockFile, json);
        }

        public async Task ReleaseLockAsync()
        {
            // We overwrite the lock with empty or delete it. 
            // Writing empty is safer than deleting in some rclone contexts.
            await _rclone.WriteRemoteFileAsync(LockFile, "");
        }

        public async Task ForceUnlockAsync()
        {
            await ReleaseLockAsync();
        }
    }
}
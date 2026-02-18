#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Collections.Generic;
using HotPotatoLauncher.Core;
using HotPotatoLauncher.Networking;

// Fix Ambiguity and References
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using RadioButton = System.Windows.Controls.RadioButton;

namespace HotPotatoLauncher
{
    public partial class MainWindow : Window
    {
        private NetworkFactory? _netFactory;
        private ProfileManager _profileMgr;
        private ServerManager? _serverMgr;
        private RcloneWrapper? _rclone;
        private LockManager? _lockMgr;
        private DispatcherTimer _resourceTimer;
        private Stopwatch _uptimeStopwatch = new Stopwatch();

        // Troll Variables
        private int _secretClickCount = 0;

        public MainWindow()
        {
            InitializeComponent();
            _profileMgr = ProfileManager.Load();

            // --- OMAR PROTOCOL: THE QUESTION ---
            if (_profileMgr.FirstRunCheck)
            {
                var result = MessageBox.Show(
                    "IDENTITY VERIFICATION REQUIRED\n\nAre you Omar (GamerTag: Wingding)?",
                    "Security Check",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _profileMgr.IsOmarMode = true;
                    MessageBox.Show("Security Level: RESTRICTED.\n\nConfig access has been disabled for your safety, Wingding.", "Access Denied");
                }
                else
                {
                    _profileMgr.IsOmarMode = false;
                }

                _profileMgr.FirstRunCheck = false;
                _profileMgr.Save();
            }
            // ------------------------------------

            ComboProfiles.ItemsSource = _profileMgr.Profiles;
            ComboProfiles.SelectedIndex = _profileMgr.LastUsedIndex;

            // _resourceTimer setup
            _resourceTimer = new DispatcherTimer();
            _resourceTimer.Interval = TimeSpan.FromSeconds(2);
            _resourceTimer.Tick += UpdateResources;

            InitializeEngines();
        }

        private void InitializeEngines()
        {
            var p = _profileMgr.ActiveProfile;
            p.IsTreeSaverEnabled = false;
            _profileMgr.Save();
            string serverPath = Path.Combine(AppPaths.ServerDataDir, p.FolderName);
            Directory.CreateDirectory(serverPath);

            // --- NETWORKING INIT ---
            _netFactory = new NetworkFactory(Log);

            // Cloud Wrapper
            _rclone = new RcloneWrapper(serverPath, p.RemoteVaultName, p.FolderName);
            _rclone.OnLogReceived += Log;
            _rclone.OnProgressUpdate += (percent) =>
            {
                Dispatcher.Invoke(() => {
                    if (MainProgressBar != null)
                    {
                        MainProgressBar.IsIndeterminate = false;
                        MainProgressBar.Value = percent;
                    }
                });
            };

            // Lock Manager (We use a placeholder IP until networking starts)
            _lockMgr = new LockManager(_rclone, Environment.UserName, "0.0.0.0");

            // =========================================================
            // UI STATE SYNC (Merged from Advanced Window)
            // =========================================================
            UpdateTreeSaverUi();

            // Toggles
            if (ChkOnline != null) ChkOnline.IsChecked = p.OnlineMode;
            if (ChkCloud != null) ChkCloud.IsChecked = p.IsCloudEnabled;
            if (ChkWhitelist != null) ChkWhitelist.IsChecked = p.UseWhitelist;
            if (ChkConsole != null) ChkConsole.IsChecked = p.ShowConsole;

            // Radios
            if (RadioImportWorld != null) RadioImportWorld.IsChecked = p.ImportWorldMode;
            if (RadioNewWorld != null) RadioNewWorld.IsChecked = !p.ImportWorldMode;

            // Sliders
            if (RamSlider != null) RamSlider.Value = p.AllocatedRam > 0 ? p.AllocatedRam : 6;
            if (SldMaxPlayers != null) SldMaxPlayers.Value = p.MaxPlayers > 0 ? p.MaxPlayers : 10;
            if (SldViewDist != null) SldViewDist.Value = p.ViewDistance > 0 ? p.ViewDistance : 10;
            if (SldSimDist != null) SldSimDist.Value = p.SimDistance > 0 ? p.SimDistance : 10;

            // TextBoxes
            if (TxtWhitelist != null) TxtWhitelist.Text = string.Join(Environment.NewLine, p.FriendUsernames);

            // Combos
            if (ComboJava != null) ComboJava.SelectedIndex = (p.JavaFolder == "java8") ? 1 : 0;
            if (ComboType != null) ComboType.SelectedIndex = (int)p.ModLoader;
            // --- LOAD WORLD GEN SETTINGS (Paste inside InitializeEngines) ---
            if (TxtSeed != null) TxtSeed.Text = p.WorldSeed;
            if (ChkHardcore != null) ChkHardcore.IsChecked = p.Hardcore;

            if (ComboGameMode != null)
            {
                foreach (ComboBoxItem item in ComboGameMode.Items)
                    if (item.Tag.ToString() == p.GameMode) ComboGameMode.SelectedItem = item;
            }

            if (ComboDifficulty != null)
            {
                foreach (ComboBoxItem item in ComboDifficulty.Items)
                    if (item.Tag.ToString() == p.Difficulty) ComboDifficulty.SelectedItem = item;
            }

            if (ComboNet != null)
            {
                ComboNet.ItemsSource = Enum.GetValues(typeof(NetworkType));
                ComboNet.SelectedItem = p.NetworkMode;
            }

            RefreshJarList(serverPath);
        }

        private void RefreshJarList(string path)
        {
            if (ComboJars == null) return;
            ComboJars.Items.Clear();
            ComboJars.Items.Add("Auto-Detect / Default");

            if (Directory.Exists(path))
            {
                var jars = Directory.GetFiles(path, "*.jar")
                                    .Select(Path.GetFileName)
                                    .Where(n => !n.Contains("installer"))
                                    .OrderBy(n => n);
                foreach (var jar in jars) ComboJars.Items.Add(jar);
            }

            if (!string.IsNullOrEmpty(_profileMgr.ActiveProfile.CustomJarName) && ComboJars.Items.Contains(_profileMgr.ActiveProfile.CustomJarName))
                ComboJars.SelectedItem = _profileMgr.ActiveProfile.CustomJarName;
            else
                ComboJars.SelectedIndex = 0;
        }

        // =========================================================
        // TAB LOGIC & OMAR LOCK
        // =========================================================
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tabName)
            {
                // Hide all tabs first
                if (TabHome != null) TabHome.Visibility = Visibility.Collapsed;
                if (TabGameplay != null) TabGameplay.Visibility = Visibility.Collapsed;
                if (TabWorld != null) TabWorld.Visibility = Visibility.Collapsed;
                if (TabSystem != null) TabSystem.Visibility = Visibility.Collapsed;

                // Show the selected tab
                var target = this.FindName(tabName) as FrameworkElement;
                if (target != null) target.Visibility = Visibility.Visible;
            }
        }

        private void TabSystem_Click(object sender, RoutedEventArgs e)
        {
            if (_profileMgr.IsOmarMode)
            {
                MessageBox.Show("🔒 SYSTEM ACCESS RESTRICTED\n\nHardware configurations are locked.", "Omar Protocol");
                // Force return to Dashboard
                if (TabSystemBtn != null) TabSystemBtn.IsChecked = false;
                if (TabHomeBtn != null) TabHomeBtn.IsChecked = true;
            }
        }

        // =========================================================
        // CLOUD FETCH (Sync Profiles)
        // =========================================================
        private async void BtnFetchProfiles_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            btn.Content = "⏳";
            btn.IsEnabled = false;

            try
            {
                Log("☁️ Searching for profiles in HotPotatoLauncher folder...");

                if (_rclone == null)
                    _rclone = new RcloneWrapper("", "potato_vault:HotPotatoLauncher/", "");

                var cloudProfiles = await _rclone.GetCloudProfilesAsync();

                if (cloudProfiles.Count == 0)
                {
                    MessageBox.Show("No profiles found on the cloud yet!", "Cloud Search");
                }
                else
                {
                    int addedCount = 0;
                    foreach (string folderName in cloudProfiles)
                    {
                        bool exists = _profileMgr.Profiles.Any(p => p.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase));

                        if (!exists)
                        {
                            var newProfile = new PotatoProfile
                            {
                                ProfileName = folderName,
                                IsCloudEnabled = true,
                                ImportWorldMode = true
                            };
                            _profileMgr.Profiles.Add(newProfile);
                            addedCount++;
                            Log($"✨ Found & Added: {folderName}");
                        }
                    }

                    if (addedCount > 0)
                    {
                        _profileMgr.Save();
                        ComboProfiles.Items.Refresh();
                        MessageBox.Show($"Success! Found {addedCount} new profiles.", "Cloud Sync");
                    }
                    else
                    {
                        Log("☁️ All cloud profiles are already in your list.");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching cloud: {ex.Message}");
            }
            finally
            {
                btn.Content = "☁️ Sync Cloud Profiles";
                btn.IsEnabled = true;
            }
        }

        // --- OMAR PROTOCOL: THE UNLOCK ---
        private void LblTitle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 1)
            {
                _secretClickCount++;
                if (_secretClickCount >= 5)
                {
                    _profileMgr.IsOmarMode = !_profileMgr.IsOmarMode;
                    _profileMgr.Save();
                    _secretClickCount = 0;

                    if (_profileMgr.IsOmarMode) MessageBox.Show("🔒 OMAR PROTOCOL: ENGAGED.\nConfig is now locked.", "Troll Mode Active");
                    else MessageBox.Show("🔓 ADMIN OVERRIDE.\nConfig is now accessible.", "Access Granted");
                }
            }
        }

        // =========================================================
        // CORE HOSTING LOGIC
        // =========================================================
        private async Task RunHostingLogic(int ram)
        {
            try
            {
                var p = _profileMgr.ActiveProfile;

                // --- 0. PREVENTION LOGIC ---
                string javaVer = p.JavaFolder;
                string jarName = p.CustomJarName.ToLower();

                if (string.IsNullOrEmpty(jarName))
                {
                    var potentialJar = Directory.GetFiles(Path.Combine(AppPaths.ServerDataDir, p.FolderName), "*.jar")
                                                .FirstOrDefault(j => !j.Contains("installer") && !j.Contains("default"));
                    if (potentialJar != null) jarName = Path.GetFileName(potentialJar).ToLower();
                }

                if ((jarName.Contains("1.18") || jarName.Contains("1.19") || jarName.Contains("1.20") || jarName.Contains("1.21")) && javaVer == "java8")
                {
                    MessageBox.Show("❌ VERSION MISMATCH!\nModern Server (1.18+) needs Java 17.", "Prevention Warning");
                    return;
                }
                if ((jarName.Contains("1.12") || jarName.Contains("1.7.10") || jarName.Contains("1.6.4") || jarName.Contains("1.16.5")) && javaVer == "java17")
                {
                    if (MessageBox.Show("⚠️ POTENTIAL MISMATCH\nOld Server (1.16.5-) usually needs Java 8.\nContinue anyway?", "Prevention Warning", MessageBoxButton.YesNo) == MessageBoxResult.No) return;
                }

                // --- 1. UI & SETUP ---
                BtnHost.IsEnabled = false;
                BtnStop.IsEnabled = true;
                if (BtnUnlock != null) BtnUnlock.Visibility = Visibility.Collapsed; // Hide unlock on start
                _resourceTimer.Start();

                p.AllocatedRam = ram;
                _profileMgr.Save();

                string serverPath = Path.Combine(AppPaths.ServerDataDir, p.FolderName);
                Directory.CreateDirectory(serverPath);

                Log($"🔍 Profile: {p.ProfileName}");
                Log($"☁️ Cloud Mode: {(p.IsCloudEnabled ? "ENABLED" : "DISABLED")}");

                // --- 2. AUTO-INSTALL DEFAULTS ---
                if (!Directory.GetFiles(serverPath, "*.jar").Any())
                {
                    Log("✨ New Profile. Installing defaults...");
                    string installersDir = Path.Combine(AppPaths.ToolsDir, "Installers");

                    if (Directory.Exists(installersDir))
                    {
                        foreach (var file in Directory.GetFiles(installersDir))
                        {
                            string fileName = Path.GetFileName(file).ToLower();
                            string destName = fileName.Contains("forge") ? "installer_forge.jar" : fileName.Replace("default_", "server_");

                            File.Copy(file, Path.Combine(serverPath, destName), true);
                            Log($"📦 Copied: {destName}");
                        }
                    }
                }

                // --- 3. PRE-FLIGHT & NETWORKING ---
                SystemDiagnostics.RunPreFlightChecks(ram * 1024);
                ServerManager.KillZombieProcesses();

                string myIp = "127.0.0.1";

                if (_netFactory != null)
                {
                    myIp = await _netFactory.InitializeAsync(p.NetworkMode);
                    Dispatcher.Invoke(() => { if (TxtZtIp != null) TxtZtIp.Text = $"IP: {myIp}"; });
                }

                if (_rclone != null) _lockMgr = new LockManager(_rclone, Environment.UserName, myIp);

                // --- 4. CLOUD SYNC ---
                if (p.IsCloudEnabled)
                {
                    Log("☁️ Connecting to Cloud...");
                    if (_lockMgr == null || !await _lockMgr.CanAcquireLockAsync())
                    {
                        MessageBox.Show("Profile is LOCKED by another user (or a crash).\n\nIf you are sure nobody is online, use the 'Force Unlock' button that just appeared in the sidebar.", "Locked");
                        if (BtnUnlock != null) BtnUnlock.Visibility = Visibility.Visible; // Show unlock button
                        return;
                    }
                    await _lockMgr.AcquireLockAsync();

                    bool cloudHasData = false;
                    if (_rclone != null) cloudHasData = await _rclone.CheckCloudHasFilesAsync();

                    if (!cloudHasData)
                    {
                        Log("☁️ Cloud is empty. Uploading local files...");
                        if (_rclone != null)
                        {
                            await _rclone.SyncProfileUpAsync();
                            await _rclone.SyncGlobalConfigUpAsync();
                        }
                    }
                    else
                    {
                        Log("☁️ Downloading from Cloud...");
                        if (_rclone != null)
                        {
                            await _rclone.SyncProfileDownAsync();
                            await _rclone.SyncGlobalConfigDownAsync();

                            Log("🔄 Reloading Settings...");
                            var oldIndex = _profileMgr.LastUsedIndex;
                            _profileMgr = ProfileManager.Load();
                            _profileMgr.LastUsedIndex = oldIndex;
                            p = _profileMgr.ActiveProfile;

                            ComboProfiles.ItemsSource = _profileMgr.Profiles;
                            ComboProfiles.SelectedIndex = _profileMgr.LastUsedIndex;
                        }
                    }
                }

                // --- 5. IMPORT CHECK (SMART RENAME) ---
                if (p.ImportWorldMode)
                {
                    string defaultWorldPath = Path.Combine(serverPath, "world");
                    bool isDefaultValid = Directory.Exists(defaultWorldPath) && File.Exists(Path.Combine(defaultWorldPath, "level.dat"));

                    if (!isDefaultValid)
                    {
                        var foundWorld = Directory.GetDirectories(serverPath)
                                                  .FirstOrDefault(d => File.Exists(Path.Combine(d, "level.dat")));

                        if (foundWorld != null)
                        {
                            string foundName = Path.GetFileName(foundWorld);
                            Log($"📂 Found world folder: '{foundName}'");

                            try
                            {
                                if (Directory.Exists(defaultWorldPath)) Directory.Delete(defaultWorldPath, true);
                                Directory.Move(foundWorld, defaultWorldPath);
                                Log("✨ Renamed to 'world' for server compatibility.");
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Could not rename world folder: {ex.Message}");
                                return;
                            }
                        }
                        else
                        {
                            var result = MessageBox.Show("🛑 Missing World Files!\n\nI looked for 'level.dat' but found nothing.\n\nOpen folder to paste your world?", "Missing World", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                            if (result == MessageBoxResult.Yes) Process.Start("explorer.exe", serverPath);
                            throw new Exception("Launch Aborted: Missing World Files");
                        }
                    }
                }

                // --- 6. IDENTIFY JAR ---
                string java = AppPaths.GetJavaPath(p.JavaFolder);
                string jarToRun = p.CustomJarName;

                if (string.IsNullOrEmpty(jarToRun))
                {
                    var jars = Directory.GetFiles(serverPath, "*.jar");
                    IEnumerable<string> validJars = jars;

                    if (p.ModLoader == ServerType.Forge) validJars = jars.Where(j => j.ToLower().Contains("forge"));
                    else if (p.ModLoader == ServerType.Vanilla) validJars = jars.Where(j => !j.ToLower().Contains("forge") && !j.ToLower().Contains("fabric"));

                    var bestJar = validJars.Where(j => !j.ToLower().Contains("installer") && !j.ToLower().Contains("default"))
                                           .OrderByDescending(j => File.GetLastWriteTime(j))
                                           .FirstOrDefault();

                    if (bestJar == null && p.ModLoader == ServerType.Forge) bestJar = validJars.FirstOrDefault();
                    if (bestJar != null) jarToRun = Path.GetFileName(bestJar);
                }

                // --- 7. FORGE AUTO-INSTALLER ---
                bool isInstallerName = jarToRun != null && (jarToRun.ToLower().Contains("installer") || jarToRun.ToLower().Contains("default"));
                bool librariesMissing = (p.ModLoader == ServerType.Forge && !Directory.Exists(Path.Combine(serverPath, "libraries")));

                if (librariesMissing || (p.ModLoader == ServerType.Forge && isInstallerName))
                {
                    Log("🛠️ Running Forge Installer...");
                    if (!isInstallerName)
                    {
                        var installer = Directory.GetFiles(serverPath, "*installer*.jar").FirstOrDefault()
                                     ?? Directory.GetFiles(serverPath, "*default*forge*.jar").FirstOrDefault();
                        if (installer != null) jarToRun = Path.GetFileName(installer);
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = java,
                        Arguments = $"-jar \"{jarToRun}\" --installServer",
                        WorkingDirectory = serverPath,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var proc = Process.Start(startInfo);
                    if (proc != null)
                    {
                        proc.OutputDataReceived += (s, e) => { if (e.Data != null && !string.IsNullOrWhiteSpace(e.Data)) Log($"[Forge] {e.Data}"); };
                        proc.BeginOutputReadLine();
                        await proc.WaitForExitAsync();
                        Log("✅ Forge Installed.");
                    }

                    var newServerJar = Directory.GetFiles(serverPath, "*.jar")
                                                .Where(j => j.ToLower().Contains("forge") && !j.ToLower().Contains("installer") && !j.ToLower().Contains("default"))
                                                .OrderByDescending(j => File.GetLastWriteTime(j))
                                                .FirstOrDefault();

                    if (newServerJar != null)
                    {
                        jarToRun = Path.GetFileName(newServerJar);
                        p.CustomJarName = jarToRun;
                        _profileMgr.Save();
                    }
                }

                if (string.IsNullOrEmpty(jarToRun)) throw new Exception("Missing Server Jar");

                // --- 7.5 AUTO-EULA ---
                string eulaPath = Path.Combine(serverPath, "eula.txt");
                if (!File.Exists(eulaPath) || !File.ReadAllText(eulaPath).Contains("eula=true"))
                {
                    File.WriteAllText(eulaPath, "eula=true");
                }

                string propsPath = Path.Combine(serverPath, "server.properties");
                if (!File.Exists(propsPath) && !string.IsNullOrEmpty(p.WorldSeed))
                {
                    File.WriteAllText(propsPath, $"level-seed={p.WorldSeed}\nmotd=Hosted via HotPotato");
                }

                // --- 8. LAUNCH SERVER ---
                Log($"⚙️ Launching: {jarToRun}");

                _serverMgr = new ServerManager(serverPath, java);
                _serverMgr.OnLogReceived += Log;
                _serverMgr.OnLogReceived += CheckForGreenLight;
                await _serverMgr.InjectConfigurationAsync(myIp, p);

                await Task.Run(() => _serverMgr.StartServerProcess(ram, p.ModLoader, jarToRun, p.ShowConsole));

                // --- 9. CLEANUP ---
                Log("🛑 Server Stopped.");

                // Crash Detection
                if (_uptimeStopwatch.Elapsed.TotalSeconds > 0 && _uptimeStopwatch.Elapsed.TotalSeconds < 30)
                {
                    MessageBox.Show(
                        "💥 SERVER DIED TOO FAST!\n\n" +
                        "It looks like the server crashed on startup.\n" +
                        "1. Did you install incompatible mods?\n" +
                        "2. Is the Java version correct?\n" +
                        "If you just turned off the server, you can ignore this message.\n\n" +
                        "👉 ACTION: Send the 'latest.log' file to the yazan (Omar).",
                        "Startup Crash Detected", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                _netFactory?.Shutdown();

                if (p.IsCloudEnabled && _rclone != null && _lockMgr != null)
                {
                    await _rclone.SyncProfileUpAsync();
                    await _rclone.SyncGlobalConfigUpAsync();
                    await _lockMgr.ReleaseLockAsync();
                }

                if (MainProgressBar != null) Dispatcher.Invoke(() => MainProgressBar.Value = 0);
                Log("✅ Session Complete.");
            }
            catch (Exception ex)
            {
                Log($"❌ ERROR: {ex.Message}");
                if (_profileMgr.ActiveProfile.IsCloudEnabled && _lockMgr != null) await _lockMgr.ReleaseLockAsync();
            }
            finally
            {
                BtnHost.IsEnabled = true;
                BtnStop.IsEnabled = false;
                if (StatusIndicator != null) StatusIndicator.Background = Brushes.Red;
                if (LblStatus != null) LblStatus.Text = "OFFLINE";
                _resourceTimer.Stop();
                if (TxtResourceUsage != null) TxtResourceUsage.Text = "System Usage: 0%";
                _uptimeStopwatch.Reset();
                if (TxtUptime != null) TxtUptime.Text = "⏱️ 00:00:00";
            }
        }

        public async Task StartHostingSequenceAsync()
        {
            int ram = _profileMgr.ActiveProfile.AllocatedRam > 0 ? _profileMgr.ActiveProfile.AllocatedRam : 4;
            if (RamSlider != null) RamSlider.Value = ram;
            await RunHostingLogic(ram);
        }
        private void UpdateResources(object? sender, EventArgs e)
        {
            if (_serverMgr != null)
            {
                var usage = _serverMgr.GetResourceUsage();
                if (TxtResourceUsage != null)
                {
                    // FIXED: Now shows RAM usage in MB alongside CPU
                    TxtResourceUsage.Text = $"CPU: {usage.cpu:F1}%  |  RAM: {usage.ram} MB";
                }

                if (_uptimeStopwatch.IsRunning && TxtUptime != null)
                {
                    TxtUptime.Text = $"⏱️ {_uptimeStopwatch.Elapsed:hh\\:mm\\:ss}";
                }
            }
        }
        private void CheckForGreenLight(string line)
        {
            if (line.Contains("Done") || line.Contains("For help, type"))
            {
                Dispatcher.Invoke(async () => {
                    StatusIndicator.Background = Brushes.LimeGreen;
                    LblStatus.Text = "ONLINE";
                    MainProgressBar.IsIndeterminate = false;
                    _uptimeStopwatch.Restart();

                    // FIX: UI should match the forced "False" state
                    UpdateTreeSaverUi();
                });
            }
        }

        // =========================================================
        // MERGED ADVANCED SETTINGS LOGIC 
        // =========================================================

        private async void BtnTreeSaver_Click(object sender, RoutedEventArgs e)
        {
            bool newState = !_profileMgr.ActiveProfile.IsTreeSaverEnabled;
            _profileMgr.ActiveProfile.IsTreeSaverEnabled = newState;
            _profileMgr.Save();

            UpdateTreeSaverUi();

            if (_serverMgr != null)
            {
                if (newState)
                {
                    Log("🛡️ TREE SAVER: Disabling TNT, Fire & Griefing...");

                    // FIX: Using the exact commands you verified
                    await _serverMgr.SendCommandAsync("/gamerule tnt_explodes false");
                    await _serverMgr.SendCommandAsync("/gamerule mob_griefing false"); // "mob_griefing" usually lowercase in code
                    await _serverMgr.SendCommandAsync("/gamerule fire_damage false");

                    await _serverMgr.SendCommandAsync("/say [System] Tree Saver Enabled!");
                }
                else
                {
                    Log("⚠️ TREE SAVER: Re-enabling Dangers...");

                    await _serverMgr.SendCommandAsync("/gamerule tnt_explodes true");
                    await _serverMgr.SendCommandAsync("/gamerule mob_griefing true");
                    await _serverMgr.SendCommandAsync("/gamerule fire_damage true");

                    await _serverMgr.SendCommandAsync("/say [System] Tree Saver Disabled.");
                }
            }
        }

        private void UpdateTreeSaverUi()
        {
            if (BtnTreeSaver == null || TxtTreeSaverStatus == null) return;
            bool active = _profileMgr.ActiveProfile.IsTreeSaverEnabled;

            BtnTreeSaver.Content = active ? "PROTOCOL ACTIVE (SAFE)" : "ACTIVATE PROTECTION";
            BtnTreeSaver.Background = active ? Brushes.DarkGreen : (SolidColorBrush)new BrushConverter().ConvertFrom("#C62828")!;
            TxtTreeSaverStatus.Text = active ? "STATUS: 🛡️ PROTECTED" : "STATUS: ⚠️ UNPROTECTED";
        }

        // Auto-Save UI Handlers
        private void ChkOnline_Click(object sender, RoutedEventArgs e) { _profileMgr.ActiveProfile.OnlineMode = ChkOnline.IsChecked == true; _profileMgr.Save(); }
        private void ChkWhitelist_Click(object sender, RoutedEventArgs e) { _profileMgr.ActiveProfile.UseWhitelist = ChkWhitelist.IsChecked == true; _profileMgr.Save(); }
        private void ChkConsole_Click(object sender, RoutedEventArgs e) { _profileMgr.ActiveProfile.ShowConsole = ChkConsole.IsChecked == true; _profileMgr.Save(); }
        private void ChkCloud_Click(object sender, RoutedEventArgs e) { _profileMgr.ActiveProfile.IsCloudEnabled = ChkCloud.IsChecked == true; _profileMgr.Save(); }
        private void WorldMode_Changed(object sender, RoutedEventArgs e)
        {
            // Only run if the UI is fully loaded
            if (_profileMgr == null || RadioImportWorld == null) return;

            // 1. Save the new setting
            _profileMgr.ActiveProfile.ImportWorldMode = RadioImportWorld.IsChecked == true;
            _profileMgr.Save();

            // 2. FORCE THE UI UPDATE
            UpdateWorldTabUi();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_profileMgr == null) return;
            if (sender == SldMaxPlayers) _profileMgr.ActiveProfile.MaxPlayers = (int)e.NewValue;
            if (sender == SldViewDist) _profileMgr.ActiveProfile.ViewDistance = (int)e.NewValue;
            if (sender == SldSimDist) _profileMgr.ActiveProfile.SimDistance = (int)e.NewValue;
            _profileMgr.Save();
        }

        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_profileMgr == null || RamSlider == null) return;
            int ram = (int)e.NewValue;
            _profileMgr.ActiveProfile.AllocatedRam = ram;
            _profileMgr.Save();

            if (ram > 24) RamSlider.ToolTip = "🚀 NASA COMPUTER DETECTED";
            else if (ram > 16) RamSlider.ToolTip = "🛑 Overkill. Java GC might lag.";
            else RamSlider.ToolTip = "Memory Allocation";
        }

        private void TxtWhitelist_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_profileMgr != null && TxtWhitelist != null)
            {
                _profileMgr.ActiveProfile.FriendUsernames = TxtWhitelist.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                _profileMgr.Save();
            }
        }

        private void ComboType_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_profileMgr != null && ComboType != null) { _profileMgr.ActiveProfile.ModLoader = (ServerType)ComboType.SelectedIndex; _profileMgr.Save(); } }
        private void ComboNet_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_profileMgr != null && ComboNet != null && ComboNet.SelectedItem is NetworkType nt) { _profileMgr.ActiveProfile.NetworkMode = nt; _profileMgr.Save(); } }
        private void ComboJava_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_profileMgr != null && ComboJava != null && ComboJava.SelectedItem is ComboBoxItem item) { _profileMgr.ActiveProfile.JavaFolder = item.Tag.ToString(); _profileMgr.Save(); } }
        private void ComboJars_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_profileMgr != null && ComboJars != null)
            {
                if (ComboJars.SelectedIndex > 0 && ComboJars.SelectedItem != null) _profileMgr.ActiveProfile.CustomJarName = ComboJars.SelectedItem.ToString();
                else _profileMgr.ActiveProfile.CustomJarName = "";
                _profileMgr.Save();
            }
        }

        // --- HELPER FUNCTIONS ---
        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
        }

        private void OpenInstallerFolder()
        {
            string path = Path.Combine(AppPaths.ToolsDir, "Installers");
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }
        // =========================================================
        // WORLD GENERATION HANDLERS
        // =========================================================
        private void TxtSeed_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_profileMgr != null && TxtSeed != null)
            {
                _profileMgr.ActiveProfile.WorldSeed = TxtSeed.Text;
                _profileMgr.Save();
            }
        }

        private void ComboGameMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_profileMgr != null && ComboGameMode != null && ComboGameMode.SelectedItem is ComboBoxItem item)
            {
                _profileMgr.ActiveProfile.GameMode = item.Tag.ToString();
                _profileMgr.Save();
            }
        }

        private void ComboDifficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_profileMgr != null && ComboDifficulty != null && ComboDifficulty.SelectedItem is ComboBoxItem item)
            {
                _profileMgr.ActiveProfile.Difficulty = item.Tag.ToString();
                _profileMgr.Save();
            }
        }

        private void ChkHardcore_Click(object sender, RoutedEventArgs e)
        {
            if (_profileMgr != null && ChkHardcore != null)
            {
                _profileMgr.ActiveProfile.Hardcore = ChkHardcore.IsChecked == true;
                _profileMgr.Save();
            }
        }

        // --- BUTTON HANDLERS ---

        // 1. Plugins (Opens Browser + Plugins Folder)
        private void BtnGetEssentials_Click(object sender, RoutedEventArgs e) { OpenUrl("https://essentialsx.net/downloads.html"); OpenPluginsFolder(); }
        private void BtnGetWorldEdit_Click(object sender, RoutedEventArgs e) { OpenUrl("https://dev.bukkit.org/projects/worldedit/files"); OpenPluginsFolder(); }
        private void BtnGetGeyser_Click(object sender, RoutedEventArgs e) { OpenUrl("https://geysermc.org/download"); OpenPluginsFolder(); }

        private void BtnRepoSpigot_Click(object sender, RoutedEventArgs e) { OpenUrl("https://www.spigotmc.org/resources/"); OpenPluginsFolder(); }
        private void BtnRepoHangar_Click(object sender, RoutedEventArgs e) { OpenUrl("https://hangar.papermc.io/"); OpenPluginsFolder(); }
        private void BtnRepoCurse_Click(object sender, RoutedEventArgs e) { OpenUrl("https://www.curseforge.com/minecraft/bukkit-plugins"); OpenPluginsFolder(); }

        // 2. Server Jars (Opens Browser + Installers Folder)
        private void BtnGetPaper_Click(object sender, RoutedEventArgs e) { OpenUrl("https://papermc.io/downloads"); OpenInstallerFolder(); }
        private void BtnGetForge_Click(object sender, RoutedEventArgs e) { OpenUrl("https://files.minecraftforge.net/"); OpenInstallerFolder(); }
        private void BtnGetFabric_Click(object sender, RoutedEventArgs e) { OpenUrl("https://fabricmc.net/use/installer/"); OpenInstallerFolder(); }

        // 3. Direct Folder Access
        private void BtnOpenPlugins_Click(object sender, RoutedEventArgs e) => OpenPluginsFolder();

        private void BtnDelWorld_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Delete 'world' folder?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                var p = Path.Combine(AppPaths.ServerDataDir, _profileMgr.ActiveProfile.FolderName, "world");
                try { if (Directory.Exists(p)) Directory.Delete(p, true); Log("🌍 World deleted. Restart to regenerate."); } catch { }
            }
        }
        private void OpenPluginsFolder()
        {
            if (_profileMgr == null) return;
            string path = Path.Combine(AppPaths.ServerDataDir, _profileMgr.ActiveProfile.FolderName, "plugins");
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        // =========================================================
        // BASIC HANDLERS
        // =========================================================
        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e) { Process.Start("explorer.exe", Path.Combine(AppPaths.ServerDataDir, _profileMgr.ActiveProfile.FolderName)); }
        private void BtnOpenMods_Click(object sender, RoutedEventArgs e) { string p = Path.Combine(AppPaths.ServerDataDir, _profileMgr.ActiveProfile.FolderName, "mods"); Directory.CreateDirectory(p); Process.Start("explorer.exe", p); }
        private async void BtnHost_Click(object sender, RoutedEventArgs e) => await RunHostingLogic((int)RamSlider.Value);
        private async void BtnStop_Click(object sender, RoutedEventArgs e) { BtnStop.IsEnabled = false; if (_serverMgr != null) await _serverMgr.StopServerAsync(); }
        private async void BtnSend_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(TxtCommand.Text) && _serverMgr != null) { Log($"> {TxtCommand.Text}"); await _serverMgr.SendCommandAsync(TxtCommand.Text); TxtCommand.Clear(); } }
        private void TxtCommand_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) BtnSend_Click(sender, e); }

        private void BtnCopyIp_Click(object sender, RoutedEventArgs e)
        {
            if (TxtZtIp != null)
            {
                string t = TxtZtIp.Text.Replace("IP: ", "").Trim();
                if (t != "Checking...") { Clipboard.SetText(t); BtnCopyIp.Content = "✅"; Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(() => BtnCopyIp.Content = "📄")); }
            }
        }

        // EMERGENCY UNLOCK: Restored as requested
        private async void BtnUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Force unlock this profile?\n\nONLY do this if you are sure no one else is playing.\nData corruption may occur if two people host at once.", "Emergency Unlock", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (_lockMgr != null)
                {
                    await _lockMgr.ForceUnlockAsync();
                    MessageBox.Show("Profile unlocked. You may now try to start the server.");
                    if (BtnUnlock != null) BtnUnlock.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Log(string msg) => Dispatcher.Invoke(() => { if (LogBox != null) { LogBox.AppendText($"{DateTime.Now:T} {msg}\n"); LogBox.ScrollToEnd(); } });

        private void BtnAddProfile_Click(object sender, RoutedEventArgs e) { string n = Microsoft.VisualBasic.Interaction.InputBox("New Profile Name:", "Create", "My Server"); if (!string.IsNullOrWhiteSpace(n)) { _profileMgr.AddProfile(n); ComboProfiles.Items.Refresh(); ComboProfiles.SelectedIndex = _profileMgr.Profiles.Count - 1; } }
        private void BtnDelProfile_Click(object sender, RoutedEventArgs e)
        {
            var p = _profileMgr.ActiveProfile;
            if (MessageBox.Show($"Delete '{p.ProfileName}'?\nTHIS DELETES ALL FILES PERMANENTLY.", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try { Directory.Delete(Path.Combine(AppPaths.ServerDataDir, p.FolderName), true); } catch { }
                _profileMgr.DeleteProfile(p);
                ComboProfiles.Items.Refresh();
                ComboProfiles.SelectedIndex = 0;
            }
        }

        private void ComboProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ComboProfiles.SelectedIndex == -1) return; _profileMgr.LastUsedIndex = ComboProfiles.SelectedIndex; _profileMgr.Save(); InitializeEngines(); }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { ServerManager.KillZombieProcesses(); base.OnClosing(e); }
        private void BtnPasteWorld_Click(object sender, RoutedEventArgs e)
        {
            // Just re-use the existing folder opener
            BtnOpenFolder_Click(sender, e);
        }
        private void UpdateWorldTabUi()
        {
            // Safety check
            if (_profileMgr == null || PanelNewWorldOpts == null || PanelImportWorldOpts == null) return;

            bool isImport = _profileMgr.ActiveProfile.ImportWorldMode;

            // Toggle Visibilities
            if (isImport)
            {
                PanelNewWorldOpts.Visibility = Visibility.Collapsed;
                PanelImportWorldOpts.Visibility = Visibility.Visible;
            }
            else
            {
                PanelNewWorldOpts.Visibility = Visibility.Visible;
                PanelImportWorldOpts.Visibility = Visibility.Collapsed;
            }
        }
    }
}
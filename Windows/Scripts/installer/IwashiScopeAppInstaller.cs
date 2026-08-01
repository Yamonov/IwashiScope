using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using IwashiScope.Installer;
using Microsoft.Win32;

internal static class Program
{
    private const string AppName = "IwashiScope";
    private const string MainExecutableName = "IwashiScope.exe";
    private const string InstalledInstallerName = "IwashiScopeInstaller.exe";
    private const string UninstallRegistryKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\IwashiScope";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args != null && args.Length == 3 &&
                string.Equals(args[0], "--cleanup", StringComparison.OrdinalIgnoreCase))
            {
                return CleanupAfterUninstall(args[1], args[2]);
            }
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return Uninstall();
            }

            return Install();
        }
        catch (OperationCanceledException)
        {
            return 2;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "IwashiScope installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Install()
    {
        InstallManifest manifest = InstallManifest.Load();
        string installDirectory = InstallDirectory();
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "IwashiScope-Installer-" + Guid.NewGuid().ToString("N"));
        string programsDirectory = ProgramsDirectory();
        if (!InstallerCore.IsSafeInstallDirectory(
            installDirectory,
            programsDirectory,
            AppName))
        {
            throw new InvalidOperationException(
                "Refusing to install outside the expected per-user application directory.");
        }

        using (InstallProgressForm progress = new InstallProgressForm(manifest.DisplayVersion))
        {
            progress.Show();
            try
            {
                progress.Report(5, "Preparing installation...");
                Directory.CreateDirectory(temporaryRoot);
                string payloadZip = Path.Combine(temporaryRoot, "payload.zip");
                string stagingDirectory = Path.Combine(temporaryRoot, "payload");
                ExtractResource("payload.zip", payloadZip);

                progress.Report(25, "Extracting application files...");
                InstallerCore.ExtractZipSafely(payloadZip, stagingDirectory);
                InstallerCore.ValidateApplicationPayload(stagingDirectory);

                progress.Report(40, "Checking the running application...");
                EnsureApplicationIsNotRunning(installDirectory);
                Directory.CreateDirectory(programsDirectory);
                string preparedDirectory = Path.Combine(
                    programsDirectory,
                    ".IwashiScope.install-" + Guid.NewGuid().ToString("N"));
                progress.Report(50, "Preparing application files...");
                InstallerCore.CopyDirectorySafely(
                    stagingDirectory,
                    preparedDirectory);
                string preparedInstaller = Path.Combine(
                    preparedDirectory,
                    InstalledInstallerName);
                File.Copy(
                    Assembly.GetExecutingAssembly().Location,
                    preparedInstaller,
                    false);
                InstallerCore.ValidateApplicationPayload(preparedDirectory);

                string shortcutPath = StartMenuShortcutPath();
                using (InstallMetadataSnapshot metadata =
                    InstallMetadataSnapshot.Capture(shortcutPath, temporaryRoot))
                {
                    try
                    {
                        progress.Report(68, "Replacing the installed application...");
                        InstallerCore.ReplaceDirectoryTransactional(
                            preparedDirectory,
                            installDirectory,
                            delegate
                            {
                                string installedExecutable = Path.Combine(
                                    installDirectory,
                                    MainExecutableName);
                                string installedInstaller = Path.Combine(
                                    installDirectory,
                                    InstalledInstallerName);
                                progress.Report(85, "Creating shortcuts...");
                                CreateStartMenuShortcut(
                                    installedExecutable,
                                    installDirectory);
                                WriteUninstallRegistration(
                                    manifest,
                                    installDirectory,
                                    installedExecutable,
                                    installedInstaller);

                                progress.Report(96, "Starting IwashiScope...");
                                using (Process launched = Process.Start(new ProcessStartInfo
                                {
                                    FileName = installedExecutable,
                                    WorkingDirectory = installDirectory,
                                    UseShellExecute = true
                                }))
                                {
                                    if (launched == null)
                                    {
                                        throw new InvalidOperationException(
                                            "IwashiScope could not be started after installation.");
                                    }
                                }
                            });
                        metadata.Commit();
                    }
                    catch
                    {
                        metadata.Restore();
                        throw;
                    }
                }
                progress.Report(100, "Installation complete.");
                Thread.Sleep(300);
                return 0;
            }
            finally
            {
                TryDeleteDirectory(temporaryRoot);
            }
        }
    }

    private static int Uninstall()
    {
        string installDirectory = InstallDirectory();
        string programsDirectory = ProgramsDirectory();
        if (!InstallerCore.IsSafeInstallDirectory(
            installDirectory,
            programsDirectory,
            AppName))
        {
            throw new InvalidOperationException(
                "Refusing to uninstall outside the expected per-user application directory.");
        }
        EnsureApplicationIsNotRunning(installDirectory);
        if (Directory.Exists(installDirectory))
        {
            string metadataRoot = Path.Combine(
                Path.GetTempPath(),
                "IwashiScope-Uninstall-Metadata-" + Guid.NewGuid().ToString("N"));
            string cleanupExecutable = Path.Combine(
                Path.GetTempPath(),
                "IwashiScope-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            Directory.CreateDirectory(metadataRoot);
            try
            {
                using (InstallMetadataSnapshot metadata =
                    InstallMetadataSnapshot.Capture(
                        StartMenuShortcutPath(),
                        metadataRoot))
                {
                    DeleteStartMenuShortcut();
                    Registry.CurrentUser.DeleteSubKeyTree(
                        UninstallRegistryKey,
                        false);
                    File.Copy(
                        Assembly.GetExecutingAssembly().Location,
                        cleanupExecutable,
                        false);
                    using (Process cleanup = Process.Start(new ProcessStartInfo
                    {
                        FileName = cleanupExecutable,
                        Arguments = "--cleanup " +
                            Process.GetCurrentProcess().Id.ToString() + " " +
                            Quote(installDirectory),
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false
                    }))
                    {
                        if (cleanup == null)
                        {
                            throw new InvalidOperationException(
                                "The uninstall cleanup process could not be started.");
                        }
                    }
                    metadata.Commit();
                }
            }
            catch
            {
                if (File.Exists(cleanupExecutable))
                {
                    File.Delete(cleanupExecutable);
                }
                throw;
            }
            finally
            {
                TryDeleteDirectory(metadataRoot);
            }
        }
        else
        {
            DeleteStartMenuShortcut();
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, false);
        }

        return 0;
    }

    private static int CleanupAfterUninstall(
        string parentProcessIdText,
        string installDirectory)
    {
        int parentProcessId;
        if (!int.TryParse(parentProcessIdText, out parentProcessId) ||
            !InstallerCore.IsSafeInstallDirectory(
                installDirectory,
                ProgramsDirectory(),
                AppName))
        {
            return 1;
        }

        try
        {
            using (Process parent = Process.GetProcessById(parentProcessId))
            {
                parent.WaitForExit(15000);
            }
        }
        catch (ArgumentException)
        {
            // The uninstaller already exited.
        }
        InstallerCore.DeleteInstallDirectorySafely(
            installDirectory,
            ProgramsDirectory(),
            AppName);
        InstallerCore.DeleteRunningExecutableSafely(
            Assembly.GetExecutingAssembly().Location,
            Path.GetTempPath(),
            "IwashiScope-Uninstall-");
        return 0;
    }

    private static string InstallDirectory()
    {
        return Path.Combine(ProgramsDirectory(), AppName);
    }

    private static string ProgramsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
    }

    private static void EnsureApplicationIsNotRunning(string installDirectory)
    {
        string normalizedInstallDirectory = InstallerCore.NormalizePath(
            installDirectory);
        while (true)
        {
            using (Process running = FindRunningInstalledApplication(
                normalizedInstallDirectory))
            {
                if (running == null)
                {
                    return;
                }
                if (running.WaitForExit(15000))
                {
                    continue;
                }
            }
            DialogResult result = MessageBox.Show(
                "IwashiScope is still running. Close it and click Retry to continue.",
                "IwashiScope installer",
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Information);
            if (result == DialogResult.Cancel)
            {
                throw new OperationCanceledException("Installation was cancelled.");
            }
        }
    }

    private static Process FindRunningInstalledApplication(string installDirectory)
    {
        Process current = Process.GetCurrentProcess();
        foreach (Process process in Process.GetProcessesByName("IwashiScope"))
        {
            try
            {
                if (process.Id == current.Id || process.MainModule == null)
                {
                    continue;
                }

                string path = process.MainModule.FileName;
                if (InstallerCore.NormalizePath(path).StartsWith(
                    installDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return process;
                }
            }
            catch
            {
                // Access to another process may be denied; it is not this per-user install.
            }
        }

        return null;
    }

    private static void ExtractResource(string resourceName, string destinationPath)
    {
        using (Stream input = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName))
        {
            if (input == null)
            {
                throw new InvalidOperationException(
                    "Installer resource was not found: " + resourceName);
            }

            using (FileStream output = File.Create(destinationPath))
            {
                input.CopyTo(output);
            }
        }
    }

    private static void WriteUninstallRegistration(
        InstallManifest manifest,
        string installDirectory,
        string installedExecutable,
        string installedInstaller)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey))
        {
            if (key == null)
            {
                throw new InvalidOperationException(
                    "Could not create the uninstall registration.");
            }

            key.SetValue("DisplayName", AppName, RegistryValueKind.String);
            key.SetValue(
                "DisplayVersion",
                manifest.DisplayVersion,
                RegistryValueKind.String);
            key.SetValue("Publisher", manifest.Publisher, RegistryValueKind.String);
            key.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
            key.SetValue("DisplayIcon", installedExecutable, RegistryValueKind.String);
            key.SetValue(
                "UninstallString",
                Quote(installedInstaller) + " --uninstall",
                RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    private static void CreateStartMenuShortcut(
        string installedExecutable,
        string installDirectory)
    {
        string shortcutPath = StartMenuShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            throw new InvalidOperationException("WScript.Shell is not available.");
        }

        object shell = Activator.CreateInstance(shellType);
        object shortcut = shellType.InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            null,
            shell,
            new object[] { shortcutPath });
        Type shortcutType = shortcut.GetType();
        shortcutType.InvokeMember(
            "TargetPath",
            BindingFlags.SetProperty,
            null,
            shortcut,
            new object[] { installedExecutable });
        shortcutType.InvokeMember(
            "WorkingDirectory",
            BindingFlags.SetProperty,
            null,
            shortcut,
            new object[] { installDirectory });
        shortcutType.InvokeMember(
            "IconLocation",
            BindingFlags.SetProperty,
            null,
            shortcut,
            new object[] { installedExecutable + ",0" });
        shortcutType.InvokeMember(
            "Save",
            BindingFlags.InvokeMethod,
            null,
            shortcut,
            null);
    }

    private static string StartMenuShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\IwashiScope.lnk");
    }

    private static void DeleteStartMenuShortcut()
    {
        string path = StartMenuShortcutPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private sealed class InstallMetadataSnapshot : IDisposable
    {
        private readonly string shortcutPath;
        private readonly string shortcutBackupPath;
        private readonly bool shortcutExisted;
        private readonly bool registryExisted;
        private readonly List<RegistryValueSnapshot> registryValues;
        private bool committed;
        private bool restored;

        private InstallMetadataSnapshot(
            string shortcutPath,
            string shortcutBackupPath,
            bool shortcutExisted,
            bool registryExisted,
            List<RegistryValueSnapshot> registryValues)
        {
            this.shortcutPath = shortcutPath;
            this.shortcutBackupPath = shortcutBackupPath;
            this.shortcutExisted = shortcutExisted;
            this.registryExisted = registryExisted;
            this.registryValues = registryValues;
        }

        public static InstallMetadataSnapshot Capture(
            string shortcutPath,
            string temporaryRoot)
        {
            bool shortcutExisted = File.Exists(shortcutPath);
            string shortcutBackupPath = Path.Combine(
                temporaryRoot,
                "previous-shortcut.lnk");
            if (shortcutExisted)
            {
                File.Copy(shortcutPath, shortcutBackupPath, false);
            }

            List<RegistryValueSnapshot> values =
                new List<RegistryValueSnapshot>();
            bool registryExisted = false;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                UninstallRegistryKey,
                false))
            {
                if (key != null)
                {
                    registryExisted = true;
                    foreach (string name in key.GetValueNames())
                    {
                        values.Add(new RegistryValueSnapshot(
                            name,
                            key.GetValue(
                                name,
                                null,
                                RegistryValueOptions.DoNotExpandEnvironmentNames),
                            key.GetValueKind(name)));
                    }
                }
            }

            return new InstallMetadataSnapshot(
                shortcutPath,
                shortcutBackupPath,
                shortcutExisted,
                registryExisted,
                values);
        }

        public void Commit()
        {
            committed = true;
        }

        public void Restore()
        {
            if (restored)
            {
                return;
            }

            if (shortcutExisted)
            {
                string parent = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }
                File.Copy(shortcutBackupPath, shortcutPath, true);
            }
            else if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }

            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, false);
            if (registryExisted)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    UninstallRegistryKey))
                {
                    if (key == null)
                    {
                        throw new InvalidOperationException(
                            "Could not restore the previous uninstall registration.");
                    }
                    foreach (RegistryValueSnapshot value in registryValues)
                    {
                        key.SetValue(value.Name, value.Value, value.Kind);
                    }
                }
            }
            restored = true;
        }

        public void Dispose()
        {
            if (!committed && !restored)
            {
                Restore();
            }
            if (File.Exists(shortcutBackupPath))
            {
                File.Delete(shortcutBackupPath);
            }
        }
    }

    private sealed class RegistryValueSnapshot
    {
        public RegistryValueSnapshot(
            string name,
            object value,
            RegistryValueKind kind)
        {
            Name = name;
            Value = value;
            Kind = kind;
        }

        public string Name;
        public object Value;
        public RegistryValueKind Kind;
    }

    private sealed class InstallProgressForm : Form
    {
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;

        public InstallProgressForm(string displayVersion)
        {
            Text = "IwashiScope installer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = true;
            Width = 420;
            Height = 150;

            Label title = new Label
            {
                Left = 16,
                Top = 16,
                Width = 370,
                Height = 24,
                Text = "Installing IwashiScope " + displayVersion
            };
            statusLabel = new Label
            {
                Left = 16,
                Top = 46,
                Width = 370,
                Height = 20
            };
            progressBar = new ProgressBar
            {
                Left = 16,
                Top = 76,
                Width = 370,
                Height = 18,
                Minimum = 0,
                Maximum = 100
            };
            Controls.Add(title);
            Controls.Add(statusLabel);
            Controls.Add(progressBar);
        }

        public void Report(int percent, string message)
        {
            progressBar.Value = Math.Max(0, Math.Min(100, percent));
            statusLabel.Text = message;
            Refresh();
            Application.DoEvents();
        }
    }

    private sealed class InstallManifest
    {
        public string DisplayVersion = "0.0.0.0";
        public string Publisher = "Yamonov";

        public static InstallManifest Load()
        {
            InstallManifest manifest = new InstallManifest();
            using (Stream input = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("install.properties"))
            {
                if (input == null)
                {
                    return manifest;
                }

                using (StreamReader reader = new StreamReader(input, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int separator = line.IndexOf('=');
                        if (separator <= 0)
                        {
                            continue;
                        }

                        string key = line.Substring(0, separator).Trim();
                        string value = line.Substring(separator + 1).Trim();
                        if (string.Equals(
                            key,
                            "DisplayVersion",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            manifest.DisplayVersion = value;
                        }
                        else if (string.Equals(
                            key,
                            "Publisher",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            manifest.Publisher = value;
                        }
                    }
                }
            }

            return manifest;
        }
    }
}

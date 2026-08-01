using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using IwashiScope.Installer;

internal static class InstallerCoreTests
{
    private static int passed;

    private static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IwashiScope-Installer-Core-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int result = 0;
        try
        {
            Run("valid payload extraction", delegate { TestValidExtraction(root); });
            Run("parent traversal rejected", delegate
            {
                TestRejectedEntry(root, "../escape.txt", 0);
            });
            Run("absolute path rejected", delegate
            {
                TestRejectedEntry(root, "C:/escape.txt", 0);
            });
            Run("alternate stream rejected", delegate
            {
                TestRejectedEntry(root, "safe.txt:stream", 0);
            });
            Run("reserved device name rejected", delegate
            {
                TestRejectedEntry(root, "CON.txt", 0);
            });
            Run("symbolic link rejected", delegate
            {
                TestRejectedEntry(root, "link", unchecked((int)0xA0000000));
            });
            Run("case-insensitive duplicate rejected", delegate
            {
                TestDuplicateEntry(root);
            });
            Run("transactional update succeeds", delegate
            {
                TestTransactionalSuccess(root);
            });
            Run("transactional update rolls back", delegate
            {
                TestTransactionalRollback(root);
            });
            Run("sibling user data survives update", delegate
            {
                TestSiblingDataPreserved(root);
            });
            Run("install directory boundary enforced", delegate
            {
                TestInstallDirectoryBoundary(root);
            });
            Run("isolated uninstall preserves user data", delegate
            {
                TestIsolatedUninstall(root);
            });

            Console.WriteLine("Installer core tests passed: " + passed);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            result = 1;
        }
        finally
        {
            InstallerCore.TryDeleteDirectory(root);
        }
        if (result == 0)
        {
            try
            {
                string executable = Assembly.GetExecutingAssembly().Location;
                InstallerCore.DeleteRunningExecutableSafely(
                    executable,
                    Path.GetDirectoryName(executable),
                    "IwashiScopeInstallerCoreTests");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                if (exception.InnerException != null)
                {
                    Console.Error.WriteLine(exception.InnerException.Message);
                }
                result = 1;
            }
        }
        return result;
    }

    private static void TestValidExtraction(string root)
    {
        string testRoot = NewTestRoot(root);
        string archive = Path.Combine(testRoot, "valid.zip");
        CreateArchive(archive, new[] { "folder/data.txt", "root.txt" }, 0);
        string destination = Path.Combine(testRoot, "out");
        InstallerCore.ExtractZipSafely(archive, destination);
        Assert(File.ReadAllText(Path.Combine(destination, "root.txt")) == "test",
            "Root file content was not extracted.");
        Assert(File.Exists(Path.Combine(destination, "folder", "data.txt")),
            "Nested file was not extracted.");
    }

    private static void TestRejectedEntry(
        string root,
        string entryName,
        int externalAttributes)
    {
        string testRoot = NewTestRoot(root);
        string archive = Path.Combine(testRoot, "unsafe.zip");
        CreateArchive(archive, new[] { entryName }, externalAttributes);
        AssertThrows<InvalidDataException>(delegate
        {
            InstallerCore.ExtractZipSafely(
                archive,
                Path.Combine(testRoot, "out"));
        });
        Assert(!File.Exists(Path.Combine(root, "escape.txt")),
            "Unsafe archive wrote outside its staging directory.");
    }

    private static void TestDuplicateEntry(string root)
    {
        string testRoot = NewTestRoot(root);
        string archivePath = Path.Combine(testRoot, "duplicate.zip");
        using (ZipArchive archive = ZipFile.Open(
            archivePath,
            ZipArchiveMode.Create))
        {
            WriteEntry(archive.CreateEntry("same.txt"));
            WriteEntry(archive.CreateEntry("SAME.txt"));
        }
        AssertThrows<InvalidDataException>(delegate
        {
            InstallerCore.ExtractZipSafely(
                archivePath,
                Path.Combine(testRoot, "out"));
        });
    }

    private static void TestTransactionalSuccess(string root)
    {
        string testRoot = NewTestRoot(root);
        string target = Path.Combine(testRoot, "IwashiScope");
        string prepared = Path.Combine(testRoot, ".prepared");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(prepared);
        File.WriteAllText(Path.Combine(target, "version.txt"), "old");
        File.WriteAllText(Path.Combine(prepared, "version.txt"), "new");
        InstallerCore.ReplaceDirectoryTransactional(prepared, target, delegate { });
        Assert(File.ReadAllText(Path.Combine(target, "version.txt")) == "new",
            "New installation was not committed.");
        Assert(!Directory.Exists(prepared),
            "Prepared directory remained after commit.");
    }

    private static void TestTransactionalRollback(string root)
    {
        string testRoot = NewTestRoot(root);
        string target = Path.Combine(testRoot, "IwashiScope");
        string prepared = Path.Combine(testRoot, ".prepared");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(prepared);
        File.WriteAllText(Path.Combine(target, "version.txt"), "old");
        File.WriteAllText(Path.Combine(prepared, "version.txt"), "new");
        AssertThrows<InvalidOperationException>(delegate
        {
            InstallerCore.ReplaceDirectoryTransactional(
                prepared,
                target,
                delegate { throw new InvalidOperationException("injected failure"); });
        });
        Assert(File.ReadAllText(Path.Combine(target, "version.txt")) == "old",
            "Previous installation was not restored after failure.");
        Assert(Directory.GetDirectories(testRoot, "*.backup-*", SearchOption.TopDirectoryOnly).Length == 0,
            "Rollback backup remained after restoration.");
    }

    private static void TestSiblingDataPreserved(string root)
    {
        string testRoot = NewTestRoot(root);
        string programs = Path.Combine(testRoot, "Programs");
        string target = Path.Combine(programs, "IwashiScope");
        string prepared = Path.Combine(programs, ".prepared");
        string settings = Path.Combine(testRoot, "IwashiScope", "settings.json");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(prepared);
        Directory.CreateDirectory(Path.GetDirectoryName(settings));
        File.WriteAllText(Path.Combine(target, "version.txt"), "old");
        File.WriteAllText(Path.Combine(prepared, "version.txt"), "new");
        File.WriteAllText(settings, "user-data");
        InstallerCore.ReplaceDirectoryTransactional(prepared, target, delegate { });
        Assert(File.ReadAllText(settings) == "user-data",
            "Sibling settings data was modified by the update.");
    }

    private static void TestInstallDirectoryBoundary(string root)
    {
        string programs = Path.Combine(root, "Programs");
        Assert(InstallerCore.IsSafeInstallDirectory(
            Path.Combine(programs, "IwashiScope"),
            programs,
            "IwashiScope"),
            "Expected install directory was rejected.");
        Assert(!InstallerCore.IsSafeInstallDirectory(
            Path.Combine(programs, "Nested", "IwashiScope"),
            programs,
            "IwashiScope"),
            "Nested unexpected install directory was accepted.");
        Assert(!InstallerCore.IsSafeInstallDirectory(
            Path.Combine(root, "IwashiScope"),
            programs,
            "IwashiScope"),
            "Directory outside Programs was accepted.");
    }

    private static void TestIsolatedUninstall(string root)
    {
        string testRoot = NewTestRoot(root);
        string programs = Path.Combine(testRoot, "Programs");
        string install = Path.Combine(programs, "IwashiScope");
        string settings = Path.Combine(testRoot, "IwashiScope", "settings.json");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.GetDirectoryName(settings));
        File.WriteAllText(Path.Combine(install, "IwashiScope.exe"), "binary");
        File.WriteAllText(settings, "user-data");

        InstallerCore.DeleteInstallDirectorySafely(
            install,
            programs,
            "IwashiScope");
        Assert(!Directory.Exists(install),
            "Isolated installation directory was not deleted.");
        Assert(File.ReadAllText(settings) == "user-data",
            "User settings were deleted during isolated uninstall.");
        AssertThrows<InvalidOperationException>(delegate
        {
            InstallerCore.DeleteInstallDirectorySafely(
                testRoot,
                programs,
                "IwashiScope");
        });
    }

    private static void CreateArchive(
        string archivePath,
        string[] entryNames,
        int externalAttributes)
    {
        using (ZipArchive archive = ZipFile.Open(
            archivePath,
            ZipArchiveMode.Create))
        {
            foreach (string entryName in entryNames)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName);
                entry.ExternalAttributes = externalAttributes;
                WriteEntry(entry);
            }
        }
    }

    private static void WriteEntry(ZipArchiveEntry entry)
    {
        using (StreamWriter writer = new StreamWriter(entry.Open()))
        {
            writer.Write("test");
        }
    }

    private static string NewTestRoot(string root)
    {
        string path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(
            "Expected exception was not thrown: " + typeof(TException).FullName);
    }
}

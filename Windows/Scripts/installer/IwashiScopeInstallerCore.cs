using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IwashiScope.Installer
{
    internal static class InstallerCore
    {
        private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
        private const int MaximumEntryCount = 10000;
        private const uint DeleteAccess = 0x00010000;
        private const uint ExclusiveShareMode = 0;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const int FileRenameInfo = 3;
        private const int FileDispositionInfoEx = 21;
        private const int FileDispositionDeleteWithPosixSemantics = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            IntPtr fileInformation,
            uint bufferSize);

        public static void ExtractZipSafely(
            string archivePath,
            string destinationDirectory)
        {
            string destination = NormalizePath(destinationDirectory);
            if (Directory.Exists(destination) &&
                Directory.GetFileSystemEntries(destination).Length != 0)
            {
                throw new InvalidOperationException(
                    "Installer extraction directory must be empty.");
            }

            Directory.CreateDirectory(destination);
            string destinationPrefix = destination + Path.DirectorySeparatorChar;
            HashSet<string> extractedPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            int entryCount = 0;

            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    entryCount++;
                    if (entryCount > MaximumEntryCount)
                    {
                        throw new InvalidDataException(
                            "Installer payload contains too many entries.");
                    }

                    string relativePath = ValidateEntryPath(entry);
                    if (relativePath.Length == 0)
                    {
                        continue;
                    }

                    string targetPath = NormalizePath(Path.Combine(
                        destination,
                        relativePath));
                    if (!targetPath.StartsWith(
                        destinationPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Installer payload attempts to write outside its staging directory.");
                    }
                    if (!extractedPaths.Add(targetPath))
                    {
                        throw new InvalidDataException(
                            "Installer payload contains a duplicate path: " + relativePath);
                    }

                    bool directoryEntry = entry.FullName.EndsWith(
                        "/",
                        StringComparison.Ordinal) ||
                        entry.FullName.EndsWith("\\", StringComparison.Ordinal);
                    if (directoryEntry)
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    try
                    {
                        expandedBytes = checked(expandedBytes + entry.Length);
                    }
                    catch (OverflowException)
                    {
                        throw new InvalidDataException(
                            "Installer payload expanded size is invalid.");
                    }
                    if (expandedBytes > MaximumExpandedBytes)
                    {
                        throw new InvalidDataException(
                            "Installer payload exceeds the expanded-size limit.");
                    }

                    string parent = Path.GetDirectoryName(targetPath);
                    if (string.IsNullOrEmpty(parent))
                    {
                        throw new InvalidDataException(
                            "Installer payload target has no parent directory.");
                    }
                    Directory.CreateDirectory(parent);
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(
                        targetPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        public static void ValidateApplicationPayload(string directory)
        {
            foreach (string requiredName in new[]
            {
                "IwashiScope.exe",
                "iwashiscope-spotread.exe",
                "WinSparkle.dll",
                "LICENSE",
                "NOTICE",
                "THIRD_PARTY_NOTICES.md",
                "SOURCE_INFO.txt"
            })
            {
                string path = Path.Combine(directory, requiredName);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "Required installer payload file was not found: " + requiredName,
                        path);
                }
            }
        }

        public static void CopyDirectorySafely(
            string sourceDirectory,
            string targetDirectory)
        {
            string source = NormalizePath(sourceDirectory);
            string target = NormalizePath(targetDirectory);
            if (Directory.Exists(target))
            {
                throw new IOException(
                    "Prepared installation directory already exists: " + target);
            }
            if (IsReparsePoint(source))
            {
                throw new IOException(
                    "Installer staging directory must not be a reparse point.");
            }

            Directory.CreateDirectory(target);
            foreach (string directory in Directory.GetDirectories(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                if (IsReparsePoint(directory))
                {
                    throw new IOException(
                        "Installer payload contains a reparse-point directory.");
                }
                Directory.CreateDirectory(Path.Combine(
                    target,
                    RelativePath(source, directory)));
            }

            foreach (string file in Directory.GetFiles(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                if (IsReparsePoint(file))
                {
                    throw new IOException(
                        "Installer payload contains a reparse-point file.");
                }
                string destination = Path.Combine(
                    target,
                    RelativePath(source, file));
                string parent = Path.GetDirectoryName(destination);
                if (string.IsNullOrEmpty(parent))
                {
                    throw new IOException(
                        "Installer payload destination has no parent directory.");
                }
                Directory.CreateDirectory(parent);
                File.Copy(file, destination, false);
            }
        }

        public static void ReplaceDirectoryTransactional(
            string preparedDirectory,
            string installDirectory,
            Action afterSwap)
        {
            string prepared = NormalizePath(preparedDirectory);
            string target = NormalizePath(installDirectory);
            string preparedParent = Directory.GetParent(prepared).FullName;
            string targetParent = Directory.GetParent(target).FullName;
            if (!string.Equals(
                preparedParent,
                targetParent,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Prepared and installed directories must share a parent volume.");
            }
            if (!Directory.Exists(prepared))
            {
                throw new DirectoryNotFoundException(
                    "Prepared installation directory was not found: " + prepared);
            }
            if (Directory.Exists(target) && IsReparsePoint(target))
            {
                throw new IOException(
                    "Existing installation directory must not be a reparse point.");
            }

            string backup = target + ".backup-" + Guid.NewGuid().ToString("N");
            bool previousMoved = false;
            bool newMoved = false;
            bool committed = false;
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                    previousMoved = true;
                }
                Directory.Move(prepared, target);
                newMoved = true;
                afterSwap();
                committed = true;
            }
            catch
            {
                if (newMoved && Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
                if (previousMoved && Directory.Exists(backup))
                {
                    Directory.Move(backup, target);
                }
                throw;
            }
            finally
            {
                if (!newMoved && Directory.Exists(prepared))
                {
                    Directory.Delete(prepared, true);
                }
                if (committed && previousMoved && Directory.Exists(backup))
                {
                    TryDeleteDirectory(backup);
                }
            }
        }

        public static bool IsSafeInstallDirectory(
            string installDirectory,
            string programsDirectory,
            string expectedLeafName)
        {
            string candidate = NormalizePath(installDirectory);
            string programs = NormalizePath(programsDirectory);
            return candidate.StartsWith(
                    programs + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    Path.GetFileName(candidate),
                    expectedLeafName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    Directory.GetParent(candidate).FullName,
                    programs,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static void DeleteInstallDirectorySafely(
            string installDirectory,
            string programsDirectory,
            string expectedLeafName)
        {
            if (!IsSafeInstallDirectory(
                installDirectory,
                programsDirectory,
                expectedLeafName))
            {
                throw new InvalidOperationException(
                    "Refusing to delete an unexpected installation directory.");
            }
            if (!Directory.Exists(installDirectory))
            {
                return;
            }
            if (IsReparsePoint(installDirectory))
            {
                throw new IOException(
                    "Installation directory must not be a reparse point.");
            }
            Directory.Delete(installDirectory, true);
        }

        public static void DeleteRunningExecutableSafely(
            string executablePath,
            string expectedParentDirectory,
            string expectedFileNamePrefix)
        {
            string executable = NormalizePath(executablePath);
            string expectedParent = NormalizePath(expectedParentDirectory);
            string actualParent = Directory.GetParent(executable).FullName;
            string fileName = Path.GetFileName(executable);
            if (!string.Equals(
                    actualParent,
                    expectedParent,
                    StringComparison.OrdinalIgnoreCase) ||
                !fileName.StartsWith(
                    expectedFileNamePrefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to delete an unexpected running executable.");
            }

            string streamName = ":IwashiScope-Uninstall-" +
                Guid.NewGuid().ToString("N");
            byte[] streamNameBytes = System.Text.Encoding.Unicode.GetBytes(
                streamName);
            int rootDirectoryOffset = IntPtr.Size == 8 ? 8 : 4;
            int fileNameLengthOffset = IntPtr.Size == 8 ? 16 : 8;
            int fileNameOffset = IntPtr.Size == 8 ? 20 : 12;
            IntPtr renameBuffer = Marshal.AllocHGlobal(
                fileNameOffset + streamNameBytes.Length);
            try
            {
                for (int index = 0;
                    index < fileNameOffset + streamNameBytes.Length;
                    index++)
                {
                    Marshal.WriteByte(renameBuffer, index, 0);
                }
                Marshal.WriteIntPtr(
                    renameBuffer,
                    rootDirectoryOffset,
                    IntPtr.Zero);
                Marshal.WriteInt32(
                    renameBuffer,
                    fileNameLengthOffset,
                    streamNameBytes.Length);
                Marshal.Copy(
                    streamNameBytes,
                    0,
                    IntPtr.Add(renameBuffer, fileNameOffset),
                    streamNameBytes.Length);

                using (SafeFileHandle file = OpenForDelete(executable))
                {
                    if (!SetFileInformationByHandle(
                        file,
                        FileRenameInfo,
                        renameBuffer,
                        (uint)(fileNameOffset + streamNameBytes.Length)))
                    {
                        throw Win32IOException(
                            "Unable to isolate the temporary uninstall helper stream.");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(renameBuffer);
            }

            IntPtr dispositionBuffer = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(
                    dispositionBuffer,
                    0,
                    FileDispositionDeleteWithPosixSemantics);
                using (SafeFileHandle file = OpenForDelete(executable))
                {
                    if (!SetFileInformationByHandle(
                        file,
                        FileDispositionInfoEx,
                        dispositionBuffer,
                        4))
                    {
                        throw Win32IOException(
                            "Unable to remove the temporary uninstall helper name.");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(dispositionBuffer);
            }

            if (File.Exists(executable))
            {
                throw new IOException(
                    "The temporary uninstall helper remained visible after deletion.");
            }
        }

        private static SafeFileHandle OpenForDelete(string path)
        {
            SafeFileHandle file = CreateFile(
                path,
                DeleteAccess,
                ExclusiveShareMode,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);
            if (file.IsInvalid)
            {
                file.Dispose();
                throw Win32IOException(
                    "Unable to open the temporary uninstall helper for deletion.");
            }
            return file;
        }

        private static IOException Win32IOException(string message)
        {
            return new IOException(
                message,
                new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error()));
        }

        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        public static void TryDeleteDirectory(string path)
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

        private static string ValidateEntryPath(ZipArchiveEntry entry)
        {
            if (IsArchiveLink(entry))
            {
                throw new InvalidDataException(
                    "Installer payload contains a symbolic link or reparse point.");
            }

            string path = entry.FullName.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            if (path.Length == 0)
            {
                return string.Empty;
            }
            if (Path.IsPathRooted(path) || path.IndexOf(':') >= 0)
            {
                throw new InvalidDataException(
                    "Installer payload contains an absolute or alternate-stream path.");
            }

            string[] segments = path.Split(Path.DirectorySeparatorChar);
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(" ", StringComparison.Ordinal) ||
                    segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    IsReservedWindowsName(segment))
                {
                    throw new InvalidDataException(
                        "Installer payload contains an unsafe path: " + entry.FullName);
                }
            }
            return Path.Combine(segments);
        }

        private static bool IsArchiveLink(ZipArchiveEntry entry)
        {
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            bool unixSymbolicLink = unixType == 0xA000;
            bool windowsReparsePoint =
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
            return unixSymbolicLink || windowsReparsePoint;
        }

        private static bool IsReservedWindowsName(string segment)
        {
            string name = Path.GetFileNameWithoutExtension(segment).ToUpperInvariant();
            if (name == "CON" || name == "PRN" || name == "AUX" || name == "NUL")
            {
                return true;
            }
            if (name.Length == 4 &&
                (name.StartsWith("COM", StringComparison.Ordinal) ||
                 name.StartsWith("LPT", StringComparison.Ordinal)))
            {
                char number = name[3];
                return number >= '1' && number <= '9';
            }
            return false;
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static string RelativePath(string root, string path)
        {
            return path.Substring(root.Length).TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
    }
}

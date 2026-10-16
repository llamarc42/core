using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Llamarc42.Core.Discovery;

internal enum FileSystemEntryKind
{
    RegularFile,
    Directory,
    Link,
    Other,
}

internal static class FileSystemEntryInspector
{
    private const int AtFileDescriptorCurrentWorkingDirectory = -100;
    private const int AtSymbolicLinkNoFollow = 0x100;
    private const uint StatxType = 0x0001;
    private const int StatxModeOffset = 28;
    private const int DarwinModeOffset = 4;
    private const int NativeStatBufferSize = 256;
    private const ushort FileTypeMask = 0xF000;
    private const ushort DirectoryType = 0x4000;
    private const ushort RegularFileType = 0x8000;
    private const ushort SymbolicLinkType = 0xA000;

    public static FileSystemEntryKind Inspect(string path, FileAttributes attributes)
    {
        if (OperatingSystem.IsLinux())
        {
            return InspectLinux(path);
        }

        if (OperatingSystem.IsMacOS())
        {
            return InspectDarwin(path);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return FileSystemEntryKind.Link;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return FileSystemEntryKind.Directory;
        }

        return (attributes & FileAttributes.Device) != 0
            ? FileSystemEntryKind.Other
            : FileSystemEntryKind.RegularFile;
    }

    private static FileSystemEntryKind InspectLinux(string path)
    {
        var buffer = Marshal.AllocHGlobal(NativeStatBufferSize);
        try
        {
            var status = Statx(
                AtFileDescriptorCurrentWorkingDirectory,
                path,
                AtSymbolicLinkNoFollow,
                StatxType,
                buffer);

            if (status != 0)
            {
                throw CreateInspectionException(path);
            }

            return ClassifyMode(unchecked((ushort)Marshal.ReadInt16(buffer, StatxModeOffset)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static FileSystemEntryKind InspectDarwin(string path)
    {
        var buffer = Marshal.AllocHGlobal(NativeStatBufferSize);
        try
        {
            if (LStat(path, buffer) != 0)
            {
                throw CreateInspectionException(path);
            }

            return ClassifyMode(unchecked((ushort)Marshal.ReadInt16(buffer, DarwinModeOffset)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static FileSystemEntryKind ClassifyMode(ushort mode) =>
        (mode & FileTypeMask) switch
        {
            RegularFileType => FileSystemEntryKind.RegularFile,
            DirectoryType => FileSystemEntryKind.Directory,
            SymbolicLinkType => FileSystemEntryKind.Link,
            _ => FileSystemEntryKind.Other,
        };

    private static IOException CreateInspectionException(string path)
    {
        var error = Marshal.GetLastPInvokeError();
        return new IOException(
            $"The filesystem entry kind for '{path}' could not be inspected ({new Win32Exception(error).Message}).");
    }

#pragma warning disable CA2101 // Unix filesystem paths are marshalled explicitly as UTF-8.
#pragma warning disable SYSLIB1054 // The generated alternative requires enabling unsafe code for the project.
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        nint buffer);

    [DllImport("libSystem.B.dylib", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint buffer);
#pragma warning restore SYSLIB1054
#pragma warning restore CA2101
}

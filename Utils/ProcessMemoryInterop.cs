using System.Runtime.InteropServices;

namespace OsuMate.Utils
{

  internal static class ProcessMemoryInterop
  {
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessQueryInformation = 0x0400;

    internal const uint MemCommit = 0x1000;
    internal const uint MemPrivate = 0x20000;

    internal const uint PageNoAccess = 0x01;
    internal const uint PageGuard = 0x100;
    internal const uint PageReadableMask = 0xFE;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
      public nuint BaseAddress;
      public nuint AllocationBase;
      public uint AllocationProtect;
      public ushort PartitionId;
      public nuint RegionSize;
      public uint State;
      public uint Protect;
      public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(
      uint desiredAccess,
      [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
      int processId
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process(
      nint process,
      [MarshalAs(UnmanagedType.Bool)] out bool wow64Process
    );

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
      nint process,
      nuint baseAddress,
      ref byte buffer,
      nuint size,
      out nuint bytesRead
    );

    [DllImport("kernel32.dll")]
    internal static extern nuint VirtualQueryEx(
      nint process,
      nuint address,
      out MemoryBasicInformation buffer,
      nuint length
    );
  }
}

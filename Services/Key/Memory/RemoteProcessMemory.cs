using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OsuMate.Utils;

namespace OsuMate.Services.Key.Memory
{

  internal readonly record struct MemoryRegion(uint Start, uint Size, uint Protect, uint Type);

  internal sealed class RemoteProcessMemory : IDisposable
  {
    private const uint ReadAccess =
      ProcessMemoryInterop.ProcessVmRead | ProcessMemoryInterop.ProcessQueryInformation;
    private const ulong AddressSpaceEnd = 0x1_0000_0000UL;
    private const uint MinPointerValue = 0x10000;

    private readonly nint _handle;

    private RemoteProcessMemory(nint handle) => _handle = handle;

    internal static bool IsValidPointer(uint value) => value >= MinPointerValue;

    internal static RemoteProcessMemory? TryOpen(int processId)
    {
      if (processId <= 0)
        return null;

      var handle = ProcessMemoryInterop.OpenProcess(ReadAccess, false, processId);
      if (handle == 0)
        return null;

      if (ProcessMemoryInterop.IsWow64Process(handle, out var is32BitProcess) && is32BitProcess)
        return new RemoteProcessMemory(handle);

      ProcessMemoryInterop.CloseHandle(handle);
      return null;
    }

    internal bool TryRead(uint address, Span<byte> buffer)
    {
      if (buffer.IsEmpty)
        return true;
      if ((ulong)address + (ulong)buffer.Length > AddressSpaceEnd)
        return false;

      return ProcessMemoryInterop.ReadProcessMemory(
          _handle,
          address,
          ref MemoryMarshal.GetReference(buffer),
          (nuint)buffer.Length,
          out var bytesRead
        ) && bytesRead == (nuint)buffer.Length;
    }

    internal bool TryReadUInt32(uint address, out uint value)
    {
      Span<byte> buffer = stackalloc byte[sizeof(uint)];
      if (!TryRead(address, buffer))
      {
        value = 0;
        return false;
      }

      value = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
      return true;
    }

    internal bool TryReadInt32(uint address, out int value)
    {
      Span<byte> buffer = stackalloc byte[sizeof(int)];
      if (!TryRead(address, buffer))
      {
        value = 0;
        return false;
      }

      value = BinaryPrimitives.ReadInt32LittleEndian(buffer);
      return true;
    }

    internal IEnumerable<MemoryRegion> EnumerateCommittedRegions()
    {
      var infoSize = (nuint)Marshal.SizeOf<ProcessMemoryInterop.MemoryBasicInformation>();
      ulong address = 0;

      while (address < AddressSpaceEnd)
      {
        if (ProcessMemoryInterop.VirtualQueryEx(_handle, (nuint)address, out var info, infoSize) == 0)
          yield break;

        var start = (ulong)info.BaseAddress;
        var next = start + (ulong)info.RegionSize;
        if (next <= address)
          yield break;

        var end = Math.Min(next, AddressSpaceEnd);
        if (info.State == ProcessMemoryInterop.MemCommit && end > start)
          yield return new MemoryRegion((uint)start, (uint)(end - start), info.Protect, info.Type);

        address = next;
      }
    }

    public void Dispose() => ProcessMemoryInterop.CloseHandle(_handle);
  }
}

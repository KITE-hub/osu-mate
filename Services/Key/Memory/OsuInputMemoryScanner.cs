using System.Buffers;
using System.Runtime.InteropServices;
using OsuMate.Utils;

namespace OsuMate.Services.Key.Memory
{

  internal static class OsuInputMemoryScanner
  {
    private const int ChunkSize = 1024 * 1024;
    private const int OverlapBytes = 128;
    private const int WindowSize = ChunkSize + OverlapBytes;
    private const int MinWindowBytes = 16;
    private const int MaxEntryNextIndex = 512;

    internal static void Scan(
      RemoteProcessMemory memory,
      ref BindingDictionaryTable? bindings,
      ref ManiaLayoutTable? maniaLayouts
    )
    {
      var buffer = ArrayPool<byte>.Shared.Rent(WindowSize);
      try
      {
        foreach (var region in memory.EnumerateCommittedRegions())
        {
          if (!IsReadablePrivate(region))
            continue;

          ScanRegion(memory, region, buffer, ref bindings, ref maniaLayouts);
          if (bindings != null && maniaLayouts != null)
            return;
        }
      }
      finally
      {
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
      }
    }

    private static bool IsReadablePrivate(MemoryRegion region) =>
      region.Type == ProcessMemoryInterop.MemPrivate
      && (region.Protect & ProcessMemoryInterop.PageReadableMask) != 0
      && (region.Protect & (ProcessMemoryInterop.PageGuard | ProcessMemoryInterop.PageNoAccess)) == 0;

    private static void ScanRegion(
      RemoteProcessMemory memory,
      MemoryRegion region,
      byte[] buffer,
      ref BindingDictionaryTable? bindings,
      ref ManiaLayoutTable? maniaLayouts
    )
    {
      for (ulong offset = 0; offset < region.Size; offset += ChunkSize)
      {
        var length = (int)Math.Min((ulong)WindowSize, region.Size - offset) & ~(sizeof(int) - 1);
        if (length < MinWindowBytes)
          break;

        var windowStart = region.Start + (uint)offset;
        var window = buffer.AsSpan(0, length);
        if (!memory.TryRead(windowStart, window))
          continue;

        ScanWindow(memory, window, windowStart, ref bindings, ref maniaLayouts);
        if (bindings != null && maniaLayouts != null)
          return;
      }
    }

    private static void ScanWindow(
      RemoteProcessMemory memory,
      ReadOnlySpan<byte> window,
      uint windowStart,
      ref BindingDictionaryTable? bindings,
      ref ManiaLayoutTable? maniaLayouts
    )
    {
      var words = MemoryMarshal.Cast<byte, int>(window);
      for (var i = 0; i < words.Length; i++)
      {
        var address = windowStart + (uint)(i * sizeof(int));
        var word = words[i];

        if (
          bindings == null
          && word == (int)OsuBinding.OsuLeft
          && LooksLikeOsuLeftEntry(words, i)
          && BindingDictionaryTable.TryCreate(memory, address, out var bindingTable)
        )
          bindings = bindingTable;
        else if (
          maniaLayouts == null
          && word == ManiaLayoutTable.TotalElementCount
          && LooksLikeStageArrayHeader(words, i)
          && ManiaLayoutTable.TryCreate(memory, address - sizeof(uint), out var layoutTable)
        )
          maniaLayouts = layoutTable;

        if (bindings != null && maniaLayouts != null)
          return;
      }
    }

    private static bool LooksLikeOsuLeftEntry(ReadOnlySpan<int> words, int index) =>
      index + 3 < words.Length
      && words[index + 1] is >= -1 and < MaxEntryNextIndex
      && words[index + 2] == (int)OsuBinding.OsuLeft
      && OsuBindingCatalog.IsValidKeyCode(words[index + 3]);

    private static bool LooksLikeStageArrayHeader(ReadOnlySpan<int> words, int index) =>
      index >= 1
      && index + 4 < words.Length
      && IsPlausiblePointer((uint)words[index - 1])
      && words[index + 1] == ManiaLayoutTable.Dimension0Length
      && words[index + 2] == ManiaLayoutTable.Dimension1Length
      && words[index + 3] == 0
      && words[index + 4] == 0;

    private static bool IsPlausiblePointer(uint value) =>
      RemoteProcessMemory.IsValidPointer(value) && (value & (sizeof(uint) - 1)) == 0;
  }
}

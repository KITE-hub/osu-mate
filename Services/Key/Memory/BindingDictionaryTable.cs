using System.Runtime.InteropServices;

namespace OsuMate.Services.Key.Memory
{

  internal sealed class BindingDictionaryTable
  {
    private const uint LengthOffsetBeforeData = 0x4;
    private const uint ArrayHeaderSize = 0x8;
    private const int EntrySize = 16;
    private const int WordsPerEntry = EntrySize / sizeof(int);
    private const int MaxEntryCount = 512;
    private const int MaxBindingId = 255;

    private readonly uint _dataAddress;
    private readonly int _entryCount;
    private readonly byte[] _buffer;

    private BindingDictionaryTable(uint dataAddress, int entryCount)
    {
      _dataAddress = dataAddress;
      _entryCount = entryCount;
      _buffer = new byte[entryCount * EntrySize];
    }

    internal uint Address => _dataAddress;

    internal static bool TryCreate(
      RemoteProcessMemory memory,
      uint osuLeftEntryAddress,
      out BindingDictionaryTable? table
    )
    {
      table = null;
      for (var index = 0; index < MaxEntryCount; index++)
      {
        var offset = (uint)(index * EntrySize);
        if (osuLeftEntryAddress < offset + ArrayHeaderSize)
          return false;

        var dataAddress = osuLeftEntryAddress - offset;
        if (
          !memory.TryReadInt32(dataAddress - LengthOffsetBeforeData, out var length)
          || length <= index
          || length > MaxEntryCount
        )
          continue;

        var candidate = new BindingDictionaryTable(dataAddress, length);
        if (!candidate.TryRead(memory, out _))
          continue;

        table = candidate;
        return true;
      }
      return false;
    }

    internal bool TryRead(RemoteProcessMemory memory, out Dictionary<int, int> bindings)
    {
      bindings = [];
      if (
        !memory.TryReadInt32(_dataAddress - LengthOffsetBeforeData, out var length)
        || length != _entryCount
        || !memory.TryRead(_dataAddress, _buffer)
      )
        return false;

      var words = MemoryMarshal.Cast<byte, int>(_buffer.AsSpan());
      var found = new Dictionary<int, int>();
      for (var i = 0; i < words.Length; i += WordsPerEntry)
      {
        if (words[i] < 0)
          continue;

        var key = words[i + 2];
        if (key is < 0 or > MaxBindingId)
          return false;

        found[key] = words[i + 3];
      }

      if (!HasValidKeyCode(found, OsuBinding.OsuLeft)
        || !HasValidKeyCode(found, OsuBinding.OsuRight)
        || !found.ContainsKey((int)OsuBinding.TaikoInnerLeft))
        return false;

      bindings = found;
      return true;
    }

    private static bool HasValidKeyCode(Dictionary<int, int> entries, OsuBinding binding) =>
      entries.TryGetValue((int)binding, out var keyCode) && OsuBindingCatalog.IsValidKeyCode(keyCode);
  }
}

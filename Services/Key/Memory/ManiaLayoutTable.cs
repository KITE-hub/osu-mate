using System.Runtime.InteropServices;
using System.Windows.Forms;
using OsuMate.Models;

namespace OsuMate.Services.Key.Memory
{

  internal sealed class ManiaLayoutTable
  {
    internal const int TotalElementCount = 38;
    internal const int Dimension0Length = 19;
    internal const int Dimension1Length = 2;

    private const int LayoutColumn = 0;
    private const int MinAcceptedLayoutCount = 4;

    private const uint Dim0LengthOffset = 0x8;
    private const uint Dim1LengthOffset = 0xC;
    private const uint ElementsOffset = 0x18;

    private const uint StageListOffset = 0x4;
    private const uint StageKeyCountOffset = 0x8;

    private const uint ListItemsOffset = 0x4;
    private const uint ListSizeOffset = 0xC;

    private const uint LayoutKeysArrayOffset = 0x4;

    private const uint ArrayLengthOffset = 0x4;
    private const uint ArrayElementsOffset = 0x8;

    private readonly uint _arrayAddress;

    private ManiaLayoutTable(uint arrayAddress)
    {
      _arrayAddress = arrayAddress;
    }

    internal uint Address => _arrayAddress;

    internal static bool TryCreate(
      RemoteProcessMemory memory,
      uint arrayAddress,
      out ManiaLayoutTable? table
    )
    {
      table = null;
      if (!RemoteProcessMemory.IsValidPointer(arrayAddress))
        return false;

      var candidate = new ManiaLayoutTable(arrayAddress);
      if (!candidate.TryRead(memory, out var layouts) || layouts.Count < MinAcceptedLayoutCount)
        return false;

      table = candidate;
      return true;
    }

    internal bool TryRead(RemoteProcessMemory memory, out Dictionary<int, Keys[]> layouts)
    {
      layouts = [];
      if (!HasExpectedDimensions(memory))
        return false;

      Span<byte> pointerBuffer = stackalloc byte[TotalElementCount * sizeof(uint)];
      if (!memory.TryRead(_arrayAddress + ElementsOffset, pointerBuffer))
        return false;

      var stagePointers = MemoryMarshal.Cast<byte, uint>(pointerBuffer);
      for (var keyCount = OsuMode.ManiaMinKeyCount; keyCount <= OsuMode.ManiaMaxKeyCount; keyCount++)
      {
        var stagePointer = stagePointers[keyCount * Dimension1Length + LayoutColumn];
        if (RemoteProcessMemory.IsValidPointer(stagePointer) && TryReadStageLayout(memory, stagePointer, keyCount, out var keys))
          layouts[keyCount] = keys;
      }

      return layouts.Count > 0;
    }

    private bool HasExpectedDimensions(RemoteProcessMemory memory) =>
      memory.TryReadInt32(_arrayAddress + Dim0LengthOffset, out var dim0)
      && dim0 == Dimension0Length
      && memory.TryReadInt32(_arrayAddress + Dim1LengthOffset, out var dim1)
      && dim1 == Dimension1Length;

    private static bool TryReadStageLayout(
      RemoteProcessMemory memory,
      uint stageAddress,
      int expectedKeyCount,
      out Keys[] keys
    )
    {
      keys = [];
      return TryReadListAddress(memory, stageAddress, expectedKeyCount, out var listAddress)
        && TryReadFirstLayoutAddress(memory, listAddress, out var layoutAddress)
        && TryReadKeys(memory, layoutAddress, expectedKeyCount, out keys);
    }

    private static bool TryReadListAddress(
      RemoteProcessMemory memory,
      uint stageAddress,
      int expectedKeyCount,
      out uint listAddress
    )
    {
      listAddress = 0;
      return memory.TryReadUInt32(stageAddress + StageListOffset, out listAddress)
        && RemoteProcessMemory.IsValidPointer(listAddress)
        && memory.TryReadInt32(stageAddress + StageKeyCountOffset, out var keyCount)
        && keyCount == expectedKeyCount;
    }

    private static bool TryReadFirstLayoutAddress(
      RemoteProcessMemory memory,
      uint listAddress,
      out uint layoutAddress
    )
    {
      layoutAddress = 0;
      return memory.TryReadUInt32(listAddress + ListItemsOffset, out var itemsArrayAddress)
        && RemoteProcessMemory.IsValidPointer(itemsArrayAddress)
        && memory.TryReadInt32(listAddress + ListSizeOffset, out var listSize)
        && listSize >= 1
        && memory.TryReadUInt32(itemsArrayAddress + ArrayElementsOffset, out layoutAddress)
        && RemoteProcessMemory.IsValidPointer(layoutAddress);
    }

    private static bool TryReadKeys(
      RemoteProcessMemory memory,
      uint layoutAddress,
      int expectedKeyCount,
      out Keys[] keys
    )
    {
      keys = [];
      if (
        !memory.TryReadUInt32(layoutAddress + LayoutKeysArrayOffset, out var keysArrayAddress)
        || !RemoteProcessMemory.IsValidPointer(keysArrayAddress)
        || !memory.TryReadInt32(keysArrayAddress + ArrayLengthOffset, out var keysLength)
        || keysLength < expectedKeyCount
      )
        return false;

      Span<byte> keysBuffer = stackalloc byte[expectedKeyCount * sizeof(int)];
      if (!memory.TryRead(keysArrayAddress + ArrayElementsOffset, keysBuffer))
        return false;

      var keyCodes = MemoryMarshal.Cast<byte, int>(keysBuffer);
      var result = new Keys[expectedKeyCount];
      for (var i = 0; i < result.Length; i++)
      {
        if (!OsuBindingCatalog.IsValidKeyCode(keyCodes[i]))
          return false;

        result[i] = (Keys)keyCodes[i];
      }

      keys = result;
      return true;
    }
  }
}

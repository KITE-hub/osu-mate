using System;
using System.IO;

namespace OsuMate.Services.StableDb
{
  public abstract class OsuBinaryReader : IDisposable
  {
    private readonly Stream _stream;
    private readonly byte[] _primitiveBuffer = new byte[8];

    protected OsuBinaryReader(Stream stream)
    {
      _stream = stream;
    }

    protected byte ReadByte()
    {
      ReadExact(_primitiveBuffer, 1);
      return _primitiveBuffer[0];
    }

    protected ushort ReadUInt16()
    {
      ReadExact(_primitiveBuffer, 2);
      return BitConverter.ToUInt16(_primitiveBuffer, 0);
    }

    protected int ReadInt32()
    {
      ReadExact(_primitiveBuffer, 4);
      return BitConverter.ToInt32(_primitiveBuffer, 0);
    }

    protected long ReadInt64()
    {
      ReadExact(_primitiveBuffer, 8);
      return BitConverter.ToInt64(_primitiveBuffer, 0);
    }

    protected float ReadSingle()
    {
      ReadExact(_primitiveBuffer, 4);
      return BitConverter.ToSingle(_primitiveBuffer, 0);
    }

    protected double ReadDouble()
    {
      ReadExact(_primitiveBuffer, 8);
      return BitConverter.ToDouble(_primitiveBuffer, 0);
    }

    protected string? ReadString()
    {
      byte marker = ReadByte();
      if (marker == 0x00)
      {
        return null;
      }
      if (marker != 0x0b)
      {
        string offsetInfo = _stream.CanSeek ? $" at offset {_stream.Position - 1}" : string.Empty;
        throw new InvalidDataException($"Unexpected string marker 0x{marker:x2}{offsetInfo}.");
      }

      int length = ReadULeb128();
      if (length < 0)
        throw new InvalidDataException("Negative string length.");
      if (length == 0)
        return string.Empty;

      var bytes = new byte[length];
      ReadExact(bytes, length);
      return System.Text.Encoding.UTF8.GetString(bytes);
    }

    protected int ReadULeb128()
    {
      int result = 0;
      int shift = 0;
      while (true)
      {
        byte b = ReadByte();
        result |= (b & 0x7f) << shift;
        if ((b & 0x80) == 0)
        {
          break;
        }
        shift += 7;
        if (shift > 28)
          throw new InvalidDataException("ULEB128 value is too large.");
      }
      return result;
    }

    protected void Skip(int count)
    {
      if (count < 0)
        throw new InvalidDataException("Negative skip length.");
      if (count == 0)
        return;

      if (_stream.CanSeek)
      {
        if (_stream.Position + count > _stream.Length)
          throw new InvalidDataException("Unexpected end of osu database file.");
        _stream.Seek(count, SeekOrigin.Current);
        return;
      }

      var discardBuffer = new byte[Math.Min(count, 8192)];
      int remaining = count;
      while (remaining > 0)
      {
        int chunk = Math.Min(discardBuffer.Length, remaining);
        int read = _stream.Read(discardBuffer, 0, chunk);
        if (read <= 0)
          throw new InvalidDataException("Unexpected end of osu database file.");
        remaining -= read;
      }
    }

    private void ReadExact(byte[] buffer, int count)
    {
      int total = 0;
      while (total < count)
      {
        int read = _stream.Read(buffer, total, count - total);
        if (read <= 0)
          throw new InvalidDataException("Unexpected end of osu database file.");
        total += read;
      }
    }

    public void Dispose()
    {
      _stream.Dispose();
    }
  }
}

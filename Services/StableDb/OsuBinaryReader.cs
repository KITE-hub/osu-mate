using System;
using System.IO;

namespace OsuMate.Services.StableDb
{
    /// <summary>
    /// osu!.db / scores.db のバイナリ形式に共通するプリミティブ読み取りヘルパー
    /// </summary>
    public abstract class OsuBinaryReader
    {
        protected readonly byte[] _data;
        protected int _offset;

        protected OsuBinaryReader(byte[] data)
        {
            _data = data;
            _offset = 0;
        }

        protected byte ReadByte() => _data[_offset++];

        protected ushort ReadUInt16()
        {
            ushort v = BitConverter.ToUInt16(_data, _offset);
            _offset += 2;
            return v;
        }

        protected int ReadInt32()
        {
            int v = BitConverter.ToInt32(_data, _offset);
            _offset += 4;
            return v;
        }

        protected long ReadInt64()
        {
            long v = BitConverter.ToInt64(_data, _offset);
            _offset += 8;
            return v;
        }

        protected float ReadSingle()
        {
            float v = BitConverter.ToSingle(_data, _offset);
            _offset += 4;
            return v;
        }

        protected double ReadDouble()
        {
            double v = BitConverter.ToDouble(_data, _offset);
            _offset += 8;
            return v;
        }

        /// <summary>
        /// osu! 独自の ULEB128 + UTF8 文字列形式を読む。
        /// 先頭マーカー: 0x00 = null, 0x0b = 文字列あり(続けて ULEB128 長 + UTF8バイト列)。
        /// </summary>
        protected string? ReadString()
        {
            byte marker = ReadByte();
            if (marker == 0x00)
            {
                return null;
            }
            if (marker != 0x0b)
            {
                throw new InvalidDataException(
                    $"Unexpected string marker 0x{marker:x2} at offset {_offset - 1}");
            }

            int length = ReadULeb128();
            string s = System.Text.Encoding.UTF8.GetString(_data, _offset, length);
            _offset += length;
            return s;
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
            }
            return result;
        }
    }
}

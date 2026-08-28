using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkyEditor.SaveEditor
{
    /// <summary>
    /// A block of data designed for reading data from non-byte-aligned addresses
    /// </summary>
    public class BitBlock : IClonable, IEnumerable<bool>
    {
        public BitBlock()
        {
            Position = 0;
            Bits = new List<bool>();
        }

        /// <param name="length">Length of the bit block, in bits</param>
        public BitBlock(int length)
        {
            Position = 0;
            Bits = new List<bool>(length);
            for (int i = 0; i < length; i++)
            {
                Bits.Add(false);
            }
        }

        public BitBlock(BitBlock source)
        {
            Position = 0;
            Bits = source.Bits.ToList(); // Clone the source
        }

        public BitBlock(IEnumerable<bool> source)
        {
            Position = 0;
            Bits = source.ToList();
        }

        public BitBlock(IEnumerable<byte> source)
        {
            Position = 0;
            // Unpack into a right-sized array first, then let the List constructor bulk-copy it:
            // growing a List bit-by-bit from a 128KB save means ~1M Add calls plus ~20 backing
            // array regrowths, which used to be roughly half the cost of loading a save.
            if (source is byte[] || source is ICollection<byte>)
            {
                var bytes = source as byte[] ?? System.Linq.Enumerable.ToArray(source);
                var buffer = new bool[bytes.Length * 8];
                for (int i = 0; i < bytes.Length; i++)
                {
                    var value = bytes[i];
                    var offset = i * 8;
                    for (int b = 0; b < 8; b++)
                    {
                        buffer[offset + b] = ((value >> b) & 1) == 1;
                    }
                }
                Bits = new List<bool>(buffer);
            }
            else
            {
                Bits = new List<bool>();
                foreach (var item in source)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        Bits.Add(((item >> b) & 1) == 1);
                    }
                }
            }
        }

        public List<bool> Bits { get; set; }

        public int Position { get; set; }

        public int Count => Bits.Count;

        public bool this[int index]
        {
            get
            {
                return Bits[index];
            }
            set
            {
                Bits[index] = value;
            }
        }

        public int GetInt(int byteIndex, int bitIndex, int bitLength)
        {
            var bits = Bits;
            var offset = byteIndex * 8 + bitIndex;
            int output = 0;
            for (int b = 0; b < bitLength; b++)
            {
                output |= (bits[offset + b] ? 1 : 0) << b;
            }
            return output;
        }

        public void SetInt(int byteIndex, int bitIndex, int bitLength, int value)
        {
            SetBitsLsbFirst(byteIndex * 8 + bitIndex, Math.Min(bitLength, 32), (uint)value);
        }

        // All the Set{Int,UInt,Short,UShort} overloads write the value's bits LSB-first, at most
        // the value's own width. Shared here so none of them allocates a BitConverter buffer per
        // call (the save path serializes thousands of small fields).
        private void SetBitsLsbFirst(int bitOffset, int bitLength, uint value)
        {
            var bits = Bits;
            for (int b = 0; b < bitLength; b++)
            {
                bits[bitOffset + b] = ((value >> b) & 1) == 1;
            }
        }

        public int GetNextInt(int bitLength)
        {
            var output = GetInt(0, Position, bitLength);
            Position += bitLength;
            return output;
        }

        public void SetNextInt(int bitLength, int value)
        {
            SetInt(0, Position, bitLength, value);
            Position += bitLength;
        }

        public uint GetUInt(int byteIndex, int bitIndex, int bitLength)
        {
            var bits = Bits;
            var offset = byteIndex * 8 + bitIndex;
            uint output = 0;
            for (int b = 0; b < bitLength; b++)
            {
                output |= (uint)((bits[offset + b] ? 1 : 0) << b);
            }
            return output;
        }

        public void SetUInt(int byteIndex, int bitIndex, int bitLength, uint value)
        {
            SetBitsLsbFirst(byteIndex * 8 + bitIndex, Math.Min(bitLength, 32), value);
        }

        public uint GetNextUInt(int bitLength)
        {
            var output = GetUInt(0, Position, bitLength);
            Position += bitLength;
            return output;
        }

        public void SetNextUInt(int bitLength, uint value)
        {
            SetUInt(0, Position, bitLength, value);
            Position += bitLength;
        }

        public short GetShort(int byteIndex, int bitIndex, int bitLength)
        {
            short output = 0;
            for (int b = 0; b < bitLength; b++)
            {
                output |= (short)((Bits[byteIndex * 8 + bitIndex + b] ? 1 : 0) << b);
            }
            return output;
        }

        public void SetShort(int byteIndex, int bitIndex, int bitLength, short value)
        {
            SetBitsLsbFirst(byteIndex * 8 + bitIndex, Math.Min(bitLength, 16), (ushort)value);
        }

        public short GetNextShort(int bitLength)
        {
            var output = GetShort(0, Position, bitLength);
            Position += bitLength;
            return output;
        }

        public void SetNextShort(int bitLength, short value)
        {
            SetShort(0, Position, bitLength, value);
            Position += bitLength;
        }

        public ushort GetUShort(int byteIndex, int bitIndex, int bitLength)
        {
            ushort output = 0;
            for (int b = 0; b < bitLength; b++)
            {
                output |= (ushort)((Bits[byteIndex * 8 + bitIndex + b] ? 1 : 0) << b);
            }
            return output;
        }

        // Previously wrote all 16 bits regardless of bitLength; it had no callers anywhere in
        // the solution, so it now respects bitLength like the other setters.
        public void SetUShort(int byteIndex, int bitIndex, int bitLength, ushort value)
        {
            SetBitsLsbFirst(byteIndex * 8 + bitIndex, Math.Min(bitLength, 16), value);
        }

        public uint GetNextUShort(int bitLength)
        {
            var output = GetUShort(0, Position, bitLength);
            Position += bitLength;
            return output;
        }

        public void SetNextUShort(int bitLength, ushort value)
        {
            SetUInt(0, Position, bitLength, value);
            Position += bitLength;
        }

        public BitBlock GetRange(int bitIndex, int bitLength)
        {
            var buffer = new bool[bitLength];
            Bits.CopyTo(bitIndex, buffer, 0, bitLength);
            return new BitBlock(buffer);
        }

        public void SetRange(int bitIndex, int bitLength, BitBlock value)
        {
            var destination = Bits;
            var source = value.Bits;
            for (int i = 0; i < bitLength; i++)
            {
                destination[bitIndex + i] = source[i];
            }
        }

        /// <summary>
        /// Copies a bit range to another position within this same block, without the
        /// intermediate allocations of GetRange+SetRange. Ranges are assumed not to overlap.
        /// </summary>
        public void CopyRangeWithin(int sourceBitIndex, int destinationBitIndex, int bitLength)
        {
            var bits = Bits;
            for (int i = 0; i < bitLength; i++)
            {
                bits[destinationBitIndex + i] = bits[sourceBitIndex + i];
            }
        }

        public void SetRange(int bitIndex, BitBlock value)
        {
            SetRange(bitIndex, value.Count, value);
        }

        public BitBlock GetNextRange(int bitLength)
        {
            var buffer = new bool[bitLength];
            Bits.CopyTo(Position, buffer, 0, bitLength);
            Position += bitLength;
            return new BitBlock(buffer);
        }

        public void SetNextRange(int bitLength, BitBlock value)
        {
            SetRange(Position, bitLength, value);
            Position += bitLength;
        }

        public void SetNextRange(BitBlock value)
        {
            SetNextRange(value.Count, value);
        }

        public List<byte> ToByteList()
        {
            var bits = Bits;
            var byteCount = bits.Count / 8;
            var output = new List<byte>(byteCount);
            for (int i = 0; i < byteCount; i++)
            {
                var offset = i * 8;
                var value = 0;
                for (int b = 0; b < 8; b++)
                {
                    if (bits[offset + b])
                    {
                        value |= 1 << b;
                    }
                }
                output.Add((byte)value);
            }
            return output;
        }

        public byte[] ToByteArray()
        {
            return ToByteList().ToArray();
        }

        public void AppendByte(byte source)
        {
            for (int i = 0; i < 8; i++)
            {
                Bits.Add(((source >> i) & 1) != 0);
            }
        }

        public string GetString(int bitIndex, int byteLength, Encoding charEncoding)
        {
            return charEncoding.GetString(GetRange(bitIndex, byteLength * 8).ToByteArray(), 0, byteLength);
        }

        public void SetString(int bitIndex, int byteLength, Encoding charEncoding, string value)
        {
            if (value == null)
            {
                value = string.Empty;
            }
            var buffer = charEncoding.GetBytes(value);
            for (int i = 0; i < byteLength; i++)
            {
                if (value.Length > i)
                {
                    SetInt(0, i * 8 + bitIndex, 8, buffer[i]);
                }
                else
                {
                    SetInt(0, i * 8 + bitIndex, 8, 0);
                }
            }
        }

        /// <summary>
        /// Gets a representation of the binary
        /// </summary>
        /// <returns>A string representing the binary</returns>
        /// <remarks>Example: A 5-Bit <see cref="Binary"/> representing the number 8 will return "1000"</remarks>
        public string GetBigEndianStringRepresentation()
        {
            var builder = new StringBuilder();
            for (int i = Bits.Count - 1; i >= 0; i -= 1)
            {
                if (Bits[i])
                {
                    builder.Append("1");
                }
                else
                {
                    builder.Append("0");
                }
            }
            return builder.ToString();
        }

        public object Clone()
        {
            return new BitBlock(this);
        }

        public IEnumerator<bool> GetEnumerator()
        {
            return Bits.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Bits.GetEnumerator();
        }
    }
}

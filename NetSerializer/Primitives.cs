/*
 * Copyright 2015 Tomi Valkeinen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Unicode;
using System.Buffers.Binary;
using System.Buffers;

namespace NetSerializer
{
	public static class Primitives
	{
		private const int StringByteBufferLength = 256;
		private const int StringCharBufferLength = 128;

		public static MethodInfo GetWritePrimitive(Type type)
		{
			return typeof(Primitives).GetMethod("WritePrimitive",
				BindingFlags.Static | BindingFlags.Public | BindingFlags.ExactBinding, null,
				new Type[] { typeof(Stream), type }, null);
		}

		public static MethodInfo GetReaderPrimitive(Type type)
		{
			return typeof(Primitives).GetMethod("ReadPrimitive",
				BindingFlags.Static | BindingFlags.Public | BindingFlags.ExactBinding, null,
				new Type[] { typeof(Stream), type.MakeByRefType() }, null);
		}

		static uint EncodeZigZag32(int n)
		{
			return (uint)((n << 1) ^ (n >> 31));
		}

		static ulong EncodeZigZag64(long n)
		{
			return (ulong)((n << 1) ^ (n >> 63));
		}

		static int DecodeZigZag32(uint n)
		{
			return (int)(n >> 1) ^ -(int)(n & 1);
		}

		static long DecodeZigZag64(ulong n)
		{
			return (long)(n >> 1) ^ -(long)(n & 1);
		}

		static uint ReadVarint32(Stream stream)
		{
			int result = 0;
			int offset = 0;

			for (; offset < 32; offset += 7)
			{
				int b = stream.ReadByte();
				if (b == -1)
					throw new EndOfStreamException();

				result |= (b & 0x7f) << offset;

				if ((b & 0x80) == 0)
					return (uint)result;
			}

			throw new InvalidDataException();
		}

		static void WriteVarint32(Stream stream, uint value)
		{
			for (; value >= 0x80u; value >>= 7)
				stream.WriteByte((byte)(value | 0x80u));

			stream.WriteByte((byte)value);
		}

		static ulong ReadVarint64(Stream stream)
		{
			long result = 0;
			int offset = 0;

			for (; offset < 64; offset += 7)
			{
				int b = stream.ReadByte();
				if (b == -1)
					throw new EndOfStreamException();

				result |= ((long)(b & 0x7f)) << offset;

				if ((b & 0x80) == 0)
					return (ulong)result;
			}

			throw new InvalidDataException();
		}

		static void WriteVarint64(Stream stream, ulong value)
		{
			for (; value >= 0x80u; value >>= 7)
				stream.WriteByte((byte)(value | 0x80u));

			stream.WriteByte((byte)value);
		}


		public static void WritePrimitive(Stream stream, bool value)
		{
			stream.WriteByte(value ? (byte)1 : (byte)0);
		}

		public static void ReadPrimitive(Stream stream, out bool value)
		{
			var b = stream.ReadByte();
			if (b < 0)
				throw new EndOfStreamException();
			value = b != 0;
		}

		public static void WritePrimitive(Stream stream, byte value)
		{
			stream.WriteByte(value);
		}

		public static void ReadPrimitive(Stream stream, out byte value)
		{
			var b = stream.ReadByte();
			if (b < 0)
				throw new EndOfStreamException();
			value = (byte)b;
		}

		public static void WritePrimitive(Stream stream, sbyte value)
		{
			stream.WriteByte((byte)value);
		}

		public static void ReadPrimitive(Stream stream, out sbyte value)
		{
			var b = stream.ReadByte();
			if (b < 0)
				throw new EndOfStreamException();
			value = (sbyte)b;
		}

		public static void WritePrimitive(Stream stream, char value)
		{
			WriteVarint32(stream, value);
		}

		public static void ReadPrimitive(Stream stream, out char value)
		{
			value = (char)ReadVarint32(stream);
		}

		public static void WritePrimitive(Stream stream, ushort value)
		{
			WriteVarint32(stream, value);
		}

		public static void ReadPrimitive(Stream stream, out ushort value)
		{
			value = (ushort)ReadVarint32(stream);
		}

		public static void WritePrimitive(Stream stream, short value)
		{
			WriteVarint32(stream, EncodeZigZag32(value));
		}

		public static void ReadPrimitive(Stream stream, out short value)
		{
			value = (short)DecodeZigZag32(ReadVarint32(stream));
		}

		public static void WritePrimitive(Stream stream, uint value)
		{
			WriteVarint32(stream, value);
		}

		public static void ReadPrimitive(Stream stream, out uint value)
		{
			value = ReadVarint32(stream);
		}

		public static void WritePrimitive(Stream stream, int value)
		{
			WriteVarint32(stream, EncodeZigZag32(value));
		}

		public static void ReadPrimitive(Stream stream, out int value)
		{
			value = DecodeZigZag32(ReadVarint32(stream));
		}

		public static void WritePrimitive(Stream stream, ulong value)
		{
			WriteVarint64(stream, value);
		}

		public static void ReadPrimitive(Stream stream, out ulong value)
		{
			value = ReadVarint64(stream);
		}

		public static void WritePrimitive(Stream stream, long value)
		{
			WriteVarint64(stream, EncodeZigZag64(value));
		}

		public static void ReadPrimitive(Stream stream, out long value)
		{
			value = DecodeZigZag64(ReadVarint64(stream));
		}

		public static unsafe void WritePrimitive(Stream stream, float value)
		{
			uint v = *(uint*)(&value);
			WriteUInt32(stream, v);
		}

		public static unsafe void ReadPrimitive(Stream stream, out float value)
		{
			uint v = ReadUInt32(stream);
			value = *(float*)(&v);
		}

		public static unsafe void WritePrimitive(Stream stream, double value)
		{
			ulong v = *(ulong*)(&value);
			WriteUInt64(stream, v);
		}

		public static unsafe void ReadPrimitive(Stream stream, out double value)
		{
			ulong v = ReadUInt64(stream);
			value = *(double*)(&v);
		}

        public static void WritePrimitive(Stream stream, Half value)
        {
            ushort v = Unsafe.As<Half, ushort>(ref value);
            WriteUInt16(stream, v);
        }

        public static void ReadPrimitive(Stream stream, out Half value)
        {
            var v = ReadUInt16(stream);
            value = Unsafe.As<ushort, Half>(ref v);
        }


		private static void WriteUInt16(Stream stream, ushort value)
	    {
	        stream.WriteByte((byte) value);
	        stream.WriteByte((byte) (value >> 8));
	    }

		private static ushort ReadUInt16(Stream stream)
		{
            ushort a = 0;

            for (var i = 0; i < 16; i += 8)
            {
                var val = stream.ReadByte();
                if (val == -1)
                    throw new EndOfStreamException();

                a |= (ushort) (val << i);
            }

            return a;
		}

		// 32 and 64 bit variants use stackalloc when everything is available since it's faster.

		private static void WriteUInt32(Stream stream, uint value)
		{
			Span<byte> buf = stackalloc byte[4];
			BinaryPrimitives.WriteUInt32LittleEndian(buf, value);

			stream.Write(buf);
		}

		private static void WriteUInt64(Stream stream, ulong value)
		{
			Span<byte> buf = stackalloc byte[8];
			BinaryPrimitives.WriteUInt64LittleEndian(buf, value);

			stream.Write(buf);
		}

		private static uint ReadUInt32(Stream stream)
		{
			Span<byte> buf = stackalloc byte[4];
			var wSpan = buf;

			while (true)
			{
				var read = stream.Read(wSpan);
				if (read == 0)
					throw new EndOfStreamException();
				if (read == wSpan.Length)
					break;
				wSpan = wSpan[read..];
			}

			return BinaryPrimitives.ReadUInt32LittleEndian(buf);
		}

		private static ulong ReadUInt64(Stream stream)
		{
			Span<byte> buf = stackalloc byte[8];
			var wSpan = buf;

			while (true)
			{
				var read = stream.Read(wSpan);
				if (read == 0)
					throw new EndOfStreamException();
				if (read == wSpan.Length)
					break;
				wSpan = wSpan[read..];
			}

			return BinaryPrimitives.ReadUInt64LittleEndian(buf);
		}

		public static void WritePrimitive(Stream stream, DateTime value)
		{
			long v = value.ToBinary();
			WritePrimitive(stream, v);
		}

		public static void ReadPrimitive(Stream stream, out DateTime value)
		{
			long v;
			ReadPrimitive(stream, out v);
			try
			{
				value = DateTime.FromBinary(v);
			}
			catch (ArgumentException e)
			{
				throw new InvalidDataException("Invalid DateTime", e);
			}
		}

		[ThreadStatic]
		static int[] s_decimalBitsArray;

		public static void WritePrimitive(Stream stream, decimal value)
		{
			int[] bits = Decimal.GetBits(value);

			ulong low = (uint)bits[0];
			ulong mid = ((ulong)(uint)bits[1]) << 32;
			ulong lowmid = low | mid;

			uint high = (uint)bits[2];

			uint scale = ((uint)bits[3] >> 15) & 0x01fe;
			uint sign = ((uint)bits[3]) >> 31;
			uint scaleSign = scale | sign;

			WritePrimitive(stream, lowmid);
			WritePrimitive(stream, high);
			WritePrimitive(stream, scaleSign);
		}

		public static void ReadPrimitive(Stream stream, out decimal value)
		{
			ulong lowmid;
			uint high, scaleSign;

			ReadPrimitive(stream, out lowmid);
			ReadPrimitive(stream, out high);
			ReadPrimitive(stream, out scaleSign);

			int scale = (int)((scaleSign & ~1) << 15);
			int sign = (int)((scaleSign & 1) << 31);

			var arr = s_decimalBitsArray;
			if (arr == null)
				arr = s_decimalBitsArray = new int[4];

			arr[0] = (int)lowmid;
			arr[1] = (int)(lowmid >> 32);
			arr[2] = (int)high;
			arr[3] = scale | sign;

			try
			{
				value = new Decimal(arr);
			}
			catch (ArgumentException e)
			{
				// Invalid scale or reserved bits.
				throw new InvalidDataException("Invalid decimal", e);
			}
		}

		public static void WritePrimitive(Stream stream, string value)
		{
			if (value == null)
			{
				WritePrimitive(stream, (uint)0);
				return;
			}

			if (value.Length == 0)
			{
				WritePrimitive(stream, (uint)1);
				return;
			}

			Span<byte> buf = stackalloc byte[StringByteBufferLength];

			var totalChars = value.Length;
			var totalBytes = Encoding.UTF8.GetByteCount(value);

			WritePrimitive(stream, (uint)totalBytes + 1);
			WritePrimitive(stream, (uint)totalChars);

			var totalRead = 0;
			ReadOnlySpan<char> span = value;
			for (;;)
			{
				var finalChunk = totalRead + totalChars >= totalChars;
				Utf8.FromUtf16(span, buf, out var read, out var wrote, isFinalBlock: finalChunk);
				stream.Write(buf.Slice(0, wrote));
				totalRead += read;
				if (read >= totalChars)
					break;

				span = span[read..];
				totalChars -= read;
			}
		}

		// We cache the delegate in a static here to avoid a delegate allocation on every call to ReadPrimitive.
		private static readonly SpanAction<char, (int, Stream)> _stringSpanRead = StringSpanRead;

		public static void ReadPrimitive(Stream stream, out string value)
		{
			ReadString(null, stream, out value);
		}

		/// <param name="serializer">
		/// When given, the memory of the string counts toward <see cref="Settings.MaxCollectionBytes"/>. Without it the
		/// only limits are the ones that can be derived from the stream itself.
		/// </param>
		internal static void ReadString(Serializer serializer, Stream stream, out string value)
		{
			ReadPrimitive(stream, out uint totalBytes);

			if (totalBytes == 0)
			{
				value = null;
				return;
			}

			if (totalBytes == 1)
			{
				value = string.Empty;
				return;
			}

			totalBytes -= 1;

			ReadPrimitive(stream, out uint totalChars);

			Helpers.ValidateStringLength(stream, totalBytes, totalChars);

			// Two bytes per UTF-16 char. On streams of unknown length the declared size is the only thing bounding
			// the allocation, so it has to be charged before the string is created.
			if (serializer != null)
				Helpers.ChargeAllocation(serializer, (ulong)totalChars * 2);

			value = string.Create((int) totalChars, ((int) totalBytes, stream), _stringSpanRead);
		}

		private static void StringSpanRead(Span<char> span, (int totalBytes, Stream stream) tuple)
		{
			Span<byte> buf = stackalloc byte[StringByteBufferLength];

			var (totalBytes, stream) = tuple;

			var totalBytesRead = 0;
			var totalCharsRead = 0;
			var writeBufStart = 0;

			while (totalBytesRead < totalBytes)
			{
				var bytesLeft = totalBytes - totalBytesRead;
				var bytesReadLeft = Math.Min(buf.Length, bytesLeft);
				var writeSlice = buf.Slice(writeBufStart, bytesReadLeft - writeBufStart);
				var bytesInBuffer = stream.Read(writeSlice);
				if (bytesInBuffer == 0)
					throw new EndOfStreamException();

				var readFromStream = bytesInBuffer + writeBufStart;
				var final = readFromStream == bytesLeft;
				var status = Utf8.ToUtf16(buf[..readFromStream], span[totalCharsRead..], out var bytesRead, out var charsRead, isFinalBlock: final);

				totalBytesRead += bytesRead;
				totalCharsRead += charsRead;
				writeBufStart = 0;

				if (status == OperationStatus.DestinationTooSmall)
				{
					// Malformed data?
					throw new InvalidDataException();
				}

				if (status == OperationStatus.NeedMoreData)
				{
					// We got cut short in the middle of a multi-byte UTF-8 sequence.
					// So we need to move it to the bottom of the span, then read the next bit *past* that.
					// This copy should be fine because we're only ever gonna be copying up to 4 bytes
					// from the end of the buffer to the start.
					// So no chance of overlap.
					buf[bytesRead..].CopyTo(buf);
					writeBufStart = readFromStream - bytesRead;
					continue;
				}

				Debug.Assert(status == OperationStatus.Done);
			}

			// The declared character count must match what the bytes actually decoded to,
			// otherwise the tail of the string would be left as uninitialized '\0' characters.
			if (totalCharsRead != span.Length)
				throw new InvalidDataException("String declares more characters than its bytes decode to");
		}

		public static void WritePrimitive(Stream stream, byte[] value)
		{
			if (value == null)
			{
				WritePrimitive(stream, (uint)0);
				return;
			}

			WritePrimitive(stream, (uint)value.Length + 1);

			stream.Write(value, 0, value.Length);
		}

		static readonly byte[] s_emptyByteArray = new byte[0];

		public static void ReadPrimitive(Stream stream, out byte[] value)
		{
			uint len;
			ReadPrimitive(stream, out len);

			if (len == 0)
			{
				value = null;
				return;
			}
			else if (len == 1)
			{
				value = s_emptyByteArray;
				return;
			}

			len -= 1;

			if (stream.CanSeek)
			{
				// Each byte takes one byte, so the declared length can never exceed what is left in the stream.
				if (len > stream.Length - stream.Position)
					throw new EndOfStreamException("Declared length exceeds the remaining stream length");

				value = new byte[len];
				int l = 0;

				while (l < len)
				{
					int r = stream.Read(value, l, (int)len - l);
					if (r == 0)
						throw new EndOfStreamException();
					l += r;
				}

				return;
			}

			// The stream length is unknown, so don't trust the declared length for the allocation.
			// Grow the buffer as data actually arrives, so the sender has to supply the bytes it wants us to allocate.
			var buf = new byte[Math.Min(len, 8192u)];
			var read = 0;

			while (read < len)
			{
				if (read == buf.Length)
					Array.Resize(ref buf, (int)Math.Min(len, (long)buf.Length * 2));

				int r = stream.Read(buf, read, buf.Length - read);
				if (r == 0)
					throw new EndOfStreamException();
				read += r;
			}

			value = buf;
		}
	}
}

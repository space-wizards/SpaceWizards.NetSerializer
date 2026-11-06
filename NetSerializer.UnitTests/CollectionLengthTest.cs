using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace NetSerializer.UnitTests
{
	[TestFixture]
	public class CollectionLengthTest
	{
		[Serializable]
		struct EmptyStruct
		{
		}

		[Serializable]
		struct IntPair
		{
			public int A;
			public int B;
		}

		/// <summary>Stream wrapper that cannot seek, like a network stream.</summary>
		sealed class NonSeekableStream : Stream
		{
			readonly Stream _inner;

			public NonSeekableStream(Stream inner) => _inner = inner;

			public override bool CanRead => true;
			public override bool CanSeek => false;
			public override bool CanWrite => false;
			public override long Length => throw new NotSupportedException();
			public override long Position
			{
				get => throw new NotSupportedException();
				set => throw new NotSupportedException();
			}

			public override void Flush() { }
			public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		}

		static MemoryStream DeclaredLength(uint declaredLength)
		{
			// Array/list lengths are encoded as (length + 1).
			var stream = new MemoryStream();
			Primitives.WritePrimitive(stream, declaredLength + 1);
			stream.Position = 0;
			return stream;
		}

		static long Allocated(Action action)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			var before = GC.GetAllocatedBytesForCurrentThread();
			action();
			return GC.GetAllocatedBytesForCurrentThread() - before;
		}

		[Test]
		public void ArrayDeclaringMoreThanStreamHoldsDoesNotAllocate()
		{
			var serializer = new Serializer(new[] { typeof(int[]) });
			var stream = DeclaredLength(100_000_000);

			var allocated = Allocated(() =>
				Assert.Throws<EndOfStreamException>(() => serializer.DeserializeDirect<int[]>(stream, out _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}

		[Test]
		public void ListDeclaringMoreThanStreamHoldsDoesNotAllocate()
		{
			var serializer = new Serializer(new[] { typeof(List<int>) });
			var stream = DeclaredLength(100_000_000);

			var allocated = Allocated(() =>
				Assert.Throws<EndOfStreamException>(() => serializer.DeserializeDirect<List<int>>(stream, out _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}

		[Test]
		public void StructArrayDeclaringMoreThanStreamHoldsDoesNotAllocate()
		{
			var serializer = new Serializer(new[] { typeof(IntPair[]) });
			var stream = DeclaredLength(50_000_000);

			var allocated = Allocated(() =>
				Assert.Throws<EndOfStreamException>(() => serializer.DeserializeDirect<IntPair[]>(stream, out _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}

		[Test]
		public void ZeroSizedElementsAreCappedBySetting()
		{
			var serializer = new Serializer(new[] { typeof(EmptyStruct[]) });
			var stream = DeclaredLength(100_000_000);

			var allocated = Allocated(() =>
				Assert.Throws<InvalidDataException>(() => serializer.DeserializeDirect<EmptyStruct[]>(stream, out _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}

#pragma warning disable CS0649
		[Serializable]
		struct WideNullable
		{
			public long? A, B, C, D, E, F, G, H;
		}
#pragma warning restore CS0649

		static MemoryStream ZeroElements(int count)
		{
			// A declared length followed by `count` zero bytes: each zero is a null string, or a Nullable without a value.
			var stream = new MemoryStream();
			Primitives.WritePrimitive(stream, (uint)count + 1);
			stream.Write(new byte[count]);
			stream.Position = 0;
			return stream;
		}

		[Test]
		public void ByteBudgetBoundsElementMemory()
		{
			var serializer = new Serializer(new[] { typeof(string[]) }, new Settings { MaxCollectionBytes = 1000 });

			// 200 string references = 1600 bytes in memory, from a 201-byte payload.
			Assert.Throws<InvalidDataException>(() =>
				serializer.DeserializeDirect<string[]>(ZeroElements(200), out _));

			// 100 string references = 800 bytes, within budget.
			serializer.DeserializeDirect<string[]>(ZeroElements(100), out var ok);
			Assert.That(ok, Has.Length.EqualTo(100));
		}

		[Test]
		public void DefaultByteBudgetStopsWideElementAmplification()
		{
			var serializer = new Serializer(new[] { typeof(WideNullable[]) });

			// 2M elements of 128 bytes each = 256 MB in memory, from a 16 MB payload
			// (eight one-byte "no value" flags per element).
			var declared = new MemoryStream();
			Primitives.WritePrimitive(declared, 2_000_001u);
			declared.Write(new byte[16_000_000]);
			declared.Position = 0;

			var allocated = Allocated(() =>
				Assert.Throws<InvalidDataException>(() => serializer.DeserializeDirect<WideNullable[]>(declared, out _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}

		[Test]
		public void ByteBudgetDoesNotAffectNormalSizedData()
		{
			var serializer = new Serializer(new[] { typeof(int[]), typeof(List<string>) });

			serializer.DeserializeDirect<int[]>(ZeroElements(1_000_000), out var ints);
			Assert.That(ints, Has.Length.EqualTo(1_000_000));

			serializer.DeserializeDirect<List<string>>(ZeroElements(1_000_000), out var strings);
			Assert.That(strings, Has.Count.EqualTo(1_000_000));
		}

		[Test]
		public void NestedCollectionHeadersAreBoundedByTotalBudget()
		{
			var serializer = new Serializer(new[] { typeof(object[]) });
			var id = serializer.GetTypeMap()[typeof(object[])];

			// 250 nested object[] of 100,000 elements each. Every level fits the remaining stream, because its parent
			// has not read its elements yet, so only a running total stops this (250 x 800 KB = 200 MB from about 100 KB).
			const int levels = 250, count = 100_000;
			var stream = new MemoryStream();
			for (var i = 0; i < levels; i++)
			{
				Primitives.WritePrimitive(stream, id);
				Primitives.WritePrimitive(stream, (uint)count + 1);
			}
			stream.Write(new byte[count]);
			stream.Position = 0;

			var allocated = Allocated(() =>
				Assert.Throws<InvalidDataException>(() => serializer.Deserialize(stream)));

			// Default budget is 64 MiB; allow some slack for the objects themselves.
			Assert.That(allocated, Is.LessThan(100_000_000));
		}

		[Test]
		public void BudgetIsSharedBySiblingCollectionsInOneCall()
		{
			var serializer = new Serializer(new[] { typeof(string[][]) }, new Settings { MaxCollectionBytes = 1000 });

			static MemoryStream Siblings(int inner)
			{
				var stream = new MemoryStream();
				Primitives.WritePrimitive(stream, (uint)inner + 1);
				for (var i = 0; i < inner; i++)
				{
					Primitives.WritePrimitive(stream, 101u); // 100 string references = 800 bytes
					stream.Write(new byte[100]);
				}
				stream.Position = 0;
				return stream;
			}

			serializer.DeserializeDirect<string[][]>(Siblings(1), out var one);
			Assert.That(one, Has.Length.EqualTo(1));

			Assert.Throws<InvalidDataException>(() => serializer.DeserializeDirect<string[][]>(Siblings(2), out _));
		}

		[Test]
		public void BudgetResetsForEachTopLevelCall()
		{
			var serializer = new Serializer(new[] { typeof(string[]) }, new Settings { MaxCollectionBytes = 1000 });

			// Each call allocates 800 bytes, which is within the budget on its own. Repeating must not accumulate.
			for (var i = 0; i < 5; i++)
				serializer.DeserializeDirect<string[]>(ZeroElements(100), out _);

			Assert.Throws<InvalidDataException>(() => serializer.DeserializeDirect<string[]>(ZeroElements(200), out _));
			serializer.DeserializeDirect<string[]>(ZeroElements(100), out _);
		}

		[Test]
		public void CapIsConfigurable()
		{
			var serializer = new Serializer(new[] { typeof(EmptyStruct[]) },
				new Settings { MaxCollectionLength = 10 });

			Assert.Throws<InvalidDataException>(() =>
				serializer.DeserializeDirect<EmptyStruct[]>(DeclaredLength(11), out _));

			Assert.DoesNotThrow(() =>
				serializer.DeserializeDirect<EmptyStruct[]>(DeclaredLength(10), out _));
		}

		[Test]
		public void NonSeekableStreamIsCappedBySetting()
		{
			var serializer = new Serializer(new[] { typeof(int[]) });
			var stream = new NonSeekableStream(DeclaredLength(100_000_000));

			var allocated = Allocated(() =>
				Assert.Throws<InvalidDataException>(() => serializer.DeserializeDirect<int[]>(stream, out _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}

		[Test]
		public void LegitimateDataStillRoundTrips()
		{
			var serializer = new Serializer(new[]
			{
				typeof(int[]), typeof(List<string>), typeof(IntPair[]), typeof(EmptyStruct[]), typeof(string),
				typeof(Dictionary<string, int>), typeof(HashSet<int>), typeof(byte[])
			});

			static T RoundTrip<T>(Serializer s, T value, bool seekable)
			{
				var stream = new MemoryStream();
				s.SerializeDirect(stream, value);
				stream.Position = 0;
				Stream input = seekable ? stream : new NonSeekableStream(stream);
				s.DeserializeDirect<T>(input, out var result);
				return result;
			}

			foreach (var seekable in new[] { true, false })
			{
				Assert.That(RoundTrip(serializer, new[] { 1, 2, 3, -4 }, seekable), Is.EqualTo(new[] { 1, 2, 3, -4 }));
				Assert.That(RoundTrip(serializer, new List<string> { "a", "", "héllo \U0001F600" }, seekable),
					Is.EqualTo(new List<string> { "a", "", "héllo \U0001F600" }));
				Assert.That(RoundTrip(serializer, new[] { new IntPair { A = 1, B = 2 } }, seekable),
					Is.EqualTo(new[] { new IntPair { A = 1, B = 2 } }));
				Assert.That(RoundTrip(serializer, new EmptyStruct[5], seekable).Length, Is.EqualTo(5));
				Assert.That(RoundTrip(serializer, new int[0], seekable), Is.Empty);
				Assert.That(RoundTrip(serializer, (int[])null, seekable), Is.Null);
				Assert.That(RoundTrip(serializer, new Dictionary<string, int> { ["x"] = 1 }, seekable),
					Is.EqualTo(new Dictionary<string, int> { ["x"] = 1 }));
				Assert.That(RoundTrip(serializer, new HashSet<int> { 1, 2 }, seekable), Is.EquivalentTo(new[] { 1, 2 }));
				Assert.That(RoundTrip(serializer, new byte[100_000], seekable).Length, Is.EqualTo(100_000));
			}
		}

		[Test]
		public void ByteArrayDeclaringMoreThanStreamHoldsDoesNotAllocate()
		{
			var serializer = new Serializer(new[] { typeof(byte[]) });

			foreach (var seekable in new[] { true, false })
			{
				Stream stream = DeclaredLength(2_000_000_000);
				if (!seekable)
					stream = new NonSeekableStream(stream);

				var allocated = Allocated(() =>
					Assert.Throws<EndOfStreamException>(() => serializer.DeserializeDirect<byte[]>(stream, out _)));

				Assert.That(allocated, Is.LessThan(1_000_000), $"seekable: {seekable}");
			}
		}

		[Test]
		public void LargeByteArrayRoundTripsOnNonSeekableStream()
		{
			var serializer = new Serializer(new[] { typeof(byte[]) });
			var data = new byte[3_000_000];
			new Random(1).NextBytes(data);

			var stream = new MemoryStream();
			serializer.SerializeDirect(stream, data);
			stream.Position = 0;

			serializer.DeserializeDirect<byte[]>(new NonSeekableStream(stream), out var result);
			Assert.That(result, Is.EqualTo(data));
		}

		[Test]
		public void TruncatedByteBoolAndSbyteReadsThrow()
		{
			Assert.Throws<EndOfStreamException>(() => Primitives.ReadPrimitive(new MemoryStream(), out byte _));
			Assert.Throws<EndOfStreamException>(() => Primitives.ReadPrimitive(new MemoryStream(), out sbyte _));
			Assert.Throws<EndOfStreamException>(() => Primitives.ReadPrimitive(new MemoryStream(), out bool _));
		}

		[Serializable]
		struct Tail
		{
			public int Value;
			public bool Flag;
			public sbyte Small;
		}

		[Test]
		public void TruncatedStructEndingInByteSizedFieldsThrows()
		{
			var serializer = new Serializer(new[] { typeof(Tail) });

			var full = new MemoryStream();
			serializer.SerializeDirect(full, new Tail { Value = 7, Flag = true, Small = -3 });
			var bytes = full.ToArray();

			// Every proper prefix is truncated data and must be rejected, not padded with 0xFF.
			for (var cut = 0; cut < bytes.Length; cut++)
			{
				var truncated = new MemoryStream(bytes, 0, cut);
				Assert.Throws<EndOfStreamException>(() => serializer.DeserializeDirect<Tail>(truncated, out _), $"cut {cut}");
			}

			serializer.DeserializeDirect<Tail>(new MemoryStream(bytes), out var ok);
			Assert.That(ok.Value, Is.EqualTo(7));
			Assert.That(ok.Flag, Is.True);
			Assert.That(ok.Small, Is.EqualTo(-3));
		}

		[Test]
		public void BoolArrayDeclaringDataThatNeverArrivesThrows()
		{
			var serializer = new Serializer(new[] { typeof(bool[]) });
			var stream = new NonSeekableStream(DeclaredLength(10_000_000));

			Assert.Throws<EndOfStreamException>(() => serializer.DeserializeDirect<bool[]>(stream, out _));
		}

		[Test]
		public void UnknownTypeIdThrowsInvalidData()
		{
			var serializer = new Serializer(new[] { typeof(string) });

			foreach (var id in new uint[] { 50, 100_000, uint.MaxValue - 1 })
			{
				var stream = new MemoryStream();
				Primitives.WritePrimitive(stream, id);
				stream.Position = 0;

				Assert.Throws<InvalidDataException>(() => serializer.Deserialize(stream), $"id {id}");
			}
		}

		static MemoryStream StringPayload(uint totalBytes, uint totalChars, params byte[] bytes)
		{
			var stream = new MemoryStream();
			Primitives.WritePrimitive(stream, totalBytes + 1);
			Primitives.WritePrimitive(stream, totalChars);
			stream.Write(bytes);
			stream.Position = 0;
			return stream;
		}

		[Test]
		public void StringDeclaringMoreCharsThanItDecodesToIsRejected()
		{
			// "é" is 2 bytes but 1 char; declaring 2 chars would leave a trailing '\0'.
			Assert.Throws<InvalidDataException>(() =>
				Primitives.ReadPrimitive(StringPayload(2, 2, 0xC3, 0xA9), out string _));

			// The honest encoding still works.
			Primitives.ReadPrimitive(StringPayload(2, 1, 0xC3, 0xA9), out string ok);
			Assert.That(ok, Is.EqualTo("é"));
		}

		[Test]
		public void StringDeclaringZeroCharsWithBytesIsRejected()
		{
			Assert.Throws<InvalidDataException>(() =>
				Primitives.ReadPrimitive(StringPayload(2, 0, (byte)'a', (byte)'b'), out string _));
		}

		[Test]
		public void StringDeclaringMoreCharsThanBytesIsRejected()
		{
			var stream = new MemoryStream();
			Primitives.WritePrimitive(stream, 3u); // 2 bytes (+1)
			Primitives.WritePrimitive(stream, 500_000_000u); // chars
			stream.WriteByte((byte)'a');
			stream.WriteByte((byte)'b');
			stream.Position = 0;

			var allocated = Allocated(() =>
				Assert.Throws<InvalidDataException>(() => Primitives.ReadPrimitive(stream, out string _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}

		[Test]
		public void StringDeclaringMoreBytesThanStreamHoldsIsRejected()
		{
			var stream = new MemoryStream();
			Primitives.WritePrimitive(stream, 500_000_001u); // bytes (+1)
			Primitives.WritePrimitive(stream, 500_000_000u); // chars
			stream.Position = 0;

			var allocated = Allocated(() =>
				Assert.Throws<EndOfStreamException>(() => Primitives.ReadPrimitive(stream, out string _)));

			Assert.That(allocated, Is.LessThan(1_000_000));
		}
	}
}

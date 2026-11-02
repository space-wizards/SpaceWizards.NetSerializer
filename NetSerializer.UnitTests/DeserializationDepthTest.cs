using System.IO;
using NUnit.Framework;

namespace NetSerializer.UnitTests
{
	[TestFixture]
	public class DeserializationDepthTest
	{
		static object Nest(int depth)
		{
			object ob = "leaf";
			for (var i = 0; i < depth; i++)
				ob = new object[] { ob };
			return ob;
		}

		static object RoundTrip(Serializer serializer, object ob)
		{
			var stream = new MemoryStream();
			serializer.Serialize(stream, ob);
			stream.Position = 0;
			return serializer.Deserialize(stream);
		}

		[Test]
		public void NestingWithinLimitRoundTrips()
		{
			var serializer = new Serializer(new[] { typeof(object[]), typeof(string) });

			var result = RoundTrip(serializer, Nest(100));

			for (var i = 0; i < 100; i++)
				result = ((object[])result)[0];

			Assert.That(result, Is.EqualTo("leaf"));
		}

		[Test]
		public void ExcessiveNestingThrowsInsteadOfOverflowingStack()
		{
			var serializer = new Serializer(new[] { typeof(object[]), typeof(string) });
			var single = new MemoryStream();
			serializer.Serialize(single, new object[] { null });
			var bytes = single.ToArray();
			var prefix = bytes[..^1];

			var payload = new MemoryStream();
			for (var i = 0; i < 1_000_000; i++)
				payload.Write(prefix);
			payload.WriteByte(0);
			payload.Position = 0;

			Assert.Throws<InvalidDataException>(() => serializer.Deserialize(payload));
		}

		[Test]
		public void LimitIsConfigurableAndDepthRecoversAfterFailure()
		{
			var serializer = new Serializer(new[] { typeof(object[]), typeof(string) },
				new Settings { MaxDeserializationDepth = 10 });

			Assert.Throws<InvalidDataException>(() => RoundTrip(serializer, Nest(50)));
			Assert.DoesNotThrow(() => RoundTrip(serializer, Nest(5)));
		}
	}
}

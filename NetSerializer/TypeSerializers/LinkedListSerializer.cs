using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NetSerializer;

internal sealed class LinkedListSerializer : IStaticTypeSerializer
{
	public bool Handles(Type type)
	{
		return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LinkedList<>);
	}

	public IEnumerable<Type> GetSubtypes(Type type)
	{
		return [type.GetGenericArguments()[0].MakeArrayType()];
	}

	public MethodInfo GetStaticWriter(Type type)
	{
		var elementType = type.GetGenericArguments()[0];

		return typeof(LinkedListSerializer)
			.GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic)!
			.MakeGenericMethod(elementType);
	}

	public MethodInfo GetStaticReader(Type type)
	{
		var elementType = type.GetGenericArguments()[0];

		return typeof(LinkedListSerializer)
			.GetMethod("Deserialize", BindingFlags.Static | BindingFlags.NonPublic)!
			.MakeGenericMethod(elementType);
	}

	// ReSharper disable once UnusedMember.Local
	private static void Serialize<T>(Serializer serializer, Stream stream, LinkedList<T> ob)
	{
		var array = ob == null ? null : ob.ToArray();

		serializer.Serialize(stream, array);
	}

	// ReSharper disable once UnusedMember.Local
	private static void Deserialize<T>(Serializer serializer, Stream stream, out LinkedList<T> ob)
	{
		var array = Helpers.CastDeserialized<T[]>(serializer.Deserialize(stream));

		// A null array is a null list
		ob = array == null ? null : new LinkedList<T>(array);
	}
}

/*
 * Copyright 2015 Tomi Valkeinen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NetSerializer
{
	sealed class ObjectSerializer : IStaticTypeSerializer
	{
		[ThreadStatic]
		static int s_depth;

		public bool Handles(Type type)
		{
			return type == typeof(object);
		}

		public IEnumerable<Type> GetSubtypes(Type type)
		{
			return new Type[0];
		}

		public MethodInfo GetStaticWriter(Type type)
		{
			return typeof(ObjectSerializer).GetMethod("Serialize", BindingFlags.Static | BindingFlags.Public);
		}

		public MethodInfo GetStaticReader(Type type)
		{
			return typeof(ObjectSerializer).GetMethod("Deserialize", BindingFlags.Static | BindingFlags.Public);
		}

		public static void Serialize(Serializer serializer, Stream stream, object ob)
		{
			if (ob == null)
			{
				Primitives.WritePrimitive(stream, (uint)0);
				return;
			}

			var type = ob.GetType();

			SerializeDelegate<object> del;

			uint id = serializer.GetTypeIdAndSerializer(type, out del);

			Primitives.WritePrimitive(stream, id);

			if (id == Serializer.ObjectTypeId)
				return;

			del(serializer, stream, ob);
		}

		public static void Deserialize(Serializer serializer, Stream stream, out object ob)
		{
			uint id;

			Primitives.ReadPrimitive(stream, out id);

			if (id == 0)
			{
				ob = null;
				return;
			}

			if (id == Serializer.ObjectTypeId)
			{
				ob = new object();
				return;
			}

			var del = serializer.GetDeserializeTrampolineFromId(id);

			s_depth++;

			try
			{
				if (s_depth > serializer.Settings.MaxDeserializationDepth)
					throw new InvalidDataException("Maximum deserialization depth exceeded");

				del(serializer, stream, out ob);
			}
			finally
			{
				s_depth--;
			}
		}

		public static void DeserializeTyped<T>(Serializer serializer, Stream stream, out T value)
		{
			// Check before assigning it. This just ensures we get an InvalidCastException out instead of sending bad data.
			Deserialize(serializer, stream, out var ob);

			if (ob is T typed)
			{
				value = typed;
			}
			else if (ob == null && default(T) == null)
			{
				value = default;
			}
			else
			{
				throw new InvalidDataException(
					$"Expected {typeof(T)} but the data contained {(ob == null ? "null" : ob.GetType().ToString())}");
			}
		}
	}
}

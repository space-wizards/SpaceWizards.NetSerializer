/*
 * Copyright 2015 Tomi Valkeinen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace NetSerializer
{
	static class Helpers
	{
		public static MethodInfo GetCheckedReader(Type type, TypeData data)
		{
			if (data.Type == type)
				return data.ReaderMethodInfo;

			Debug.Assert(data.Type == typeof(object));
			return typeof(ObjectSerializer).GetMethod(nameof(ObjectSerializer.DeserializeTyped))!
				.MakeGenericMethod(type);
		}

		/// <summary>
		/// Validate a declared array/collection length before allocating storage for it.
		/// Called from generated reader code.
		/// </summary>
		/// <param name="elementsNeverEmpty">True if every element is guaranteed to serialize to at least one byte.</param>
		/// <param name="elementSize">In-memory size of one element, in bytes.</param>
		public static void ValidateCollectionLength(Serializer serializer, Stream stream, uint count, bool elementsNeverEmpty, int elementSize)
		{
			if (elementsNeverEmpty && stream.CanSeek)
			{
				var remaining = stream.Length - stream.Position;
				if (count > remaining)
					throw new EndOfStreamException("Declared length exceeds the remaining stream length");
			}
			else if (count > (uint)Math.Max(serializer.Settings.MaxCollectionLength, 0))
			{
				throw new InvalidDataException("Maximum collection length exceeded");
			}

			// The length is plausible for the stream, but elements can be much larger in memory than on the wire.
			// Nested collections each fit the remaining stream (their parents have not consumed their elements yet).
			// So without a running total a small payload could allocate the budget once per nesting level
			ChargeAllocation(serializer, (ulong)count * (uint)Math.Max(elementSize, 1));
		}

		/// <summary>
		/// Add an allocation to the running total of the current top-level deserialize call and throw if the total
		/// exceeds <see cref="Settings.MaxCollectionBytes"/>.
		/// </summary>
		public static void ChargeAllocation(Serializer serializer, ulong bytes)
		{
			var total = bytes;
			if (s_deserializeDepth > 0)
				total += (ulong)s_collectionBytes;

			if (total > (ulong)Math.Max(serializer.Settings.MaxCollectionBytes, 0))
				throw new InvalidDataException("Maximum collection size exceeded");

			if (s_deserializeDepth > 0)
				s_collectionBytes = (long)total;
		}

		// Allocation accounting for the current top-level Deserialize/DeserializeDirect call on this thread.
		[ThreadStatic] static int s_deserializeDepth;
		[ThreadStatic] static long s_collectionBytes;

		/// <summary>
		/// Mark the start of a public deserialize call. Only the outermost call resets the allocation total, so
		/// nested calls made by readers (HashSet, Dictionary, ...) share the budget of the call that started them.
		/// </summary>
		public static void EnterDeserialize()
		{
			if (s_deserializeDepth++ == 0)
				s_collectionBytes = 0;
		}

		public static void ExitDeserialize()
		{
			s_deserializeDepth--;
		}

		/// <summary>
		/// Validate the character/byte counts declared by a string before allocating the string.
		/// </summary>
		public static void ValidateStringLength(Stream stream, uint totalBytes, uint totalChars)
		{
			// Strings and their byte counts are handled as int downstream.
			if (totalBytes > int.MaxValue)
				throw new InvalidDataException("String is too long");

			// Callers handle null and empty strings before this point, so there is at least one byte and one char.
			// (A zero char count would also make string.Create skip the read, leaving the bytes unconsumed.)
			if (totalChars == 0)
				throw new InvalidDataException("String declares no characters");

			// UTF-8 never encodes a UTF-16 code unit in less than one byte.
			if (totalChars > totalBytes)
				throw new InvalidDataException("String declares more characters than bytes");

			if (stream.CanSeek && totalBytes > stream.Length - stream.Position)
				throw new EndOfStreamException("Declared string length exceeds the remaining stream length");
		}

		/// <summary>
		/// Returns true if values of the type, as an array/collection element, always serialize to at least one byte.
		/// Conservative: returns false when unsure.
		/// </summary>
		public static bool ElementsNeverEmpty(Serializer serializer, Type type)
		{
			return NeverEmpty(serializer, type, new HashSet<Type>());
		}

		static bool NeverEmpty(Serializer serializer, Type type, HashSet<Type> visiting)
		{
			// Same lookup as Serializer.GetTypeSerializer: the first custom serializer that handles the type wins.
			// NetSerializer can't know what a custom serializer writes, unless it says so.
			var custom = serializer.Settings.CustomTypeSerializers.FirstOrDefault(s => s.Handles(type));
			if (custom != null)
				return custom is IMinimumSerializedSize size && size.GetMinimumSerializedSize(type) >= 1;

			// Arrays, strings and reference types (which go through ObjectSerializer and write a type id).
			if (!type.IsValueType)
				return true;

			if (type.IsPrimitive || type.IsEnum)
				return true;

			if (type.IsGenericType)
			{
				var def = type.GetGenericTypeDefinition();
				if (def == typeof(System.Collections.Immutable.ImmutableArray<>) || def == typeof(NetListAsArray<>))
					return true;
			}

			if (!type.IsSerializable || !visiting.Add(type))
				return false;

			try
			{
				return GetFieldInfos(type).Any(f => NeverEmpty(serializer, f.FieldType, visiting));
			}
			finally
			{
				visiting.Remove(type);
			}
		}

		/// <summary>
		/// Cast a freshly deserialized object to the type a reader expects.
		/// A mismatch means malformed data.
		/// </summary>
		public static T CastDeserialized<T>(object ob) where T : class
		{
			if (ob == null)
				return null;

			if (ob is T typed)
				return typed;

			throw new InvalidDataException($"Expected {typeof(T)} but the data contained {ob.GetType()}");
		}

		public static IEnumerable<FieldInfo> GetFieldInfos(Type type)
		{
			Debug.Assert(type.IsSerializable, $"Type {type} is not marked as serializable");

			var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(fi => (fi.Attributes & FieldAttributes.NotSerialized) == 0)
				.OrderBy(f => f.Name, StringComparer.Ordinal);

			if (type.BaseType == null)
			{
				return fields;
			}
			else
			{
				var baseFields = GetFieldInfos(type.BaseType);
				return baseFields.Concat(fields);
			}
		}

		public static DynamicMethod GenerateDynamicSerializerStub(Type type)
		{
			var dm = new DynamicMethod("Serialize", null,
				new Type[] { typeof(Serializer), typeof(Stream), type },
				typeof(Serializer), true);

			dm.DefineParameter(1, ParameterAttributes.None, "serializer");
			dm.DefineParameter(2, ParameterAttributes.None, "stream");
			dm.DefineParameter(3, ParameterAttributes.None, "value");

			return dm;
		}

		public static DynamicMethod GenerateDynamicDeserializerStub(Type type)
		{
			var dm = new DynamicMethod("Deserialize", null,
				new Type[] { typeof(Serializer), typeof(Stream), type.MakeByRefType() },
				typeof(Serializer), true);
			dm.DefineParameter(1, ParameterAttributes.None, "serializer");
			dm.DefineParameter(2, ParameterAttributes.None, "stream");
			dm.DefineParameter(3, ParameterAttributes.Out, "value");

			return dm;
		}

#if GENERATE_DEBUGGING_ASSEMBLY
		public static MethodBuilder GenerateStaticSerializerStub(TypeBuilder tb, Type type)
		{
			var mb = tb.DefineMethod("Serialize", MethodAttributes.Public | MethodAttributes.Static, null,
				new Type[] { typeof(Serializer), typeof(Stream), type });
			mb.DefineParameter(1, ParameterAttributes.None, "serializer");
			mb.DefineParameter(2, ParameterAttributes.None, "stream");
			mb.DefineParameter(3, ParameterAttributes.None, "value");
			return mb;
		}

		public static MethodBuilder GenerateStaticDeserializerStub(TypeBuilder tb, Type type)
		{
			var mb = tb.DefineMethod("Deserialize", MethodAttributes.Public | MethodAttributes.Static, null,
				new Type[] { typeof(Serializer), typeof(Stream), type.MakeByRefType() });
			mb.DefineParameter(1, ParameterAttributes.None, "serializer");
			mb.DefineParameter(2, ParameterAttributes.None, "stream");
			mb.DefineParameter(3, ParameterAttributes.Out, "value");
			return mb;
		}
#endif

		/// <summary>
		/// Create delegate that calls writer either directly, or via a trampoline
		/// </summary>
		public static Delegate CreateSerializeDelegate(Type paramType, TypeData data)
		{
			Type writerType = data.Type;

			if (paramType != writerType && paramType != typeof(object))
				throw new Exception();

			bool needTypeConv = paramType != writerType;
			bool needsInstanceParameter = data.WriterNeedsInstance;

			var delegateType = typeof(SerializeDelegate<>).MakeGenericType(paramType);

			// Can we call the writer directly?

			if (!needTypeConv && needsInstanceParameter)
			{
				var dynamicWriter = data.WriterMethodInfo as DynamicMethod;

				if (dynamicWriter != null)
					return dynamicWriter.CreateDelegate(delegateType);
				else
					return Delegate.CreateDelegate(delegateType, data.WriterMethodInfo);
			}

			// Create a trampoline

			var wrapper = GenerateDynamicSerializerStub(paramType);
			var il = wrapper.GetILGenerator();

			if (needsInstanceParameter)
				il.Emit(OpCodes.Ldarg_0);

			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldarg_2);
			if (needTypeConv)
				il.Emit(writerType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, writerType);

			// XXX tailcall causes slowdowns with large valuetypes
			//il.Emit(OpCodes.Tailcall);
			il.Emit(OpCodes.Call, data.WriterMethodInfo);

			il.Emit(OpCodes.Ret);

			return wrapper.CreateDelegate(delegateType);
		}

		/// <summary>
		/// Create delegate that calls reader either directly, or via a trampoline
		/// </summary>
		public static Delegate CreateDeserializeDelegate(Type paramType, TypeData data)
		{
			Type readerType = data.Type;

			if (paramType != readerType && paramType != typeof(object))
				throw new Exception();

			bool needTypeConv = paramType != readerType;
			bool needsInstanceParameter = data.ReaderNeedsInstance;

			var delegateType = typeof(DeserializeDelegate<>).MakeGenericType(paramType);

			// Can we call the reader directly?

			if (!needTypeConv && needsInstanceParameter)
			{
				var dynamicReader = data.ReaderMethodInfo as DynamicMethod;

				if (dynamicReader != null)
					return dynamicReader.CreateDelegate(delegateType);
				else
					return Delegate.CreateDelegate(delegateType, data.ReaderMethodInfo);
			}

			// Create a trampoline

			var wrapper = GenerateDynamicDeserializerStub(paramType);
			var il = wrapper.GetILGenerator();

			if (needsInstanceParameter)
				il.Emit(OpCodes.Ldarg_0);

			if (needTypeConv && readerType.IsValueType)
			{
				var local = il.DeclareLocal(readerType);

				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldloca_S, local);

				il.Emit(OpCodes.Call, data.ReaderMethodInfo);

				// write result object to out object
				il.Emit(OpCodes.Ldarg_2);
				il.Emit(OpCodes.Ldloc_0);
				il.Emit(OpCodes.Box, readerType);
				il.Emit(OpCodes.Stind_Ref);
			}
			else
			{
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldarg_2);

				// XXX tailcall causes slowdowns with large valuetypes
				//il.Emit(OpCodes.Tailcall);
				il.Emit(OpCodes.Call, data.ReaderMethodInfo);
			}

			il.Emit(OpCodes.Ret);

			return wrapper.CreateDelegate(delegateType);
		}

		public static MethodInfo GetGenReader(Type containerType, Type genType, string methodName = "ReadPrimitive")
		{
			var mis = containerType.GetMethods(BindingFlags.Static | BindingFlags.Public)
				.Where(mi => mi.IsGenericMethod && mi.Name == methodName);

			foreach (var mi in mis)
			{
				var p = mi.GetParameters();

				if (p.Length != 3)
					continue;

				if (p[1].ParameterType != typeof(Stream))
					continue;

				var paramType = p[2].ParameterType;

				if (paramType.IsByRef == false)
					continue;

				paramType = paramType.GetElementType();

				if (paramType.IsGenericType == false)
					continue;

				var genParamType = paramType.GetGenericTypeDefinition();

				if (genType == genParamType)
					return mi;
			}

			return null;
		}

		public static MethodInfo GetGenWriter(Type containerType, Type genType, string methodName = "WritePrimitive")
		{
			var mis = containerType.GetMethods(BindingFlags.Static | BindingFlags.Public)
				.Where(mi => mi.IsGenericMethod && mi.Name == methodName);

			foreach (var mi in mis)
			{
				var p = mi.GetParameters();

				if (p.Length != 3)
					continue;

				if (p[1].ParameterType != typeof(Stream))
					continue;

				var paramType = p[2].ParameterType;

				if (paramType.IsGenericType == false)
					continue;

				var genParamType = paramType.GetGenericTypeDefinition();

				if (genType == genParamType)
					return mi;
			}

			return null;
		}
	}
}

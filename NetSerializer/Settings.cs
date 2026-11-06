/*
 * Copyright 2016 Tomi Valkeinen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;

namespace NetSerializer
{
	public class Settings
	{
		/// <summary>
		/// Array of custom TypeSerializers
		/// </summary>
		public ITypeSerializer[] CustomTypeSerializers = new ITypeSerializer[0];

		/// <summary>
		/// Support IDeserializationCallback
		/// </summary>
		public bool SupportIDeserializationCallback = false;

		/// <summary>
		/// Support OnSerializing, OnSerialized, OnDeserializing, OnDeserialized attributes
		/// </summary>
		public bool SupportSerializationCallbacks = false;

		/// <summary>
		/// Maximum nesting depth of dynamically typed values while deserializing. Exceeding it throws InvalidDataException.
		/// </summary>
		public int MaxDeserializationDepth = 256;

		/// <summary>
		/// Maximum number of elements an array or collection may declare while deserializing, applied when the declared
		/// length cannot be checked against the data left in the stream.
		/// </summary>
		/// <remarks>
		/// When the stream is seekable and every element is guaranteed to take at least one byte, a declared length
		/// larger than the remaining stream is rejected and this limit is not used. Otherwise (non-seekable streams,
		/// or element types that can serialize to zero bytes, such as empty structs, or element types handled by a custom
		/// serializer that does not implement IMinimumSerializedSize) this limit applies instead.
		/// Exceeding it throws InvalidDataException.
		/// </remarks>
		public int MaxCollectionLength = 1 << 24;

		/// <summary>
		/// Maximum memory, in bytes, that the arrays, collections and strings of one Deserialize / DeserializeDirect call
		/// may allocate in total (declared length multiplied by the in-memory element size, two bytes per string
		/// character, summed over everything read by the call, including nested collections). Exceeding it throws
		/// InvalidDataException.
		/// </summary>
		/// <remarks>
		/// Elements can be much larger in memory than on the wire (a null reference or null Nullable takes one byte
		/// on the wire but eight or more in memory), and nested collections each look plausible for the remaining
		/// stream because their parents have not read their elements yet. Checking the declared length against the
		/// stream alone therefore still lets a small payload allocate far more than its own size. This limit bounds
		/// the total. byte[] is not counted, it is bounded by the data actually in the stream.
		/// </remarks>
		public int MaxCollectionBytes = 64 * 1024 * 1024;
	}
}

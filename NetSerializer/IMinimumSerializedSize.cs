/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;

namespace NetSerializer
{
	/// <summary>
	/// Optional interface for a custom type serializer (<see cref="Settings.CustomTypeSerializers"/>)
	/// </summary>
	/// <remarks>
	/// When reading an array or list, NetSerializer rejects a declared length that is larger than the bytes left in the
	/// stream, but only if every element takes at least one byte. It knows that for its own types. For a type handled by
	/// a custom serializer it can't tell, so by default such arrays are only limited by
	/// <see cref="Settings.MaxCollectionLength"/> and <see cref="Settings.MaxCollectionBytes"/>. Implementing this
	/// interface lets the stream check apply to those types too.
	/// </remarks>
	public interface IMinimumSerializedSize
	{
		/// <summary>
		/// Returns the smallest number of bytes that a serialized value of <paramref name="type"/> can take, for a type
		/// this serializer handles. Return 0 if it can be empty or you are not sure.
		/// </summary>
		int GetMinimumSerializedSize(Type type);
	}
}

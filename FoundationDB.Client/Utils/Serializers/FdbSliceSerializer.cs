﻿#region BSD Licence
/* Copyright (c) 2013, Doxense SARL
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
	* Redistributions of source code must retain the above copyright
	  notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright
	  notice, this list of conditions and the following disclaimer in the
	  documentation and/or other materials provided with the distribution.
	* Neither the name of Doxense nor the
	  names of its contributors may be used to endorse or promote products
	  derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
#endregion

namespace FoundationDB.Client.Serializers
{
	using FoundationDB.Client.Converters;
	using FoundationDB.Layers.Tuples;
	using System;

	public static class FdbSliceSerializer
	{

		public static FdbSliceSerializer<IFdbTuple> Tuple
		{
			get { return FdbSliceSerializer<IFdbTuple>.Default; }
		}

		public static FdbSliceSerializer<string> UnicodeString
		{
			get { return FdbSliceSerializer<string>.Default; }
		}

		public static FdbSliceSerializer<string> AsciiString
		{
			get { return new FdbSliceSerializer<string>((value) => Slice.FromAscii(value), (slice) => slice.ToAscii()); }
		}

		/// <summary>Simple serializer that can pack or unpack types that implement ISliceSerializable</summary>
		internal sealed class SerializableSerializer<T> : FdbSliceSerializer<T>
			where T : ISliceSerializable, new()
		{

			public SerializableSerializer()
				: base(
					(value) => value == null ? Slice.Nil : value.ToSlice(),
					(data) =>
					{
						var result = new T();
						result.FromSlice(data);
						return result;
					}
				)
			{ }

		}

		/// <summary>Create a new serializer from a two packing and unpacking lambdas</summary>
		public static FdbSliceSerializer<T> Create<T>(Func<T, Slice> pack, Func<Slice, T> unpack)
		{
			if (pack == null) throw new ArgumentNullException("pack");
			if (unpack == null) throw new ArgumentNullException("unpack");

			return new FdbSliceSerializer<T>(pack, unpack);
		}


		/// <summary>Convert a <typeparamref name="T"> into a slice, using a serializer (or the default serializer if none is provided)</summary>
		public static Slice ToSlice<T>(T value, ISliceSerializer<T> serializer = null)
		{
			if (serializer == null) serializer = FdbSliceSerializer<T>.Default;
			return serializer.ToSlice(value);
		}

		/// <summary>Convert an array of <typeparamref name="T">s into an array of  slices, using a serializer (or the default serializer if none is provided)</summary>
		public static Slice[] ToSlices<T>(T[] values, ISliceSerializer<T> serializer = null)
		{
			if (values == null) throw new ArgumentNullException("values");

			if (serializer == null) serializer = FdbSliceSerializer<T>.Default;

			var slices = new Slice[values.Length];
			for (int i = 0; i < values.Length; i++)
			{
				slices[i] = serializer.ToSlice(values[i]);
			}
			return slices;
		}

		/// <summary>Convert a slice back into a <typeparamref name="T"/>, using a serializer (or the default serializer if none is provided)</summary>
		public static T FromSlice<T>(Slice slice, ISliceSerializer<T> serializer = null)
		{
			if (serializer == null) serializer = FdbSliceSerializer<T>.Default;
			return serializer.FromSlice(slice);
		}

		/// <summary>Convert an array of slices back into an array of <typeparamref name="T"/>s, using a serializer (or the default serializer if none is provided)</summary>
		public static T[] FromSlices<T>(Slice[] slices, ISliceSerializer<T> serializer = null)
		{
			if (slices == null) throw new ArgumentNullException("values");

			if (serializer == null) serializer = FdbSliceSerializer<T>.Default;

			var values = new T[slices.Length];
			for (int i = 0; i < slices.Length; i++)
			{
				values[i] = serializer.FromSlice(slices[i]);
			}
			return values;
		}

	}
}

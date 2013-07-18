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

namespace FoundationDB.Layers.Tables
{
	using FoundationDB.Client;
	using FoundationDB.Layers.Tuples;
	using System;

	public static class FdbTableExtensions
	{

		public static FdbTable Table(this FdbDatabase db, string tableName)
		{
			return new FdbTable(tableName, db.Partition(tableName));
		}

		public static FdbTable Table(this FdbDatabase db, string tableName, IFdbTuple prefix)
		{
			return new FdbTable(tableName, db.Partition(prefix));
		}

		public static FdbTable<TKey, TValue> Table<TKey, TValue>(this FdbDatabase db, string tableName, ITupleKeyFormatter<TKey> keyReader, ISliceSerializer<TValue> valueSerializer)
		{
			return new FdbTable<TKey, TValue>(tableName, db.Partition(tableName), keyReader, valueSerializer);
		}

		public static FdbTable<TKey, TValue> Table<TKey, TValue>(this FdbDatabase db, string tableName, IFdbTuple prefix, ITupleKeyFormatter<TKey> keyReader, ISliceSerializer<TValue> valueSerializer)
		{
			return new FdbTable<TKey, TValue>(tableName, db.Partition(prefix), keyReader, valueSerializer);
		}



	}

}

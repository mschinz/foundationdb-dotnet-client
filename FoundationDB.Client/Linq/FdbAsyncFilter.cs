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

namespace FoundationDB.Linq
{
	using FoundationDB.Async;
	using FoundationDB.Client.Utils;
	using System;
	using System.Threading;
	using System.Threading.Tasks;

	internal abstract class FdbAsyncFilter<TSource, TResult> : FdbAsyncIterator<TResult>
	{
		/// <summary>Source sequence (when in iterable mode)</summary>
		protected IFdbAsyncEnumerable<TSource> m_source;

		/// <summary>Active iterator on the source (when in terator mode)</summary>
		protected IFdbAsyncEnumerator<TSource> m_iterator;

		protected FdbAsyncFilter(IFdbAsyncEnumerable<TSource> source)
		{
			Contract.Requires(source != null);
			m_source = source;
		}

		protected override Task<bool> OnFirstAsync(CancellationToken ct)
		{
			// on the first call to MoveNext, we have to hook up with the source iterator

			IFdbAsyncEnumerator<TSource> iterator = null;
			try
			{
				// filtering changes the number of items, so that means that, even if the underlying caller wants one item, we may need to read more.
				// => change all "Head" requests into "Iterator" to prevent any wrong optimizations by the underlying source (ex: using a too small batch size)
				var mode = m_mode;
				if (mode == FdbAsyncMode.Head) mode = FdbAsyncMode.Iterator;

				iterator = m_source.GetEnumerator(mode);
				return TaskHelpers.FromResult(iterator != null);
			}
			catch (Exception)
			{
				// whatever happens, make sure that we released the iterator...
				if (iterator != null)
				{
					iterator.Dispose();
					iterator = null;
				}
				throw;
			}
			finally
			{
				m_iterator = iterator;
			}
		}

		protected override void Cleanup()
		{
			try
			{
				var iterator = m_iterator;
				if (iterator != null)
				{
					iterator.Dispose();
				}
			}
			finally
			{
				m_iterator = null;
			}
		}

	}

}

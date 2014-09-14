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

namespace FoundationDB.Client.Tests
{
	using FoundationDB.Client;
	using FoundationDB.Filters.Logging;
	using FoundationDB.Layers.Directories;
	using FoundationDB.Layers.Tuples;
	using NUnit.Framework;
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;

	[TestFixture]
	public class DatabaseBulkFacts : FdbTest
	{

		[Test]
		public async Task Test_Can_Bulk_Insert_Raw_Data()
		{
			const int N = 20 * 1000;

			using (var db = await OpenTestPartitionAsync())
			{
				Console.WriteLine("Bulk inserting " + N + " random items...");

				var location = await GetCleanDirectory(db, "Bulk", "Write");

				var rnd = new Random(2403);
				var data = Enumerable.Range(0, N)
					.Select((x) => new KeyValuePair<Slice, Slice>(location.Pack(x.ToString("x8")), Slice.Random(rnd, 16 + rnd.Next(240))))
					.ToArray();

				Console.WriteLine("Total data size is " + data.Sum(x => x.Key.Count + x.Value.Count).ToString("N0") + " bytes");

				Console.WriteLine("Starting...");

				var sw = Stopwatch.StartNew();
				long? lastReport = null;
				int called = 0;
				long count = await Fdb.Bulk.WriteAsync(
					db,
					data,
					new Progress<long>((n) =>
					{
						++called;
						lastReport = n;
						Console.WriteLine("Chunk #" + called + " : " + n.ToString());
					}),
					this.Cancellation
				);
				sw.Stop();

				//note: calls to Progress<T> are async, so we need to wait a bit ...
				Thread.Sleep(640); // "Should be enough"

				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " secs and " + called + " chunks");

				Assert.That(count, Is.EqualTo(N), "count");
				Assert.That(lastReport, Is.EqualTo(N), "lastResport");
				Assert.That(called, Is.GreaterThan(0), "called");

				// read everything back...

				Console.WriteLine("Reading everything back...");

				var stored = await db.ReadAsync((tr) =>
				{
					return tr.GetRangeStartsWith(location).ToArrayAsync();
				}, this.Cancellation);

				Assert.That(stored.Length, Is.EqualTo(N));
				Assert.That(stored, Is.EqualTo(data));
			}
		}

		[Test]
		public async Task Test_Can_Bulk_Insert_Items()
		{
			const int N = 20 * 1000;

			using (var db = await OpenTestPartitionAsync())
			{
				db.DefaultTimeout = 60 * 1000;

				Console.WriteLine("Generating " + N + " random items...");

				var location = await GetCleanDirectory(db, "Bulk", "Insert");

				var rnd = new Random(2403);
				var data = Enumerable.Range(0, N)
					.Select((x) => new KeyValuePair<int, int>(x, 16 + (int)(Math.Pow(rnd.NextDouble(), 4) * 10 * 1000)))
					.ToList();

				long totalSize = data.Sum(x => (long)x.Value);
				Console.WriteLine("Total size is ~ " + totalSize.ToString("N0") + " bytes");

				Console.WriteLine("Starting...");

				long called = 0;
				var uniqueKeys = new HashSet<int>();
				var sw = Stopwatch.StartNew();
				long count = await Fdb.Bulk.InsertAsync(
					db,
					data,
					(kv, tr) =>
					{
						++called;
						uniqueKeys.Add(kv.Key);
						tr.Set(
							location.Pack(kv.Key),
							Slice.FromString(new string('A', kv.Value))
						);
					},
					this.Cancellation
				);
				sw.Stop();

				//note: calls to Progress<T> are async, so we need to wait a bit ...
				Thread.Sleep(640);   // "Should be enough"

				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " secs for " + count.ToString("N0") + " keys and " + totalSize.ToString("N0") + " bytes");
				Console.WriteLine("> Throughput " + (count / sw.Elapsed.TotalSeconds).ToString("N0") + " key/sec and " + (totalSize / (1048576 * sw.Elapsed.TotalSeconds)).ToString("N3") + " MB/sec");
				Console.WriteLine("Called " + called.ToString("N0") + " for " + uniqueKeys.Count.ToString("N0") + " unique keys");

				Assert.That(count, Is.EqualTo(N), "count");
				Assert.That(uniqueKeys.Count, Is.EqualTo(N), "unique keys");
				Assert.That(called, Is.EqualTo(N), "number of calls (no retries)");

				// read everything back...

				Console.WriteLine("Reading everything back...");

				var stored = await db.ReadAsync((tr) =>
				{
					return tr.GetRange(location.ToRange()).ToArrayAsync();
				}, this.Cancellation);

				Assert.That(stored.Length, Is.EqualTo(N), "DB contains less or more items than expected");
				for (int i = 0; i < stored.Length;i++)
				{
					Assert.That(stored[i].Key, Is.EqualTo(location.Pack(data[i].Key)), "Key #{0}", i);
					Assert.That(stored[i].Value.Count, Is.EqualTo(data[i].Value), "Value #{0}", i);
				}

				// cleanup because this test can produce a lot of data
				await location.RemoveAsync(db, this.Cancellation);
			}
		}

		[Test]
		public async Task Test_Can_Batch_ForEach_AsyncWithContextAndState()
		{
			const int N = 50 * 1000;

			using (var db = await OpenTestPartitionAsync())
			{

				Console.WriteLine("Bulk inserting " + N + " items...");
				var location = await GetCleanDirectory(db, "Bulk", "ForEach");

				Console.WriteLine("Preparing...");

				await Fdb.Bulk.WriteAsync(
					db,
					Enumerable.Range(1, N).Select((x) => new KeyValuePair<Slice, Slice>(location.Pack(x), Slice.FromInt32(x))),
					null,
					this.Cancellation
				);

				Console.WriteLine("Reading...");

				long total = 0;
				long count = 0;
				int chunks = 0;
				var sw = Stopwatch.StartNew();
				await Fdb.Bulk.ForEachAsync(
					db,
					Enumerable.Range(1, N).Select(x => location.Pack(x)),
					() => FdbTuple.Create(0L, 0L),
					async (xs, ctx, state) =>
					{
						Interlocked.Increment(ref chunks);
						Console.WriteLine("> Called with batch of " + xs.Length.ToString("N0") + " at offset " + ctx.Position.ToString("N0") + " of gen " + ctx.Generation + " with step " + ctx.Step + " and cooldown " + ctx.Cooldown + " (genElapsed=" + ctx.ElapsedGeneration + ", totalElapsed=" + ctx.ElapsedTotal + ")");

						var throttle = Task.Delay(TimeSpan.FromMilliseconds(10 + (xs.Length / 25) * 5)); // magic numbers to try to last longer than 5 sec
						var results = await ctx.Transaction.GetValuesAsync(xs);
						await throttle;

						long sum = 0;
						for (int i = 0; i < results.Length; i++)
						{
							sum += results[i].ToInt32();
						}
						return FdbTuple.Create(state.Item1 + sum, state.Item2 + results.Length);
					},
					(state) =>
					{
						Interlocked.Add(ref total, state.Item1);
						Interlocked.Add(ref count, state.Item2);
					},
					this.Cancellation
				);
				sw.Stop();

				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " seconds and " + chunks + " chunks");
				Console.WriteLine("Sum of integers 1 to " + count + " is " + total);

				// cleanup because this test can produce a lot of data
				await location.RemoveAsync(db, this.Cancellation);
			}
		}

		[Test]
		public async Task Test_Can_Batch_ForEach_WithContextAndState()
		{
			const int N = 50 * 1000;

			using (var db = await OpenTestPartitionAsync())
			{

				Console.WriteLine("Bulk inserting " + N + " items...");
				var location = await GetCleanDirectory(db, "Bulk", "ForEach");

				Console.WriteLine("Preparing...");

				await Fdb.Bulk.WriteAsync(
					db,
					Enumerable.Range(1, N).Select((x) => new KeyValuePair<Slice, Slice>(location.Pack(x), Slice.FromInt32(x))),
					null,
					this.Cancellation
				);

				Console.WriteLine("Reading...");

				long total = 0;
				long count = 0;
				int chunks = 0;
				var sw = Stopwatch.StartNew();
				await Fdb.Bulk.ForEachAsync(
					db,
					Enumerable.Range(1, N).Select(x => location.Pack(x)),
					() => FdbTuple.Create(0L, 0L), // (sum, count)
					(xs, ctx, state) =>
					{
						Interlocked.Increment(ref chunks);
						Console.WriteLine("> Called with batch of " + xs.Length.ToString("N0") + " at offset " + ctx.Position.ToString("N0") + " of gen " + ctx.Generation + " with step " + ctx.Step + " and cooldown " + ctx.Cooldown + " (gen=" + ctx.ElapsedGeneration + ", total=" + ctx.ElapsedTotal + ")");

						var t = ctx.Transaction.GetValuesAsync(xs);
						Thread.Sleep(TimeSpan.FromMilliseconds(10 + (xs.Length / 25) * 5)); // magic numbers to try to last longer than 5 sec
						var results = t.Result; // <-- this is bad practice, never do that in real life, 'mkay?

						long sum = 0;
						for (int i = 0; i < results.Length; i++)
						{
							sum += results[i].ToInt32();
						}
						return FdbTuple.Create(
							state.Item1 + sum, // updated sum
							state.Item2 + results.Length // updated count
						);
					},
					(state) =>
					{
						Interlocked.Add(ref total, state.Item1);
						Interlocked.Add(ref count, state.Item2);
					},
					this.Cancellation
				);
				sw.Stop();

				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " seconds and " + chunks + " chunks");
				Console.WriteLine("Sum of integers 1 to " + count + " is " + total);

				// cleanup because this test can produce a lot of data
				await location.RemoveAsync(db, this.Cancellation);
			}
		}

		[Test]
		public async Task Test_Can_Batch_ForEach_AsyncWithContext()
		{
			const int N = 50 * 1000;

			using (var db = await OpenTestPartitionAsync())
			{

				Console.WriteLine("Bulk inserting " + N + " items...");
				var location = await GetCleanDirectory(db, "Bulk", "ForEach");

				Console.WriteLine("Preparing...");

				await Fdb.Bulk.WriteAsync(
					db,
					Enumerable.Range(1, N).Select((x) => new KeyValuePair<Slice, Slice>(location.Pack(x), Slice.FromInt32(x))),
					null,
					this.Cancellation
				);

				Console.WriteLine("Reading...");

				long total = 0;
				long count = 0;
				int chunks = 0;
				var sw = Stopwatch.StartNew();
				await Fdb.Bulk.ForEachAsync(
					db,
					Enumerable.Range(1, N).Select(x => location.Pack(x)),
					async (xs, ctx) =>
					{
						Interlocked.Increment(ref chunks);
						Console.WriteLine("> Called with batch of " + xs.Length.ToString("N0") + " at offset " + ctx.Position.ToString("N0") + " of gen " + ctx.Generation + " with step " + ctx.Step + " and cooldown " + ctx.Cooldown + " (genElapsed=" + ctx.ElapsedGeneration + ", totalElapsed=" + ctx.ElapsedTotal + ")");

						var throttle = Task.Delay(TimeSpan.FromMilliseconds(10 + (xs.Length / 25) * 5)); // magic numbers to try to last longer than 5 sec
						var results = await ctx.Transaction.GetValuesAsync(xs);
						await throttle;

						long sum = 0;
						for (int i = 0; i < results.Length; i++)
						{
							sum += results[i].ToInt32();
						}
						Interlocked.Add(ref total, sum);
						Interlocked.Add(ref count, results.Length);
					},
					this.Cancellation
				);
				sw.Stop();

				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " seconds and " + chunks + " chunks");
				Console.WriteLine("Sum of integers 1 to " + count + " is " + total);

				// cleanup because this test can produce a lot of data
				await location.RemoveAsync(db, this.Cancellation);
			}
		}

		[Test]
		public async Task Test_Can_Batch_Aggregate()
		{
			const int N = 50 * 1000;

			using (var db = await OpenTestPartitionAsync())
			{

				Console.WriteLine("Bulk inserting " + N + " items...");
				var location = await GetCleanDirectory(db, "Bulk", "Aggregate");

				Console.WriteLine("Preparing...");

				var rnd = new Random(2403);
				var source = Enumerable.Range(1, N).Select((x) => new KeyValuePair<int, int>(x, rnd.Next(1000))).ToList();

				await Fdb.Bulk.WriteAsync(
					db,
					source.Select((x) => new KeyValuePair<Slice, Slice>(location.Pack(x.Key), Slice.FromInt32(x.Value))),
					null,
					this.Cancellation
				);

				Console.WriteLine("Reading...");

				int chunks = 0;
				var sw = Stopwatch.StartNew();
				long total = await Fdb.Bulk.AggregateAsync(
					db,
					source.Select(x => location.Pack(x.Key)),
					() => 0L,
					async (xs, ctx, sum) =>
					{
						Interlocked.Increment(ref chunks);
						Console.WriteLine("> Called with batch of " + xs.Length.ToString("N0") + " at offset " + ctx.Position.ToString("N0") + " of gen " + ctx.Generation + " with step " + ctx.Step + " and cooldown " + ctx.Cooldown + " (genElapsed=" + ctx.ElapsedGeneration + ", totalElapsed=" + ctx.ElapsedTotal + ")");

						var throttle = Task.Delay(TimeSpan.FromMilliseconds(10 + (xs.Length / 25) * 5)); // magic numbers to try to last longer than 5 sec
						var results = await ctx.Transaction.GetValuesAsync(xs);
						await throttle;

						for (int i = 0; i < results.Length; i++)
						{
							sum += results[i].ToInt32();
						}
						return sum;
					},
					this.Cancellation
				);
				sw.Stop();

				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " seconds and " + chunks + " chunks");

				long actual = source.Sum(x => (long)x.Value);
				Console.WriteLine("> Computed sum of the " + N.ToString("N0") + " random values is " + total.ToString("N0"));
				Console.WriteLine("> Actual sum of the " + N.ToString("N0") + " random values is " + actual.ToString("N0"));
				Assert.That(total, Is.EqualTo(actual));

				// cleanup because this test can produce a lot of data
				await location.RemoveAsync(db, this.Cancellation);
			}
		}

		[Test]
		public async Task Test_Can_Batch_Aggregate_With_Transformed_Result()
		{
			const int N = 50 * 1000;

			using (var db = await OpenTestPartitionAsync())
			{

				Console.WriteLine("Bulk inserting " + N + " items...");
				var location = await GetCleanDirectory(db, "Bulk", "Aggregate");

				Console.WriteLine("Preparing...");

				var rnd = new Random(2403);
				var source = Enumerable.Range(1, N).Select((x) => new KeyValuePair<int, int>(x, rnd.Next(1000))).ToList();

				await Fdb.Bulk.WriteAsync(
					db,
					source.Select((x) => new KeyValuePair<Slice, Slice>(location.Pack(x.Key), Slice.FromInt32(x.Value))),
					null,
					this.Cancellation
				);

				Console.WriteLine("Reading...");

				int chunks = 0;
				var sw = Stopwatch.StartNew();
				double average = await Fdb.Bulk.AggregateAsync(
					db,
					source.Select(x => location.Pack(x.Key)),
					() => FdbTuple.Create(0L, 0L),
					async (xs, ctx, state) =>
					{
						Interlocked.Increment(ref chunks);
						Console.WriteLine("> Called with batch of " + xs.Length.ToString("N0") + " at offset " + ctx.Position.ToString("N0") + " of gen " + ctx.Generation + " with step " + ctx.Step + " and cooldown " + ctx.Cooldown + " (genElapsed=" + ctx.ElapsedGeneration + ", totalElapsed=" + ctx.ElapsedTotal + ")");

						var throttle = Task.Delay(TimeSpan.FromMilliseconds(10 + (xs.Length / 25) * 5)); // magic numbers to try to last longer than 5 sec
						var results = await ctx.Transaction.GetValuesAsync(xs);
						await throttle;

						long sum = 0L;
						for (int i = 0; i < results.Length; i++)
						{
							sum += results[i].ToInt32();
						}
						return FdbTuple.Create(state.Item1 + sum, state.Item2 + results.Length);
					},
					(state) => (double)state.Item1 / state.Item2,
					this.Cancellation
				);
				sw.Stop();

				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " seconds and " + chunks + " chunks");

				double actual = (double)source.Sum(x => (long)x.Value) / source.Count;
				Console.WriteLine("> Computed average of the " + N.ToString("N0") + " random values is " + average.ToString("N3"));
				Console.WriteLine("> Actual average of the " + N.ToString("N0") + " random values is " + actual.ToString("N3"));
				Assert.That(average, Is.EqualTo(actual).Within(double.Epsilon));

				// cleanup because this test can produce a lot of data
				await location.RemoveAsync(db, this.Cancellation);
			}
		}

		[Test]
		public async Task Test_Can_Export_To_Disk()
		{
			const int N = 50 * 1000;

			using (var zedb = await OpenTestPartitionAsync())
			{
				var db = zedb.Logged((tr) =>  Console.WriteLine(tr.Log.GetTimingsReport(true)));
	
				Console.WriteLine("Bulk inserting " + N + " items...");
				var location = await GetCleanDirectory(db, "Bulk", "Export");

				Console.WriteLine("Preparing...");

				var rnd = new Random(2403);
				var source = Enumerable
					.Range(1, N)
					.Select((x) => new KeyValuePair<Guid, Slice>(Guid.NewGuid(), Slice.Random(rnd, rnd.Next(8, 256))))
					.ToList();

				Console.WriteLine("Inserting...");

				await Fdb.Bulk.WriteAsync(
					db.WithoutLogging(),
					source.Select((x) => new KeyValuePair<Slice, Slice>(location.Pack(x.Key), x.Value)),
					null,
					this.Cancellation
				);

				string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "export.txt");
				Console.WriteLine("Exporting to disk... " + path);
				int chunks = 0;
				var sw = Stopwatch.StartNew();
				using (var file = File.CreateText(path))
				{
					double average = await Fdb.Bulk.ExportAsync(
						db,
						location.ToRange(),
						async (xs, pos, ct) =>
						{
							Assert.That(xs, Is.Not.Null);

							Interlocked.Increment(ref chunks);
							Console.WriteLine("> Called with batch [{0:N0}..{1:N0}] ({2:N0} items, {3:N0} bytes)", pos, pos + xs.Length - 1, xs.Length, xs.Sum(kv => kv.Key.Count + kv.Value.Count));

							//TO CHECK:
							// => keys are ordered in the batch
							// => no duplicates

							var sb = new StringBuilder(4096);
							foreach(var x in xs)
							{
								sb.AppendFormat("{0} = {1}\r\n", location.UnpackSingle<Guid>(x.Key), x.Value.ToBase64());
							}
							await file.WriteAsync(sb.ToString());
						},
						this.Cancellation
					);
				}
				sw.Stop();
				Console.WriteLine("Done in " + sw.Elapsed.TotalSeconds.ToString("N3") + " seconds and " + chunks + " chunks");
				Console.WriteLine("File size is " + new FileInfo(path).Length.ToString("N0"));

				// cleanup because this test can produce a lot of data
				await location.RemoveAsync(zedb, this.Cancellation);

				File.Delete(path);
			}
		}

	}
}

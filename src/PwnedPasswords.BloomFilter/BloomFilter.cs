// Source code adapted from https://archive.codeplex.com/?p=bloomfilter
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HashFunction = System.Func<string, int>;

namespace PwnedPasswords.BloomFilter
{
    /// <summary>
    /// A bloom filter used to hash strings
    /// </summary>
    internal partial class BloomFilter
    {
        private const int MaxShards = 256;

        /// <summary>
        /// Creates a new Bloom filter, specifying an error rate of 1/capacity, using the optimal size for the underlying data structure based on the desired capacity and error rate, as well as the optimal number of hash functions.
        /// A secondary hash function will be provided for you if your type T is either string or int. Otherwise an exception will be thrown. If you are not using these types please use the overload that supports custom hash functions.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        public BloomFilter(long capacity) : this(capacity, BestErrorRate(capacity)) { }

        /// <summary>
        /// Creates a new Bloom filter, using the optimal size for the underlying data structure based on the desired capacity and error rate, as well as the optimal number of hash functions.
        /// A secondary hash function will be provided for you if your type T is either string or int. Otherwise an exception will be thrown. If you are not using these types please use the overload that supports custom hash functions.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        /// <param name="errorRate">The acceptable false-positive rate (e.g., 0.01F = 1%)</param>
        public BloomFilter(long capacity, float errorRate) : this(capacity, errorRate, BestM(capacity, errorRate), BestK(capacity, errorRate)) { }

        /// <summary>
        /// Creates a new Bloom filter.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        /// <param name="errorRate">The acceptable false-positive rate (e.g., 0.01F = 1%)</param>
        /// <param name="shardsAndM">The number of BitArrays to use, and the number of bits in each BitArray</param>
        /// <param name="k">The number of hash functions to use.</param>
        private BloomFilter(long capacity, float errorRate, (int Shards, int M) shardsAndM, int k)
        {
            // validate the params are in range
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be > 0");
            }

            if (errorRate >= 1 || errorRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(errorRate), errorRate,
                    $"errorRate must be between 0 and 1, exclusive. Was {errorRate}");
            }

            var (shards, m) = shardsAndM;

            if (m < 1) // from overflow in bestM calculation
            {
                throw new ArgumentOutOfRangeException(
                    $"The provided capacity and errorRate values would result in an array of length > int.MaxValue. Please reduce either of these values. Capacity: {capacity}, Error rate: {errorRate}");
            }

            ShardCapacity = (int) (capacity / shards);
            ExpectedErrorRate = errorRate;
            HashFunctionCount = k;
            HashBits = Enumerable.Repeat(m, shards)
                .Select(arraySize => new BitArray(arraySize))
                .ToArray();
        }

        /// <summary>
        /// Recreates a <see cref="BloomFilter"/> instance from the provided bits
        /// </summary>
        /// <param name="arrayBits">The bloom filter bits</param>
        /// <param name="hashFunctionCount">The number of hash functions used to create the <paramref name="arrayBits"/></param>
        /// <param name="shardCapacity">The anticipated number of items that were added to each BitArray.</param>
        /// <param name="errorRate">The false-positive rate specified when creating the filter(e.g., 0.01F = 1%)</param>
        private BloomFilter(BitArray[] arrayBits, int hashFunctionCount, int shardCapacity, float errorRate)
        {
            HashFunctionCount = hashFunctionCount;
            HashBits = arrayBits;
            ShardCapacity = shardCapacity;
            ExpectedErrorRate = errorRate;
        }

        /// <summary>
        /// Adds new items to the filter. They cannot be removed.
        /// </summary>
        public void AddRange(IEnumerable<string> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        private readonly HashFunction _primaryHash = GetStableHashCode;
        private readonly HashFunction _secondaryHash = HashString;

        /// <summary>
        /// Adds a new item to the filter. It cannot be removed.
        /// </summary>
        public void Add(string item)
        {
            // start flipping bits for each hash of item
            var shard = GetShard(item);
            var primaryHash = _primaryHash(item);
            var secondaryHash = _secondaryHash(item);
            for (var i = 0; i < HashFunctionCount; i++)
            {
                var hash = ComputeHash(primaryHash, secondaryHash, i);
                shard[hash] = true;
            }
        }

        /// <summary>
        /// Checks for the existence of the item in the filter for a given probability.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Contains(string item)
        {
            var shard = GetShard(item);
            var primaryHash = _primaryHash(item);
            var secondaryHash = _secondaryHash(item);
            for (var i = 0; i < HashFunctionCount; i++)
            {
                var hash = ComputeHash(primaryHash, secondaryHash, i);
                if (shard[hash] == false)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The ratio of false to true bits in the filter. E.g., 1 true bit in a 10 bit filter means a truthiness of 0.1.
        /// </summary>
        public double Truthiness => (double)TrueBits() / TotalCapacity;

        private int TrueBits()
        {
            var output = 0;
            foreach (var shard in HashBits)
            {
                foreach (bool bit in shard)
                {
                    if (bit == true)
                    {
                        output++;
                    }
                }
            }
            return output;
        }

        /// <summary>
        /// Performs Dillinger and Manolios double hashing. 
        /// </summary>
        private int ComputeHash(int primaryHash, int secondaryHash, int i)
        {
            var resultingHash = (primaryHash + (i * secondaryHash)) % ShardCapacity;
            return Math.Abs((int)resultingHash);
        }

        /// <summary>
        /// The provided capacity used to create the filter
        /// </summary>
        public long TotalCapacity => Shards * (long)ShardCapacity;

        /// <summary>
        /// The number of BitArray shards in the filter
        /// </summary>
        public int Shards => HashBits.Count;

        /// <summary>
        /// The capacity of each <see cref="BitArray"/> shard in <see cref="HashBits"/>
        /// </summary>
        public int ShardCapacity { get; }

        /// <summary>
        /// The expected error rate for the given <see cref="TotalCapacity"/>
        /// </summary>
        public float ExpectedErrorRate { get; }

        /// <summary>
        /// Get the number of hash functions used to create the filter
        /// </summary>
        public int HashFunctionCount { get; }

        /// <summary>
        /// Get the filter as a bit array
        /// </summary>
        public IReadOnlyList<BitArray> HashBits { get; }

        private static int BestK(long capacity, float errorRate)
        {
            var (shards, m) = BestM(capacity, errorRate);
            var shardCapacity = (int)(capacity / shards);
            return (int)Math.Round(Math.Log(2.0) * m / shardCapacity);
        }

        private static (int Shards, int M) BestM(long capacity, float errorRate)
        {
            // we only use a single set of 256 shards at the moment
            var shardIncrement = 256; // 1 byte
            var shards = 1;
            while (shards < MaxShards)
            {
                var bestM = (long)Math.Ceiling(capacity * Math.Log(errorRate, (1.0 / Math.Pow(2, Math.Log(2.0)))));

                if (bestM < int.MaxValue)
                {
                    return (1, (int)bestM);
                }

                capacity = capacity / shardIncrement;
                shards = shards * shardIncrement;
            }

            // Still too big for us!
            return (1, -1);
        }

        private static float BestErrorRate(long capacity)
        {
            var c = (float)(1.0 / capacity);
            if (c != 0)
            {
                return c;
            }
            else
            {
                return (float)Math.Pow(0.6185, ((long)int.MaxValue * MaxShards) / capacity); // http://www.cs.princeton.edu/courses/archive/spring02/cs493/lec7.pdf
            }
        }

        /// <summary>
        /// Hashes a string using Bob Jenkin's "One At A Time" method from Dr. Dobbs (http://burtleburtle.net/bob/hash/doobs.html).
        /// Runtime is suggested to be 9x+9, where x = input.Length. 
        /// </summary>
        /// <param name="input">The string to hash.</param>
        /// <returns>The hashed result.</returns>
        private static int HashString(string input)
        {
            var hash = 0;

            for (var i = 0; i < input.Length; i++)
            {
                hash += input[i];
                hash += (hash << 10);
                hash ^= (hash >> 6);
            }
            hash += (hash << 3);
            hash ^= (hash >> 11);
            hash += (hash << 15);
            return hash;
        }

        /// <summary>
        /// Generates a "stable" hash, i.e. one that doesn't change based on the run
        /// This shouldn't be favored over <see cref="string.GetHashCode()"/> generally,
        /// but we need something that persists across app domains /program runs
        /// see https://github.com/dotnet/corefx/blob/a10890f4ffe0fadf090c922578ba0e606ebdd16c/src/Common/src/System/Text/StringOrCharArray.cs#L140
        /// </summary>
        static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        private BitArray GetShard(string item)
        {
            if (Shards == 1 || string.IsNullOrEmpty(item))
            {
                return HashBits[0];
            }

            //we only support 256 shards atm, so keep it simple:
            var shard = item[0];
            return HashBits[shard];
        }
    }
}
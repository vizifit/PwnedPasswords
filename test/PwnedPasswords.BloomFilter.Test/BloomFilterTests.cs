﻿// Source code adapted from https://archive.codeplex.com/?p=bloomfilter#BloomFilter/Filter.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PwnedPasswords.BloomFilter.Test
{
    public class BloomFilterTests
    {
        /// <summary>
        /// There should be no false negatives.
        /// </summary>
        [Fact]
        public void NoFalseNegativesTest()
        {
            // set filter properties
            var capacity = 10000;
            var errorRate = 0.001F; // 0.1%

            // create input collection
            var inputs = GenerateRandomDataList(capacity);

            // instantiate filter and populate it with the inputs
            var target = new BloomFilter(capacity, errorRate);
            foreach (var input in inputs)
            {
                target.Add(input);
            }

            // check for each input. if any are missing, the test failed
            foreach (var input in inputs)
            {
                Assert.True(target.Contains(input), $"False negative: {input}");
            }
        }

        /// <summary>
        /// Only in extreme cases should there be a false positive with this test.
        /// </summary>
        [Fact]
        public void LowProbabilityFalseTest()
        {
            var capacity = 10000; // we'll actually add only one item
            var errorRate = 0.0001F; // 0.01%

            // instantiate filter and populate it with a single random value
            var target = new BloomFilter(capacity, errorRate);
            target.Add(Guid.NewGuid().ToString("N"));

            // generate a new random value and check for it
            Assert.False(target.Contains(Guid.NewGuid().ToString("N")), "Check for missing item returned true.");
        }

        [Fact]
        public void FalsePositivesInRangeTest()
        {
            // set filter properties
            var capacity = 1000000;
            var errorRate = 0.001F; // 0.1%

            // instantiate filter and populate it with random strings
            var target = new BloomFilter(capacity, errorRate);
            for (var i = 0; i < capacity; i++)
            {
                target.Add(Guid.NewGuid().ToString("N"));
            }

            // generate new random strings and check for them
            // about errorRate of them should return positive
            var falsePositives = 0;
            var testIterations = capacity;
            var expectedFalsePositives = ((int)(testIterations * errorRate)) * 2;
            for (var i = 0; i < testIterations; i++)
            {
                var test = GetHash();
                if (target.Contains(test) == true)
                {
                    falsePositives++;
                }
            }

            Assert.True(falsePositives <= expectedFalsePositives,
                $"Number of false positives ({falsePositives}) greater than expected ({expectedFalsePositives}).");
        }

        private static string GetHash()
        {
            return Guid.NewGuid().ToString("N");
        }

        [Fact]
        public void OverLargeInputTest()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                // set filter properties
                var capacity = long.MaxValue - 1;
                var errorRate = 0.01F; // 1%

                // instantiate filter
                var target = new BloomFilter(capacity, errorRate);
            });
        }

        [Fact]
        public void LargeInputTest()
        {
            // set filter properties
            var capacity = 2000000;
            var errorRate = 0.01F; // 1%

            // instantiate filter and populate it with random strings
            var target = new BloomFilter(capacity, errorRate);
            for (var i = 0; i < capacity; i++)
            {
                target.Add(Guid.NewGuid().ToString("N"));
            }

            // if it didn't error out on that much input, this test succeeded
        }

        [Fact]
        public void LargeInputTestAutoError()
        {
            // set filter properties
            var capacity = 2000000;

            // instantiate filter and populate it with random strings
            var target = new BloomFilter(capacity);
            for (var i = 0; i < capacity; i++)
            {
                target.Add(Guid.NewGuid().ToString("N"));
            }

            // if it didn't error out on that much input, this test succeeded
        }

        /// <summary>
        /// If k and m are properly chosen for n and the error rate, the filter should be about half full.
        /// </summary>
        [Fact]
        public void TruthinessTest()
        {
            var capacity = 10000;
            var errorRate = 0.001F; // 0.1%
            var target = new BloomFilter(capacity, errorRate);
            for (var i = 0; i < capacity; i++)
            {
                target.Add(Guid.NewGuid().ToString("N"));
            }

            Assert.Equal(1, target.Shards.Count);
            var actual = target.TruthinessPerShard.First();
            var expected = 0.5;
            var threshold = 0.01; // filter shouldn't be < 49% or > 51% "true"
            var difference = Math.Abs(actual - expected);
            Assert.True(difference < threshold, $"Information density too high or low. Actual={actual}, Expected={expected}");
        }

        /// <summary>
        /// If k and m are properly chosen for n and the error rate, the filter should be about half full.
        /// </summary>
        [Fact(Skip = "Takes a long time to run")]
        public void TruthinessMultiShardTest()
        {
            var capacity = 100_000_000;
            var errorRate = 0.00001F; // 0.001%
            var target = new BloomFilter(capacity, errorRate);
            for (var i = 0; i < capacity; i++)
            {
                target.Add(Guid.NewGuid().ToString("N"));
            }

            Assert.NotEqual(1, target.Shards.Count);

            var expected = 0.5;
            var threshold = 0.01; // filter shouldn't be < 49% or > 51% "true"
            for (var i = 0; i < target.TruthinessPerShard.Count; i++)
            {
                var actual = target.TruthinessPerShard[i];
                var difference = Math.Abs(actual - expected);
                Assert.True(difference < threshold, $"Information density too high or low in shard {i}. Actual={actual}, Expected={expected}");
            }
        }

        private static List<string> GenerateRandomDataList(int capacity)
        {
            var inputs = new List<string>(capacity);
            for (var i = 0; i < capacity; i++)
            {
                inputs.Add(Guid.NewGuid().ToString("N"));
            }
            return inputs;
        }

        [Fact]
        public void InvalidCapacityConstructorTest()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var errorRate = 0.1F;
                var capacity = 0; // no good
                var target = new BloomFilter(capacity, errorRate);
            });
        }

        [Fact]
        public void InvalidErrorRateConstructorTest()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var errorRate = 10F; // no good
                var capacity = 10;
                var target = new BloomFilter(capacity, errorRate);
            });
        }

        [Fact]
        public void LargeNumberOfValuesDoesNotCauseOverflow()
        {
            const int capacity = 517_238_891;
            const float errorRate = 0.001f;

            const int expectedShards = 16;

            var filter = new BloomFilter(capacity, errorRate);

            Assert.Equal(expectedShards, filter.Shards.Count);
            Assert.Equal(capacity, filter.TotalCapacity);
        }

        [Fact]
        public void ExceedinglyLargeNumberOfValuesCausesOverflow()
        {
            const long capacity = 999_517_238_891;
            const float errorRate = 0.001f;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BloomFilter(capacity, errorRate));
        }
    }
}

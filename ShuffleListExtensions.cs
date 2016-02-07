using System;
using System.Collections.Generic;
using System.Security.Cryptography;

// ReSharper disable once CheckNamespace
namespace NuGet.Modules
{
    public static class ShuffleListExtensions
    {
        public static void Shuffle<T>(this List<T> list)
        {
            list.Shuffle(new Random());
        }

        public static void Shuffle<T>(this List<T> list, Random random)
        {
            list.Sort((x, y) => 2 * random.Next(0, 2) - 1);
        }

        public static void CryptoShuffle<T>(this List<T> list)
        {
            using (var generator = new RNGCryptoServiceProvider())
            {
                list.CryptoShuffle(generator);
            }
        }

        public static void CryptoShuffle<T>(this List<T> list, RandomNumberGenerator generator)
        {
            var bytes = new byte[2];
            list.Sort((x, y) =>
            {
                generator.GetBytes(bytes);
                return bytes[0].CompareTo(bytes[1]);
            });
        }
    }
}
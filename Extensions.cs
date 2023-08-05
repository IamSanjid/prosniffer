using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PROSniffer
{
    public static class Extensions
    {
        public static string[] SplitArgs(this string command, bool keepQuote = false)
        {
            if (string.IsNullOrEmpty(command))
                return Array.Empty<string>();

            var inQuote = false;
            var chars = command.ToCharArray().Select(v =>
            {
                if (v == '"')
                    inQuote = !inQuote;
                return !inQuote && v == ' ' ? '\n' : v;
            }).ToArray();

            return new string(chars).Split('\n')
                .Select(x => keepQuote ? x : x.Trim('"'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        public static IEnumerable<string> SplitIntoChunks(this string input, int chunkSize)
        {
            if (string.IsNullOrEmpty(input) || chunkSize <= 0)
            {
                throw new ArgumentException("Input string cannot be null or empty, and chunkSize must be greater than zero.");
            }

            for (int i = 0; i < input.Length; i += chunkSize)
            {
                int length = Math.Min(chunkSize, input.Length - i);
                yield return input.Substring(i, length);
            }
        }

        public static byte[] Not(this byte[] input)
        {
            byte[] result = new byte[input.Length];

            for (int i = 0; i < input.Length; i++)
            {
                result[i] = (byte)~input[i];
            }

            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Trading.Advisor
{
    static class CsvHelper
    {
        public static IEnumerable<string[]> Load (string filePath, char separator)
        {
            return
                File.ReadLines (filePath)
                    .Skip (1)
                    .Select (x => x.Split (separator));
        }

        public static IEnumerable<string[]> Parse (string content, char separator)
        {
            return
                ReadLines (content)
                    .Skip (1)
                    .Select (x => x.Split (separator));
        }

        public static void Save (string filePath, string[] header, IEnumerable<string[]> rows, string separator)
        {
            var lines =
                new [] { header }
                    .Concat (rows)
                    .Select (x => String.Join (separator, x));
            File.WriteAllLines (filePath, lines);
        }

        static IEnumerable<string> ReadLines (string value)
        {
            using (var sr = new StringReader (value)) {
                string line;
                while ((line = sr.ReadLine ()) != null) {
                    yield return line;
                }
            }
        }
    }
}


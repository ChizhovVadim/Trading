using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Globalization;

namespace Trading.Core
{
	public static class TableHelper
	{
		public static string Format (string header, IEnumerable<object[]> source)
		{
			return Format (CultureInfo.CurrentCulture, header, source);
		}
		
		public static string Format (IFormatProvider formatProvider, string header, IEnumerable<object[]> source)
		{
			string[] title;
			string[] format;
			UnzipHeader (header, out title, out format);
			return FormatTable (title, source.Select (x => FormatRow (x, format, formatProvider)).ToList ());
		}

		static void UnzipHeader (string header, out string[] title, out string[] format)
		{
			var items = header
				.Split (',')
				.Select (x => {
				int i = x.IndexOf (":");
				if (i == -1)
					return Tuple.Create (x, (string)null);
				else
					return Tuple.Create (x.Substring (0, i), x.Substring (i + 1));
			})
				.ToList ();
			title = items.Select (x => x.Item1).ToArray ();
			format = items.Select (x => x.Item2).ToArray ();
		}

		static string[] FormatRow (object[] row, string[] format, IFormatProvider formatProvider)
		{
			return row
				.Zip (format, (c, z) => c == null ? String.Empty : String.IsNullOrEmpty (z) ? c.ToString () : ((IFormattable)c).ToString (z, formatProvider))
				.ToArray ();
		}

		static string FormatTable (string[] header, List<string[]> rows)
		{
			int[] columnWidth = GetMaxColumnsWidth (header, rows);
			bool[] isNumerics = GetIsNumerics (rows, header.Length);

			var sb = new StringBuilder ();
			PrintRow (sb, header, columnWidth, isNumerics);
			PrintRow (sb, header.Select (x => new String ('-', x.Length)).ToArray (), columnWidth, isNumerics);
			foreach (string[] row in rows) {
				PrintRow (sb, row, columnWidth, isNumerics);
			}
			return sb.ToString ();
		}

		static int[] GetMaxColumnsWidth (string[] header, List<string[]> rows)
		{
			var maxColumnsWidth = header.Select (x => x.Length).ToArray ();
			foreach (var row in rows) {
				for (int i = 0; i < row.Length; i++) {
					maxColumnsWidth [i] = Math.Max (maxColumnsWidth [i], row [i].Length);
				}
			}
			return maxColumnsWidth;
		}

		static bool[] GetIsNumerics (List<string[]> rows, int columnCount)
		{
			bool[] result = Enumerable.Range (0, columnCount).Select (_ => true).ToArray ();
			foreach (var row in rows) {
				for (int i = 0; i < row.Length; i++) {
					result [i] = result [i] && IsNumeric (row [i]);
				}
			}
			return result;
		}

		static void PrintRow (StringBuilder sb, string[] columns, int[] columnWidth, bool[] isNumerics)
		{
			for (int i = 0; i < columns.Length; i++) {
				if (isNumerics [i]) {
					sb.Append (columns [i].PadLeft (columnWidth [i] + 1) + " ");
				} else {
					sb.Append (" " + columns [i].PadRight (columnWidth [i] + 1));
				}
			}
			sb.AppendLine ();
		}

		public static bool IsNumeric (string s)
		{
			string symbols = "0123456789 +-%.,\u00a0";
			return s.All (ch => symbols.Contains (ch));
		}
	}
}


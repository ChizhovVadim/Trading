using System;

namespace Trading.Advisor
{
	static class DateHelper
	{
		public static DateTime LastDayOfYear (DateTime d)
		{
			return new DateTime (d.Year, 12, 31);
		}

		public static DateTime LastDayOfMonth (DateTime d)
		{
			return new DateTime (d.Year, d.Month, DateTime.DaysInMonth (d.Year, d.Month));
		}

        public static DateTime FirstDayOfMonth(DateTime d)
        {
            return new DateTime(d.Year, d.Month, 1);
        }

		public static DateTime NextWeekday (DateTime start, DayOfWeek day)
		{
			// The (... + 7) % 7 ensures we end up with a value in the range [0, 6]
			int daysToAdd = ((int)day - (int)start.DayOfWeek + 7) % 7;
			return start.AddDays (daysToAdd);
		}

		public static DateTime LastDayOfWeek (DateTime d)
		{
			return NextWeekday (d.Date, DayOfWeek.Sunday);
		}

		public static DateTime Date (DateTime d)
		{
			return d.Date;
		}

		public static bool IsNewDayStarted (DateTime l, DateTime r)
		{
			return l.Date < r.Date;
		}

		public static bool IsMainFortsSession (DateTime d)
		{
			return d.TimeOfDay < new TimeSpan (19, 00, 00);
		}

		public static bool IsNewFortsDateStarted (DateTime l, DateTime r)
		{
			return IsMainFortsSession (l) && (!IsMainFortsSession (r) || IsNewDayStarted (l, r));
		}

		public static bool IsWeekend (DateTime d)
		{
			return d.DayOfWeek == DayOfWeek.Saturday
			|| d.DayOfWeek == DayOfWeek.Sunday;
		}

        public static bool IsNewDayAfterHolidayStarted(DateTime l, DateTime r)
        {
            var startDate = l.Date.AddDays(1);
            var endDate = r.Date;
            for (DateTime d = startDate; d < endDate; d = d.AddDays(1))
            {
                //В промежутке между currentDate и nextDate был 1 не выходной (значит торговала Америка)
                if (!DateHelper.IsWeekend(d))
                {
                    return true;
                }
            }
            return false;
        }
	}
}

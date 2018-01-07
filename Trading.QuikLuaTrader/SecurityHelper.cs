using System;
using System.Globalization;

namespace Trading.QuikLuaTrader
{
    static class SecurityHelper
    {
        //Sample: "Si-3.17" -> "SiH7"
        //http://moex.com/s205
        public static string EncodeSecurity(string securityName)
        {
            var securityParts = securityName.Split(new char[] { '-', '.' }, 3);
            int month = Int32.Parse(securityParts[1], CultureInfo.InvariantCulture);
            int year = Int32.Parse(securityParts[2], CultureInfo.InvariantCulture);
            const string MonthCodes = "FGHJKMNQUVXZ";
            return String.Format(CultureInfo.InvariantCulture, "{0}{1}{2}",
                securityParts[0], MonthCodes[month - 1], year % 10);
        }
    }
}
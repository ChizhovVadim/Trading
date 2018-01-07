using System;
using System.Linq;
using System.Collections.Generic;
using Trading.Core;

namespace Trading.QuikLuaTrader
{
	class TraderSnapshotMessage
	{
		public class PortfolioSnapshot
		{
			public string Portfolio;
			public double BeginAmount;
			public double VariationMargin;
			public double CurrentAmount;
		}

		public class PositionSnapshot
		{
			public string Portfolio;
			public string Security;
			public int Position;
			public int QuikPosition;
			public double? RequiredPosition;
		}

		public List<PortfolioSnapshot> Portfolios;
		public List<PositionSnapshot> Positions;
		public DateTime UpdateTime;
	}

	class TraderSnapshotHelper
	{
		public static void Write (TraderSnapshotMessage snapshot)
		{
			Console.Write (TableHelper.Format ("Портфель,ЛимОткрПоз:n0,ВарМаржа:n0,ТекЧистПоз:n0,ВарМаржа%:p1,ТекЧистПоз%:p1",
				snapshot.Portfolios.Select (x => new object[] {
					x.Portfolio,
					x.BeginAmount,
					x.VariationMargin,
					x.CurrentAmount,
					x.VariationMargin / x.BeginAmount,
					x.CurrentAmount / x.BeginAmount
				})));

			Console.Write (TableHelper.Format ("Портфель,Инструмент,ПозицияQuik,Позиция,Требуется:f2,Статус",
				snapshot.Positions.Select (x => new object[] {
					x.Portfolio,
					x.Security,
					x.QuikPosition,
					x.Position,
					x.RequiredPosition,
					HasError (x) ? "!" : "+"
				})));
		}

		static bool HasError (TraderSnapshotMessage.PositionSnapshot x)
		{
			return x.QuikPosition != x.Position ||
			x.RequiredPosition != null && (int)(x.RequiredPosition.Value - x.Position) != 0;
		}
	}
}


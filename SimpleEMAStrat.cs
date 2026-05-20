using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SimpleEMAStrat : Strategy
    {
        private EMA ema50;
        private ATR atr;

        private string lockDirection = "";
        private int lastTradeBar = -1;

        // ✅ Session times (Pacific Time)
        private TimeSpan StartTime = new TimeSpan(15, 0, 10);
        private TimeSpan EndTime   = new TimeSpan(12, 59, 0);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SimpleEMAStrat";
                Description = "EMA intrabar strategy with flip logic, chop filter, and session control.";

                Calculate = Calculate.OnEachTick;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 50;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
            }
            else if (State == State.Configure)
            {
                // ✅ Profit + trailing
                SetProfitTarget("LongEMA", CalculationMode.Ticks, 80);
                SetProfitTarget("ShortEMA", CalculationMode.Ticks, 80);

                SetTrailStop("LongEMA", CalculationMode.Ticks, 55, false);
                SetTrailStop("ShortEMA", CalculationMode.Ticks, 55, false);
            }
            else if (State == State.DataLoaded)
            {
                ema50 = EMA(50);
                atr   = ATR(14);

                AddChartIndicator(ema50);
                AddChartIndicator(atr);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // ✅ SESSION FILTER
            TimeSpan now = Time[0].TimeOfDay;

            bool isAllowedToTrade = (StartTime > EndTime)
                ? (now >= StartTime || now <= EndTime)
                : (now >= StartTime && now <= EndTime);

            if (!isAllowedToTrade)
                return;

            double buffer = 2 * TickSize;

            // ✅ SLOPE (4 bars like you wanted)
            double slope = ema50[0] - ema50[4];
            double minSlope = 5 * TickSize;

            // ✅ ATR FILTER
            double minATR = 8 * TickSize;

            // ✅ ENTRY LOGIC
            if (Position.MarketPosition == MarketPosition.Flat
                && CurrentBar != lastTradeBar)
            {
                // ✅ LONG
                if (Close[0] > ema50[0] + buffer
                    && slope > minSlope
                    && atr[0] > minATR
                    && lockDirection != "Long")
                {
                    EnterLong("LongEMA");
                    lastTradeBar = CurrentBar;
                }

                // ✅ SHORT
                else if (Close[0] < ema50[0] - buffer
                         && slope < -minSlope
                         && atr[0] > minATR
                         && lockDirection != "Short")
                {
                    EnterShort("ShortEMA");
                    lastTradeBar = CurrentBar;
                }
            }

            // ✅ EMA EXIT (does NOT affect lock logic)
            if (Position.MarketPosition == MarketPosition.Long &&
                Close[0] < ema50[0])
            {
                ExitLong("ExitLongEMA", "LongEMA");
            }
            else if (Position.MarketPosition == MarketPosition.Short &&
                     Close[0] > ema50[0])
            {
                ExitShort("ExitShortEMA", "ShortEMA");
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            // ✅ LOCK AFTER ANY EXIT (profit or stop)
            if (execution.Order.FromEntrySignal == "LongEMA" &&
                execution.Order.OrderAction == OrderAction.Sell)
            {
                lockDirection = "Long";
            }

            if (execution.Order.FromEntrySignal == "ShortEMA" &&
                execution.Order.OrderAction == OrderAction.BuyToCover)
            {
                lockDirection = "Short";
            }
        }
    }
}

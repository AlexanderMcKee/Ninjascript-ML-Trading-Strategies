#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SimpleEMAStrat : Strategy
    {
        // Indicators
        private EMA ema21;
        private EMA ema34;
        private EMA ema144;
        private ADX adx;

        // State tracking
        private bool trendLongConfirmed = false;  // EMA stack: 21 > 34 > 144
        private bool trendShortConfirmed = false; // EMA stack: 21 < 34 < 144

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Simple EMA Stacking Strategy - Trend following with crossover entries";
                Name = "SimpleEMAStrat";
                
                // Set to Calculate.OnEachTick for instant trading execution
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                // EMA Inputs
                EMA21Length = 21;
                EMA34Length = 34;
                EMA144Length = 144;

                // ADX Inputs
                ADXLength = 14;
                ADXThreshold = 25;
                ADXHoldBars = 1;

                // Risk Management
                ProfitTargetTicks = 50;
                FollowingStopLossTicks = 90;

                AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "EMA21");
                AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "EMA34");
                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Line, "EMA144");
            }
            else if (State == State.DataLoaded)
            {
                // Initialize indicators
                ema21 = EMA(Close, EMA21Length);
                AddChartIndicator(ema21);

                ema34 = EMA(Close, EMA34Length);
                AddChartIndicator(ema34);

                ema144 = EMA(Close, EMA144Length);
                AddChartIndicator(ema144);

                // ADX
                adx = ADX(ADXLength);
                AddChartIndicator(adx);
            }
            else if (State == State.Configure)
            {
                SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
                SetTrailStop(CalculationMode.Ticks, FollowingStopLossTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            // Wait for sufficient data (ensure ADX warmed up too)
            if (CurrentBar < Math.Max(EMA144Length, ADXLength))
                return;

            double close = Close[0];
            double ema21Val = ema21[0];
            double ema34Val = ema34[0];
            double ema144Val = ema144[0];
            
            double prevEMA21 = ema21[1];
            double prevEMA34 = ema34[1];
            double prevEMA144 = ema144[1];

            // ADX gate: require ADX > threshold for ADXHoldBars bars
            bool adxOk = true;
            for (int i = 0; i < ADXHoldBars; i++)
            {
                if (adx[i] <= ADXThreshold)
                {
                    adxOk = false;
                    break;
                }
            }

            // ===== LONG SIGNAL LOGIC =====
            // Detect uptrend: 21 crosses above 34, and 34 crosses above 144
            bool ema21CrossedAbove34 = prevEMA21 <= prevEMA34 && ema21Val > ema34Val;
            bool ema34CrossedAbove144 = prevEMA34 <= prevEMA144 && ema34Val > ema144Val;
            
            if (ema21CrossedAbove34 || ema34CrossedAbove144)
            {
                trendLongConfirmed = true;
                trendShortConfirmed = false;
            }
            // Entry: All EMAs now properly stacked (21 > 34 > 144)
            if (trendLongConfirmed && ema21Val > ema34Val && ema34Val > ema144Val && adxOk)
            {
                // Close short position if one exists, then enter long
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort();
                }
                
                // Enter long if flat or after exiting short
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EnterLong("LongEntry");
                    trendLongConfirmed = false;
                }
            }

            // ===== SHORT SIGNAL LOGIC =====
            // Detect downtrend: 21 crosses below 34, and 34 crosses below 144
            bool ema21CrossedBelow34 = prevEMA21 >= prevEMA34 && ema21Val < ema34Val;
            bool ema34CrossedBelow144 = prevEMA34 >= prevEMA144 && ema34Val < ema144Val;
            
            if (ema21CrossedBelow34 || ema34CrossedBelow144)
            {
                trendShortConfirmed = true;
                trendLongConfirmed = false;
            }
            // Entry: All EMAs now properly stacked (21 < 34 < 144)
            if (trendShortConfirmed && ema21Val < ema34Val && ema34Val < ema144Val && adxOk)
            {
                // Close long position if one exists, then enter short
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong();
                }
                
                // Enter short if flat or after exiting long
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EnterShort("ShortEntry");
                    trendShortConfirmed = false;
                }
            }

            // Plot EMA values for visual confirmation
            Values[0][0] = ema21Val;
            Values[1][0] = ema34Val;
            Values[2][0] = ema144Val;
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "EMA 21 Length", GroupName = "1. EMA Settings")]
        public int EMA21Length { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EMA 34 Length", GroupName = "1. EMA Settings")]
        public int EMA34Length { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EMA 144 Length", GroupName = "1. EMA Settings")]
        public int EMA144Length { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Target (ticks)", GroupName = "2. Risk Management")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Following Stop Loss (ticks)", GroupName = "2. Risk Management")]
        public int FollowingStopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX Length", GroupName = "3. Filters")]
        public int ADXLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX Threshold", GroupName = "3. Filters")]
        public int ADXThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX Hold Bars", GroupName = "3. Filters")]
        public int ADXHoldBars { get; set; }
        #endregion
    }
}

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class StochasticEMAPullbackStrategy : Strategy
    {
        // Indicators
        private EMA ema21;
        private EMA ema34;
        private EMA ema144;
        private Stochastic stochastic;

        // State tracking
        private bool trendLongConfirmed = false;  // Price and EMAs above 144 EMA
        private bool trendShortConfirmed = false; // Price and EMAs below 144 EMA
        private bool waitingForLongEntry = false;   // K% went oversold (<20)
        private bool waitingForShortEntry = false;  // K% went overbought (>80)

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Stochastic EMA Pullback Strategy";
                Name = "StochasticEMAPullbackStrategy";
                
                // Set to Calculate.OnEachTick for instant trading execution
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                // EMA Inputs
                EMA21Length = 21;
                EMA34Length = 34;
                EMA144Length = 144;

                // Stochastic Inputs
                StochasticKPeriod = 7;
                StochasticKSmoothing = 3;
                StochasticDSmoothing = 3;

                // Risk Management
                ProfitTargetTicks = 100;
                StopLossTicks = 50;

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

                // Stochastic with custom K/D smoothing
                stochastic = Stochastic(Close, StochasticKPeriod, StochasticKSmoothing, StochasticDSmoothing);
                AddChartIndicator(stochastic);
            }
            else if (State == State.Configure)
            {
                SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
            }
        }

        protected override void OnBarUpdate()
        {
            // Wait for sufficient data
            if (CurrentBar < Math.Max(EMA144Length, StochasticKPeriod + StochasticKSmoothing + StochasticDSmoothing))
                return;

            double close = Close[0];
            double ema21Val = ema21[0];
            double ema34Val = ema34[0];
            double ema144Val = ema144[0];
            double stochK = stochastic.StochK[0];
            double stochD = stochastic.StochD[0];
            double prevStochK = stochastic.StochK[1];
            double prevStochD = stochastic.StochD[1];

            // Exit any existing position before checking new signals
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // ===== LONG SIGNAL LOGIC =====
            // Step 1: Determine if uptrend is established (144 EMA as trend filter)
            if (close > ema144Val && ema21Val > ema144Val && ema34Val > ema144Val)
            {
                trendLongConfirmed = true;
                trendShortConfirmed = false;
            }
            // Step 2: Detect when K% drops below 20 (oversold pullback)
            else if (trendLongConfirmed && stochK < 20)
            {
                waitingForLongEntry = true;
                waitingForShortEntry = false;
            }
            // Step 3: Enter long when K% crosses back above D% while still below 20
            else if (waitingForLongEntry && stochK < 20 && prevStochK < stochD && stochK > stochD)
            {
                EnterLong("LongEntry");
                trendLongConfirmed = false;
                waitingForLongEntry = false;
            }

            // ===== SHORT SIGNAL LOGIC =====
            // Step 1: Determine if downtrend is established (144 EMA as trend filter)
            if (close < ema144Val && ema21Val < ema144Val && ema34Val < ema144Val)
            {
                trendShortConfirmed = true;
                trendLongConfirmed = false;
            }
            // Step 2: Detect when K% rises above 80 (overbought pullback)
            else if (trendShortConfirmed && stochK > 80)
            {
                waitingForShortEntry = true;
                waitingForLongEntry = false;
            }
            // Step 3: Enter short when K% crosses back below D% while still above 80
            else if (waitingForShortEntry && stochK > 80 && prevStochK > stochD && stochK < stochD)
            {
                EnterShort("ShortEntry");
                trendShortConfirmed = false;
                waitingForShortEntry = false;
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
        [Display(Name = "Stochastic K Period", GroupName = "2. Stochastic Settings")]
        public int StochasticKPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stochastic K Smoothing", GroupName = "2. Stochastic Settings")]
        public int StochasticKSmoothing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stochastic D Smoothing", GroupName = "2. Stochastic Settings")]
        public int StochasticDSmoothing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Target (ticks)", GroupName = "3. Risk Management")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss (ticks)", GroupName = "3. Risk Management")]
        public int StopLossTicks { get; set; }
        #endregion
    }
}

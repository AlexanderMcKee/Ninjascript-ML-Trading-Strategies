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
    public class MLAdaptiveSuperTrendPureSignals : Strategy
    {
        private ATR atr;
        private Series<double> stSeries;
        private Series<double> lockedLower;
        private Series<double> lockedUpper;
        private int direction = 1; // 1 = Bearish, -1 = Bullish

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "ML Adaptive SuperTrend - Pure Signals Translation";
                Name = "MLAdaptiveSuperTrendPureSignals";
                
                // Change to Calculate.OnPriceChange for 'Instant' entries
                Calculate = Calculate.OnBarClose; 
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                // --- Original Indicator Inputs ---
                AtrLen = 10;
                Factor = 3.0;
                TrainingPeriod = 100;
                HighVolPct = 0.75;
                MidVolPct = 0.5;
                LowVolPct = 0.25;

                AddPlot(new Stroke(Brushes.SeaGreen, 2), PlotStyle.Line, "SuperTrendPlot");
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(AtrLen);
                stSeries = new Series<double>(this);
                lockedLower = new Series<double>(this);
                lockedUpper = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            // Wait for sufficient data
            if (CurrentBar < TrainingPeriod) return;

            // --- 1. ML Logic: Iterative K-Means ---
            double volatility = atr[0];
            double upper = MAX(atr, TrainingPeriod)[0];
            double lower = MIN(atr, TrainingPeriod)[0];

            double amean = lower + (upper - lower) * HighVolPct;
            double bmean = lower + (upper - lower) * MidVolPct;
            double cmean = lower + (upper - lower) * LowVolPct;

            bool stable = false;
            int iterations = 0;

            // Converge centroids based on historical ATR distribution
            while (!stable && iterations < 10)
            {
                double oldA = amean, oldB = bmean, oldC = cmean;
                List<double> hv = new List<double>();
                List<double> mv = new List<double>();
                List<double> lv = new List<double>();

                for (int i = 0; i < TrainingPeriod; i++)
                {
                    double v = atr[i];
                    double d1 = Math.Abs(v - amean);
                    double d2 = Math.Abs(v - bmean);
                    double d3 = Math.Abs(v - cmean);

                    if (d1 < d2 && d1 < d3) hv.Add(v);
                    else if (d2 < d1 && d2 < d3) mv.Add(v);
                    else lv.Add(v);
                }

                if (hv.Count > 0) amean = hv.Average();
                if (mv.Count > 0) bmean = mv.Average();
                if (lv.Count > 0) cmean = lv.Average();

                if (Math.Abs(amean - oldA) < 0.0001 && Math.Abs(bmean - oldB) < 0.0001 && Math.Abs(cmean - oldC) < 0.0001)
                    stable = true;
                
                iterations++;
            }

            // Assign the centroid based on current volatility distance
            double dHv = Math.Abs(volatility - amean);
            double dMv = Math.Abs(volatility - bmean);
            double dLv = Math.Abs(volatility - cmean);
            double assignedCentroid = (dMv < dHv && dMv < dLv) ? bmean : (dLv < dHv && dLv < dMv ? cmean : amean);

            // --- 2. Band Locking Logic (Stepwise Visual Fix) ---
            double src = Median[0]; // Matches hl2 from Pine Script
            double rawUpper = src + Factor * assignedCentroid;
            double rawLower = src - Factor * assignedCentroid;

            if (CurrentBar == 0)
            {
                lockedLower[0] = rawLower;
                lockedUpper[0] = rawUpper;
            }
            else
            {
                // Force bands to only move toward price or stay flat
                lockedLower[0] = (rawLower > lockedLower[1] || Close[1] < lockedLower[1]) ? rawLower : lockedLower[1];
                lockedUpper[0] = (rawUpper < lockedUpper[1] || Close[1] > lockedUpper[1]) ? rawUpper : lockedUpper[1];
            }

            int prevDir = direction;
            if (double.IsNaN(atr[1])) 
            {
                direction = 1;
            }
            else if (stSeries[1] == lockedUpper[1])
            {
                direction = Close[0] > lockedUpper[0] ? -1 : 1;
            }
            else
            {
                direction = Close[0] < lockedLower[0] ? 1 : -1;
            }

            stSeries[0] = (direction == -1) ? lockedLower[0] : lockedUpper[0];

            // --- 3. Execution Signals (Pure Signals Translation) ---
            // Bullish flip (Direction moves from 1 to -1)
            if (prevDir == 1 && direction == -1)
            {
                EnterLong("Long");
            }

            // Bearish flip (Direction moves from -1 to 1)
            if (prevDir == -1 && direction == 1)
            {
                EnterShort("Short");
            }

            // --- 4. Plotting ---
            PlotBrushes[0][0] = (direction == -1) ? Brushes.SeaGreen : Brushes.Red;
            Values[0][0] = stSeries[0];
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name="ATR Length", GroupName="1. SuperTrend")]
        public int AtrLen { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Factor", GroupName="1. SuperTrend")]
        public double Factor { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Training Period", GroupName="2. ML Clustering")]
        public int TrainingPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name="High Vol %", GroupName="2. ML Clustering")]
        public double HighVolPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Mid Vol %", GroupName="2. ML Clustering")]
        public double MidVolPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Low Vol %", GroupName="2. ML Clustering")]
        public double LowVolPct { get; set; }
        #endregion
    }
}
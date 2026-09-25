#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// ML Adaptive SuperTrend, now delegating the buy/sell/hold/flat decision
    /// to jev (TypeSafe's decision API, https://docs.typesafe.ai) instead of
    /// the SuperTrend flip signal. Each realtime bar, once past the session
    /// re-entry cooldown, the strategy sends jev the current market state
    /// (recent OHLCV, indicators, position) and asks a single "choice"
    /// question with four options: buy, sell, hold, none.
    ///
    /// Notes:
    /// - Jev is only called when State == State.Realtime. Backtests/replays/
    ///   optimizations will compute indicators and plot lines as before, but
    ///   will NOT place trades, since a synchronous HTTP call per historical
    ///   bar would be both extremely slow and would burn API usage for no
    ///   benefit. Validate this strategy in Sim/live, not the Strategy Analyzer.
    /// - Requires a TYPESAFE_API_KEY environment variable on the machine
    ///   running NinjaTrader (set it, then restart NinjaTrader so the process
    ///   picks it up). The key is intentionally never hardcoded here so it
    ///   doesn't end up committed to the repo.
    /// - The native SetProfitTarget/SetStopLoss bracket orders configured
    ///   below still attach to every entry regardless of what jev says, as a
    ///   safety net independent of the AI call. Entries keep the exact
    ///   "Long"/"Short" signal names so those brackets keep matching.
    /// - If jev is unreachable, misconfigured, or times out, the strategy
    ///   takes no action that bar (equivalent to "hold") rather than falling
    ///   back to the old SuperTrend flip signal, so a flaky API call can
    ///   never be silently mistaken for a real decision.
    /// </summary>
    public class MLAdaptiveSuperTrendPureSignals : Strategy
    {
        private const string JevEndpoint = "https://api.typesafe.ai/v1/systemone";
        private const string JevModel = "jev-latest";
        private const int JevBarLookback = 20;

        private static readonly HttpClient JevHttp = new HttpClient();

        private ATR atr;
        private Series<double> stSeries;
        private Series<double> lockedLower;
        private Series<double> lockedUpper;
        private int direction = 1; // 1 = Bearish, -1 = Bullish
        private DateTime sessionReentryTime;
        private bool sessionReentryAllowed = true;
        private bool jevConfigured;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "ML Adaptive SuperTrend - jev-driven decisions";
                Name = "MLAdaptiveSuperTrendPureSignals";

                // Change to Calculate.OnPriceChange for 'Instant' entries
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                ExitOnSessionCloseSeconds = 180;

                // --- Original Indicator Inputs ---
                AtrLen = 17;
                Factor = 3.75;
                TrainingPeriod = 100;
                HighVolPct = 0.75;
                MidVolPct = 0.5;
                LowVolPct = 0.25;
                ProfitTargetTicks = 300;
                StopLossTicks = 125;
                ReentryWaitMinutes = 1;
                JevTimeoutSeconds = 3;

                AddPlot(new Stroke(Brushes.SeaGreen, 2), PlotStyle.Line, "SuperTrendPlot");
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(AtrLen);
                stSeries = new Series<double>(this);
                lockedLower = new Series<double>(this);
                lockedUpper = new Series<double>(this);

                string apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
                jevConfigured = !string.IsNullOrWhiteSpace(apiKey);

                if (jevConfigured)
                {
                    JevHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }
                else
                {
                    Print("[Jev] TYPESAFE_API_KEY environment variable not set - strategy will take no action until it is configured.");
                }
            }
            else if (State == State.Configure)
            {
                SetProfitTarget("Long", CalculationMode.Ticks, ProfitTargetTicks);
                SetProfitTarget("Short", CalculationMode.Ticks, ProfitTargetTicks);
                SetStopLoss("Long", CalculationMode.Ticks, StopLossTicks, false);
                SetStopLoss("Short", CalculationMode.Ticks, StopLossTicks, false);
            }
        }

        protected override void OnBarUpdate()
        {
            // Wait for sufficient data
            if (CurrentBar < TrainingPeriod) return;

            if (Bars.IsFirstBarOfSession)
            {
                sessionReentryTime = Time[0].AddMinutes(ReentryWaitMinutes);
                sessionReentryAllowed = false;
            }

            bool sessionReentrySignal = !sessionReentryAllowed && Time[0] >= sessionReentryTime;
            if (sessionReentrySignal)
                sessionReentryAllowed = true;

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

            // --- 3. Execution: ask jev what to do ---
            string jevAction = null;
            if (sessionReentryAllowed && State == State.Realtime)
            {
                JObject jevState = BuildJevState(assignedCentroid, sessionReentryAllowed, sessionReentrySignal);
                jevAction = CallJev(jevState);
            }

            switch (jevAction)
            {
                case "buy":
                    if (Position.MarketPosition != MarketPosition.Long)
                        EnterLong("Long"); // keep the "Long" signal name so the brackets above still attach
                    break;
                case "sell":
                    if (Position.MarketPosition != MarketPosition.Short)
                        EnterShort("Short"); // keep the "Short" signal name so the brackets above still attach
                    break;
                case "none":
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("Jev Flat", "Long");
                    if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort("Jev Flat", "Short");
                    break;
                case "hold":
                default:
                    // No action - either jev said hold, or jev was unavailable/
                    // timed out/misconfigured this bar. Existing position (if
                    // any) is left as-is; the profit target/stop loss brackets
                    // configured in State.Configure still protect it.
                    break;
            }

            // --- 4. Plotting ---
            PlotBrushes[0][0] = (direction == -1) ? Brushes.SeaGreen : Brushes.Red;
            Values[0][0] = stSeries[0];
        }

        /// <summary>
        /// Builds the "state" object sent to jev: recent OHLCV bars plus the
        /// indicators and position info this strategy already has on hand.
        /// </summary>
        private JObject BuildJevState(double assignedCentroid, bool sessionReentryAllowed, bool sessionReentrySignal)
        {
            var bars = new JArray();
            int lookback = Math.Min(JevBarLookback, CurrentBar + 1);
            for (int i = lookback - 1; i >= 0; i--)
            {
                bars.Add(new JObject
                {
                    ["time"] = Time[i].ToString("o"),
                    ["open"] = Open[i],
                    ["high"] = High[i],
                    ["low"] = Low[i],
                    ["close"] = Close[i],
                    ["volume"] = Volume[i]
                });
            }

            bool inPosition = Position.MarketPosition != MarketPosition.Flat;

            var position = new JObject
            {
                ["side"] = Position.MarketPosition.ToString(),
                ["quantity"] = Position.Quantity,
                ["averagePrice"] = inPosition ? Position.AveragePrice : 0,
                ["unrealizedPnl"] = inPosition ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]) : 0
            };

            return new JObject
            {
                ["instrument"] = Instrument.FullName,
                ["time"] = Time[0].ToString("o"),
                ["sessionReentryAllowed"] = sessionReentryAllowed,
                ["sessionReentrySignal"] = sessionReentrySignal,
                ["bars"] = bars,
                ["indicators"] = new JObject
                {
                    ["atr"] = atr[0],
                    ["superTrendValue"] = stSeries[0],
                    ["superTrendDirection"] = direction == -1 ? "bullish" : "bearish",
                    ["volatilityCentroid"] = assignedCentroid,
                    ["lockedUpperBand"] = lockedUpper[0],
                    ["lockedLowerBand"] = lockedLower[0]
                },
                ["position"] = position
            };
        }

        /// <summary>
        /// Synchronous call to jev with a short timeout. Returns "buy", "sell",
        /// "hold", or "none" on success; null on any failure (missing key,
        /// timeout, network error, bad response), which callers treat as "take
        /// no action this bar."
        /// </summary>
        private string CallJev(JObject state)
        {
            if (!jevConfigured) return null;

            var questions = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "choice",
                    ["instructions"] = "Given the current market state for this futures trading strategy, what should it do right now?",
                    ["criteria"] = new JObject
                    {
                        ["buy"] = "Open or flip to a long position.",
                        ["sell"] = "Open or flip to a short position.",
                        ["hold"] = "Keep the current position exactly as-is; do not open, close, or flip anything.",
                        ["none"] = "Stay flat, or close any open position and stay flat; do not enter a new trade."
                    }
                }
            };

            var payload = new JObject
            {
                ["model"] = JevModel,
                ["state"] = state,
                ["questions"] = questions
            };

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(JevTimeoutSeconds)))
                using (var content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response = JevHttp.PostAsync(JevEndpoint, content, cts.Token).GetAwaiter().GetResult();
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        Print(string.Format("[Jev] HTTP {0}: {1}", (int)response.StatusCode, body));
                        return null;
                    }

                    JObject parsed = JObject.Parse(body);
                    JToken actionToken = parsed["answers"] != null ? parsed["answers"]["action"] : null;
                    string choice = actionToken != null && actionToken["choice"] != null ? actionToken["choice"].Value<string>() : null;
                    double confidence = actionToken != null && actionToken["confidence"] != null ? actionToken["confidence"].Value<double>() : 0;

                    Print(string.Format("[Jev] action={0} confidence={1:0.00}", choice ?? "(none)", confidence));
                    return choice;
                }
            }
            catch (OperationCanceledException)
            {
                Print(string.Format("[Jev] call timed out after {0}s - taking no action this bar.", JevTimeoutSeconds));
                return null;
            }
            catch (Exception ex)
            {
                // Deliberately broad: an unhandled exception here must never
                // take down a live strategy over a flaky network call.
                Print("[Jev] call failed: " + ex.Message);
                return null;
            }
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

        [NinjaScriptProperty]
        [Display(Name="Profit Target (ticks)", GroupName="3. Risk Management")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Stop Loss (ticks)", GroupName="3. Risk Management")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Re-entry Wait (minutes)", GroupName="4. Session Safety")]
        public int ReentryWaitMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Jev Timeout (sec)", Description="Max seconds to wait for a jev decision before skipping this bar's action", GroupName="5. Jev AI")]
        public int JevTimeoutSeconds { get; set; }

        #endregion
    }
}
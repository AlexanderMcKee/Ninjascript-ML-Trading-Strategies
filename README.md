Backup of some c# ninjascript for trading on the 1 minute MES. 

# ML Adaptive SuperTrend:

A sophisticated trend-following strategy for NinjaTrader 8 and TradingView, optimized for Micro E-mini S&P 500 (MES) futures. This system combines classic SuperTrend logic with a K-Means machine learning volatility clustering algorithm to filter out sideways price action.

## Strategy Overview

The **ML Adaptive SuperTrend** is designed to capture sustained intraday trends while remaining flat during "chop." It uses a three-tier volatility clustering model to identify the current market regime.

### Key Features
- **ML Volatility Clustering:** Uses K-Means logic to group recent volatility into High, Mid, and Low clusters.
- **Chop Filter:** Automatically prevents new entries when the market is in the "Lowest Volatility" cluster.
- **Profit Guard:** A dynamic trailing stop-loss that activates only after a specific profit threshold is reached, protecting gains while allowing runners room to breathe.
- **Adaptive ATR:** The SuperTrend factor adapts based on the assigned volatility centroid from the ML model.

## Configuration & Settings

The strategy is pre-tuned for the **1-Minute MES** chart, specifically for prop firm evaluation rules (like Apex Trader Funding).

| Parameter | Default Value | Description |
| :--- | :--- | :--- |
| **ATR Length** | 10 | The lookback period for Average True Range. |
| **SuperTrend Factor** | 2.5 | Multiplier for the ATR to set the trend bands. |
| **Training Data Length** | 100 | The lookback window for the K-Means clustering model. |
| **Profit Threshold ($)** | $45.00 | The unrealized PnL at which the Trailing Guard activates. |
| **Trail Multiplier** | 1.4 | Multiplier for the trailing stop (Distance = ATR * 1.4). |
| **Use Chop Filter** | Enabled | If true, prevents entries during the lowest volatility cluster. |

## Installation (NinjaTrader 8)

1. **Create Strategy:** Open NinjaTrader 8 > New > NinjaScript Editor. Right-click 'Strategies' > New Strategy.
2. **Name:** Name it `ML_Adaptive_ChopFilter_Apex`.
3. **Paste Code:** Delete all template code and paste the provided C# script.
4. **Compile:** Press `F5`. Ensure you hear the confirmation sound.
5. **Apply to Chart:** Open a 1-Minute MES chart. Right-click > Strategies > Add `ML_Adaptive_ChopFilter_Apex`.
6. **Configure Account:** Select your Apex/Sim account and set 'Calculate' to `OnBarClose`.

## Performance Characteristics

Based on historical backtesting (Feb 2026 - Apr 2026):
- **Win Rate:** ~76%
- **Profit Factor:** 2.60+
- **Max Drawdown:** Minimal ($360-$400 range), making it ideal for $25k evaluation accounts with $1,500 trailing thresholds.

## Important Trading Rules (Apex/Prop Firms)

- **The 1:00 PM PT Rule:** You MUST be flat before the daily market close (1:00 PM PT / 4:00 PM ET). Set an alarm for 12:55 PM PT.
- **News Events:** Disable the strategy 5 minutes before "Red Folder" news (CPI, FOMC, NFP) and re-enable 10 minutes after.
- **Execution:** The strategy executes **On Bar Close**. It will only trigger at the start of a new 1-minute candle.
- **Maintenance Window:** Do not run the strategy between 1:00 PM and 3:00 PM PT.

## License
This project is for educational and personal use only. Trading futures involves significant risk.

<img width="630" height="885" alt="image" src="https://github.com/user-attachments/assets/a60837ae-5d42-49b7-b187-a59a4a917a0e" />

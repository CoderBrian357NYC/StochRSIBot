using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

class Program
{
    static readonly HttpClient http = new HttpClient();

    const string symbol = "BTCUSDT";
    const string interval = "1h";
    const int atrPeriod = 10;

    // StochRSI parameters
    const int rsiPeriod = 14;
    const int stochPeriod = 14;
    const int kSmooth = 3;
    const int dSmooth = 3;

    static async Task Main()
    {
        try
        {
            var candles = await GetHistoricalCandles(symbol, interval, totalCandles: 5000);

            Console.WriteLine($"Fetched {candles.Count} candles for {symbol}.");

            var atrValues = CalculateExponentialAtr(candles, atrPeriod);

            var closes = candles.Select(c => c.Close).ToList();

            var stochRsiValues = CalculateStochRsi(closes, rsiPeriod, stochPeriod, kSmooth, dSmooth);

            // Add padding here:
            while (atrValues.Count < candles.Count)
                atrValues.Insert(0, 0m);

            while (stochRsiValues.Count < candles.Count)
                stochRsiValues.Insert(0, 0m);

            Console.WriteLine("Starting backtest...");
            Backtest(candles, stochRsiValues, atrValues);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    record Candle(DateTime OpenTime, decimal Open, decimal High, decimal Low, decimal Close);

    static async Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 1000, long? startTime = null, long? endTime = null)
    {
        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        if (startTime.HasValue) url += $"&startTime={startTime.Value}";
        if (endTime.HasValue) url += $"&endTime={endTime.Value}";

        var response = await http.GetStringAsync(url);
        var jsonDoc = JsonDocument.Parse(response);
        var candles = new List<Candle>();

        foreach (var element in jsonDoc.RootElement.EnumerateArray())
        {
            long openTimeMs = element[0].GetInt64();
            decimal open = decimal.Parse(element[1].GetString());
            decimal high = decimal.Parse(element[2].GetString());
            decimal low = decimal.Parse(element[3].GetString());
            decimal close = decimal.Parse(element[4].GetString());

            candles.Add(new Candle(
                DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime,
                open, high, low, close));
        }

        return candles;
    }

    static async Task<List<Candle>> GetHistoricalCandles(string symbol, string interval, int totalCandles)
    {
        var allCandles = new List<Candle>();
        int limit = 1000;
        long? endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        while (allCandles.Count < totalCandles)
        {
            var candles = await GetCandlesAsync(symbol, interval, limit, null, endTime);
            if (candles.Count == 0) break;

            allCandles.InsertRange(0, candles);

            if (candles.Count < limit) break;

            endTime = new DateTimeOffset(candles[0].OpenTime).ToUnixTimeMilliseconds() - 1;
            await Task.Delay(500);
        }

        if (allCandles.Count > totalCandles)
            allCandles.RemoveRange(0, allCandles.Count - totalCandles);

        return allCandles;
    }

    static List<decimal> CalculateExponentialAtr(List<Candle> candles, int period)
    {
        var trs = new List<decimal>();

        for (int i = 1; i < candles.Count; i++)
        {
            var current = candles[i];
            var prev = candles[i - 1];

            var highLow = current.High - current.Low;
            var highClose = Math.Abs(current.High - prev.Close);
            var lowClose = Math.Abs(current.Low - prev.Close);

            trs.Add(Math.Max(highLow, Math.Max(highClose, lowClose)));
        }

        var atr = new List<decimal>();
        decimal multiplier = 2m / (period + 1);
        decimal? prevAtr = null;

        for (int i = 0; i < trs.Count; i++)
        {
            if (i < period)
            {
                atr.Add(0);
            }
            else if (i == period)
            {
                decimal sum = 0;
                for (int j = i - period; j <= i; j++)
                    sum += trs[j];
                prevAtr = sum / period;
                atr.Add(prevAtr.Value);
            }
            else
            {
                var currentAtr = (trs[i] - prevAtr.Value) * multiplier + prevAtr.Value;
                atr.Add(currentAtr);
                prevAtr = currentAtr;
            }
        }

        atr.Insert(0, 0); // align with candles count
        return atr;
    }

    // Calculate RSI
    static List<decimal> CalculateRsi(List<decimal> closes, int period)
    {
        var rsi = new List<decimal>();

        decimal gain = 0;
        decimal loss = 0;

        for (int i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change >= 0) gain += change;
            else loss -= change;
        }

        gain /= period;
        loss /= period;

        decimal rs = loss == 0 ? 0 : gain / loss;
        rsi.Add(loss == 0 ? 100 : 100 - (100 / (1 + rs)));

        for (int i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            decimal currentGain = change > 0 ? change : 0;
            decimal currentLoss = change < 0 ? -change : 0;

            gain = ((gain * (period - 1)) + currentGain) / period;
            loss = ((loss * (period - 1)) + currentLoss) / period;

            rs = loss == 0 ? 0 : gain / loss;
            rsi.Add(loss == 0 ? 100 : 100 - (100 / (1 + rs)));
        }

        // Pad with zeros at start to align with closes list length
        for (int i = 0; i < closes.Count - rsi.Count; i++)
            rsi.Insert(0, 0);

        return rsi;
    }

    // Calculate Stochastic RSI %K and %D
    static List<decimal> CalculateStochRsi(List<decimal> closes, int rsiPeriod, int stochPeriod, int kSmooth, int dSmooth)
    {
        var rsi = CalculateRsi(closes, rsiPeriod);
        var stochRsiRaw = new List<decimal>();

        for (int i = 0; i < rsi.Count; i++)
        {
            if (i < stochPeriod)
            {
                stochRsiRaw.Add(0);
                continue;
            }
            var rsiSlice = rsi.GetRange(i - stochPeriod + 1, stochPeriod);
            var minRsi = rsiSlice.Min();
            var maxRsi = rsiSlice.Max();

            if (maxRsi - minRsi == 0)
                stochRsiRaw.Add(0);
            else
                stochRsiRaw.Add((rsi[i] - minRsi) / (maxRsi - minRsi) * 100);
        }

        // Smooth %K
        var stochK = Smooth(stochRsiRaw, kSmooth);

        // Smooth %D (signal line)
        var stochD = Smooth(stochK, dSmooth);

        return stochD;
    }

    // Simple moving average smoothing
    static List<decimal> Smooth(List<decimal> values, int period)
    {
        var smoothed = new List<decimal>();

        for (int i = 0; i < values.Count; i++)
        {
            if (i < period - 1)
            {
                smoothed.Add(0);
                continue;
            }
            var window = values.GetRange(i - period + 1, period);
            smoothed.Add(window.Average());
        }

        return smoothed;
    }

    static void Backtest(List<Candle> candles, List<decimal> stochRsiValues, List<decimal> atrValues)
    {
        List<Trade> trades = new();
        Trade? currentTrade = null;
        decimal atrMultiplier = 1.5m;
        decimal takeProfitMultiplier = 2m;  // <-- New Take Profit multiplier

        decimal equity = 1000m;
        decimal riskPercent = 0.02m;
        decimal minAtrThreshold = 100m;

        for (int i = 1; i < candles.Count; i++)
        {
            if (stochRsiValues.Count != candles.Count || atrValues.Count != candles.Count)
            {
                throw new Exception("Indicator lists length mismatch with candles");
            }

            var candle = candles[i];
            var atr = atrValues[i];
            var stochRsi = stochRsiValues[i];

            if (atr == 0) continue;

            // Manage open trade
            if (currentTrade != null && currentTrade.IsOpen)
            {
                decimal stopLoss = currentTrade.EntryPrice - atrMultiplier * atr;
                decimal takeProfit = currentTrade.EntryPrice + takeProfitMultiplier * atr;

                // Check for stop loss hit
                if (candle.Low <= stopLoss)
                {
                    currentTrade.ExitPrice = stopLoss;
                    currentTrade.ExitTime = candle.OpenTime;
                    trades.Add(currentTrade);

                    decimal pl = (currentTrade.ExitPrice.Value - currentTrade.EntryPrice) * currentTrade.PositionSize;
                    equity += pl;
                    currentTrade = null;
                    continue;
                }

                // Check for take profit hit
                if (candle.High >= takeProfit)
                {
                    currentTrade.ExitPrice = takeProfit;
                    currentTrade.ExitTime = candle.OpenTime;
                    trades.Add(currentTrade);

                    decimal pl = (currentTrade.ExitPrice.Value - currentTrade.EntryPrice) * currentTrade.PositionSize;
                    equity += pl;
                    currentTrade = null;
                    continue;
                }
            }

            // Entry logic: Use StochRSI < 20 (oversold) to enter long
            if (currentTrade == null && atr >= minAtrThreshold)
            {
                if (stochRsi < 20)
                {
                    decimal riskAmount = equity * riskPercent;
                    decimal stopLossDistance = atrMultiplier * atr;
                    decimal positionSize = riskAmount / stopLossDistance;

                    currentTrade = new Trade
                    {
                        EntryTime = candle.OpenTime,
                        EntryPrice = candle.Close,
                        PositionSize = positionSize,
                        Type = PositionType.Long
                    };
                }
            }
        }

        // Exit last trade at end
        if (currentTrade != null && currentTrade.IsOpen)
        {
            currentTrade.ExitPrice = candles[^1].Close;
            currentTrade.ExitTime = candles[^1].OpenTime;
            trades.Add(currentTrade);

            decimal pl = (currentTrade.ExitPrice.Value - currentTrade.EntryPrice) * currentTrade.PositionSize;
            equity += pl;
        }

        // Summary
        decimal totalProfit = equity - 1000m;
        int wins = 0;

        foreach (var t in trades)
        {
            decimal pl = (t.ExitPrice.Value - t.EntryPrice) * t.PositionSize;
            if (pl > 0) wins++;
            Console.WriteLine($"Trade: Entry {t.EntryTime}, Exit {t.ExitTime}, Size: {t.PositionSize:F4}, P/L: {pl:F2}");
        }

        int totalTrades = trades.Count;
        decimal winRate = totalTrades > 0 ? (decimal)wins / totalTrades * 100 : 0;
        var profitFactor = CalculateProfitFactor(trades);


        Console.WriteLine($"\n--- Performance Summary ---");
        Console.WriteLine($"Starting Capital: €1000.00");
        Console.WriteLine($"Ending Capital: €{equity:F2}");
        Console.WriteLine($"Total Trades: {totalTrades}, Wins: {wins}, Win Rate: {winRate:F2}%");
        Console.WriteLine($"Total Profit: €{totalProfit:F2}");
        Console.WriteLine($"Total Trades: {trades.Count}");
        Console.WriteLine($"Winning Trades: {trades.Count(t => t.ProfitLoss > 0)}");
        Console.WriteLine($"Losing Trades: {trades.Count(t => t.ProfitLoss < 0)}");
        Console.WriteLine($"Win Rate: {trades.Count(t => t.ProfitLoss > 0) / (double)trades.Count:P2}");
        Console.WriteLine($"Net Profit: {trades.Sum(t => t.ProfitLoss):F2}");
        Console.WriteLine($"Profit Factor: {CalculateProfitFactor(trades):F2}");

    }




    public enum PositionType { Long }

    public class Trade
    {
        public DateTime EntryTime { get; set; }
        public decimal EntryPrice { get; set; }
        public DateTime? ExitTime { get; set; }
        public decimal? ExitPrice { get; set; }
        public decimal PositionSize { get; set; }
        public PositionType Type { get; set; }
        public bool IsOpen => ExitPrice == null;

        public decimal ProfitLoss
    {
        get
        {
            if (ExitPrice == null)
                return 0;

            return (ExitPrice.Value - EntryPrice) * PositionSize;
        }
    }
    }

    public static decimal CalculateProfitFactor(List<Trade> trades)
    {
        var grossProfit = trades.Where(t => t.ProfitLoss > 0).Sum(t => t.ProfitLoss);
        var grossLoss = trades.Where(t => t.ProfitLoss < 0).Sum(t => Math.Abs(t.ProfitLoss));

        return grossLoss == 0 ? grossProfit : grossProfit / grossLoss;
    }

}

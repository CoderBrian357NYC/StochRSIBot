# StochRSIBot

## Description
StochRSIBot is a long-only cryptocurrency trading bot that leverages the Stochastic RSI indicator to generate buy and sell signals. It uses the Average True Range (ATR) for position sizing, adapting trade sizes based on market volatility. The bot ensures only one trade is open at a time to manage risk effectively.

## Features
- Trades based on Stochastic RSI indicator signals.
- Position sizing using ATR and an adjustable multiplier.
- Supports Binance API for live trading.
- Restricts to a single open position at any time.
- Configurable parameters for flexibility.

## Usage
1. Configure Binance API credentials securely.
2. Adjust parameters such as `atrMultiplier`, initial equity, and trading pair in the bot’s config or code.
3. Run the bot to start live trading.

## Requirements
- .NET 8.0 SDK or later
- Binance API key and secret

## Important Notes
- Make sure to review and understand the strategy logic before deploying with real funds.
- Always test with paper trading or small amounts initially.
- This bot is designed for long-only trading.




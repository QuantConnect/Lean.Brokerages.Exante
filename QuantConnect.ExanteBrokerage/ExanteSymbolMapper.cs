/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using QuantConnect.Brokerages;
using Log = QuantConnect.Logging.Log;
using System.Linq;

namespace QuantConnect.ExanteBrokerage
{
    public static class ExanteMarket
    {
        public const string NASDAQ = "NASDAQ";
        public const string ARCA = "ARCA";
        public const string AMEX = "AMEX";
        public const string EXANTE = "EXANTE";
        public const string USD = "USD";
        public const string USCORP = "USCORP";
        public const string EUR = "EUR";
        public const string GBP = "GBP";
        public const string ASN = "ASN";
        public const string CAD = "CAD";
        public const string AUD = "AUD";
        public const string ARG = "ARG";
    }

    public class ExanteSymbolLocal
    {
        public string Exchange { get; }
        public string SymbolId { get; }

        public ExanteSymbolLocal(string exchange, string symbolId)
        {
            Exchange = exchange;
            SymbolId = symbolId;
        }

        public string Id => $"{SymbolId}.{Exchange}";
    }

    /// <summary>
    /// Provides the mapping between Lean symbols and Exante symbols.
    /// </summary>
    public class ExanteSymbolMapper : ISymbolMapper
    {
        private readonly Dictionary<string, string> _tickerToExchange;
        private readonly ExanteClientWrapper _client;
        private readonly HashSet<string> _supportedCryptoCurrencies;

        public ExanteSymbolMapper(
            ExanteClientWrapper client,
            HashSet<string> supportedCryptoCurrencies)
        {
            _client = client;
            _supportedCryptoCurrencies = supportedCryptoCurrencies;
            _tickerToExchange = ComposeTickerToExchangeDictionary();
        }

        private Dictionary<string, string> ComposeTickerToExchangeDictionary()
        {
            var tickerToExchange = new Dictionary<string, string>();

            void AddMarketSymbols(string market, Func<string, List<string>> tickersByMarket)
            {
                market = market.LazyToUpper();
                var symbols = tickersByMarket(market);
                foreach (var sym in symbols)
                {
                    if (tickerToExchange.ContainsKey(sym))
                    {
                        if (market != tickerToExchange[sym])
                        {
                            Log.Error($"Symbol {sym} occurs on two exchanges: {tickerToExchange[sym]} {market}");
                        }
                    }
                    else
                    {
                        tickerToExchange.Add(sym, market);
                    }
                }
            }

            foreach (var market in new[]
            {
                ExanteMarket.NASDAQ, ExanteMarket.ARCA, ExanteMarket.AMEX, ExanteMarket.EXANTE,
                ExanteMarket.USD, ExanteMarket.USCORP, ExanteMarket.EUR, ExanteMarket.GBP,
                ExanteMarket.ASN, ExanteMarket.CAD, ExanteMarket.AUD, ExanteMarket.ARG,
                Market.CBOE, Market.CME, "OTCMKTS", Market.NYMEX, Market.CBOT, Market.COMEX, Market.ICE,
            })
            {
                AddMarketSymbols(market,
                    m => _client.GetSymbolsByExchange(m).Data.Select(x => x.Ticker).ToList());
            }

            AddMarketSymbols("USD", m => _supportedCryptoCurrencies.ToList());

            return tickerToExchange;
        }

        /// <summary>
        /// Converts a Lean symbol instance to a brokerage symbol
        /// </summary>
        /// <param name="symbol">A Lean symbol instance</param>
        /// <returns>The brokerage symbol</returns>
        public string GetBrokerageSymbol(Symbol symbol)
        {
            return GetExanteSymbol(symbol).Id;
        }

        public ExanteSymbolLocal GetExanteSymbol(Symbol symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol?.Value))
                throw new ArgumentException($"Invalid symbol: {(symbol == null ? "null" : symbol.ToString())}");

            var ticker = symbol.ID.Symbol;

            if (string.IsNullOrWhiteSpace(ticker))
                throw new ArgumentException($"Invalid symbol: {symbol}");

            if (symbol.ID.SecurityType != SecurityType.Forex &&
                symbol.ID.SecurityType != SecurityType.Equity &&
                symbol.ID.SecurityType != SecurityType.Index &&
                symbol.ID.SecurityType != SecurityType.Option &&
                symbol.ID.SecurityType != SecurityType.Future &&
                symbol.ID.SecurityType != SecurityType.Cfd &&
                symbol.ID.SecurityType != SecurityType.Crypto &&
                symbol.ID.SecurityType != SecurityType.Index)
                throw new ArgumentException($"Invalid security type: {symbol.ID.SecurityType}");

            if (symbol.ID.SecurityType == SecurityType.Forex && ticker.Length != 6)
                throw new ArgumentException($"Forex symbol length must be equal to 6: {symbol.Value}");

            var symbolId = symbol.ID.SecurityType switch
            {
                SecurityType.Option => symbol.Underlying.ID.Symbol,
                SecurityType.Future => symbol.ID.Symbol,
                SecurityType.Equity => symbol.ID.Symbol,
                SecurityType.Index => ticker,
                SecurityType.Forex => ticker[..3] + "/" + ticker[3..],
                SecurityType.Crypto => ticker[..3],
                _ => ticker
            };

            symbolId = symbolId.LazyToUpper();

            if (!_tickerToExchange.TryGetValue(symbolId, out var exchange))
            {
                throw new ArgumentException($"Unknown exchange for symbol '{symbolId}'");
            }

            return new ExanteSymbolLocal(exchange, symbolId);
        }

        /// <summary>
        /// Converts a brokerage symbol to a Lean symbol instance
        /// </summary>
        /// <param name="brokerageSymbol">The brokerage symbol</param>
        /// <param name="securityType">The security type</param>
        /// <param name="market">The market</param>
        /// <param name="expirationDate">Expiration date of the security(if applicable)</param>
        /// <param name="strike">The strike of the security (if applicable)</param>
        /// <param name="optionRight">The option right of the security (if applicable)</param>
        /// <returns>A new Lean Symbol instance</returns>
        public Symbol GetLeanSymbol(
            string brokerageSymbol,
            SecurityType securityType,
            string market,
            DateTime expirationDate = default(DateTime),
            decimal strike = 0,
            OptionRight optionRight = OptionRight.Call
        )
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
            {
                throw new ArgumentException("Invalid symbol: " + brokerageSymbol);
            }

            if (securityType != SecurityType.Forex &&
                securityType != SecurityType.Equity &&
                securityType != SecurityType.Index &&
                securityType != SecurityType.Option &&
                securityType != SecurityType.IndexOption &&
                securityType != SecurityType.Future &&
                securityType != SecurityType.FutureOption &&
                securityType != SecurityType.Cfd &&
                securityType != SecurityType.Crypto)
            {
                throw new ArgumentException("Invalid security type: " + securityType);
            }

            Symbol symbol;
            switch (securityType)
            {
                case SecurityType.Option:
                    symbol = Symbol.CreateOption(brokerageSymbol, market, OptionStyle.American,
                        optionRight, strike, expirationDate);
                    break;

                case SecurityType.Future:
                    symbol = Symbol.CreateFuture(brokerageSymbol, market, expirationDate);
                    break;

                default:
                    symbol = Symbol.Create(brokerageSymbol, securityType, market);
                    break;
            }

            return symbol;
        }

        public string GetExchange(string symbolId)
        {
            var brokerageSymbol = GetExanteSymbol(symbolId);

            if (!_tickerToExchange.TryGetValue(brokerageSymbol.SymbolId, out var exchange))
            {
                throw new ArgumentException($"Unknown exchange for symbol '{symbolId}'");
            }

            return exchange;
        }
    }
}
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
using System.Linq;
using Exante.Net.Enums;
using QuantConnect.Data;
using QuantConnect.Data.Market;

namespace QuantConnect.ExanteBrokerage.ToolBox
{
    public class ExanteDataDownloader : IDataDownloader, IDisposable
    {
        private readonly ExanteClientWrapper _clientWrapper;
        private readonly ExanteSymbolMapper _symbolMapper;

        /// <summary>
        /// Creates a new instance of <see cref="ExanteDataDownloader" />
        /// </summary>
        public ExanteDataDownloader()
        {
            var supportedCryptoCurrencies = ExanteBrokerage.SupportedCryptoCurrencies;
            _clientWrapper = new ExanteClientWrapper(ExanteBrokerageOptions.FromConfig().ExanteClientOptions());
            _symbolMapper = new ExanteSymbolMapper(_clientWrapper, supportedCryptoCurrencies);
        }

        /// <summary>
        /// Get historical data enumerable for a single symbol, type and resolution given this start and end time (in UTC).
        /// </summary>
        /// <param name="symbol">Symbol for the data we're looking for.</param>
        /// <param name="resolution">Resolution of the data request</param>
        /// <param name="startUtc">Start time of the data in UTC</param>
        /// <param name="endUtc">End time of the data in UTC</param>
        /// <returns>Enumerable of base data for this symbol</returns>
        public IEnumerable<BaseData> Get(Symbol symbol, Resolution resolution, DateTime startUtc, DateTime endUtc)
        {
            var exanteSymbol = _symbolMapper.GetBrokerageSymbol(symbol);
            var timeframe = resolution switch
            {
                Resolution.Tick => throw new NotSupportedException(),
                Resolution.Second => throw new NotSupportedException(),
                Resolution.Minute => ExanteCandleTimeframe.Minute1,
                Resolution.Hour => ExanteCandleTimeframe.Hour1,
                Resolution.Daily => ExanteCandleTimeframe.Day1,
                _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null)
            };
            var period = resolution.ToTimeSpan();
            var candles =
                _clientWrapper.GetCandles(exanteSymbol, timeframe, startUtc, endUtc).Data.ToList();
            foreach (var candle in candles)
            {
                yield return new TradeBar(
                    candle.Date, symbol,
                    candle.Open, candle.High, candle.Low, candle.Close,
                    candle.Volume ?? 0m, period
                );
            }
        }

        public void Dispose()
        {
            _clientWrapper.Dispose();
        }

        public Symbol GetSymbol(string ticker, string market, SecurityType securityType)
        {
            return _symbolMapper.GetLeanSymbol(ticker, securityType, market);
        }
    }
}
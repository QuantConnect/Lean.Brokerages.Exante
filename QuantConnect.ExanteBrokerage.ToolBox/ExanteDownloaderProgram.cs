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
using QuantConnect.Data;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace QuantConnect.ExanteBrokerage.ToolBox
{
    public static class ExanteDownloaderProgram
    {
        /// <summary>
        /// Primary entry point to the program.
        /// </summary>
        public static void DataDownloader(IList<string> tickers,
            string market, SecurityType securityType, string resolution,
            DateTime fromDate, DateTime toDate)
        {
            if (resolution.IsNullOrEmpty() || tickers.IsNullOrEmpty())
            {
                Console.WriteLine("ExanteDownloader ERROR: '--tickers=' or '--resolution=' parameter is missing");
                Console.WriteLine("--tickers=eg BTCUSD");
                Console.WriteLine("--resolution=Minute/Hour/Daily/All");
                Environment.Exit(1);
            }

            try
            {
                var allResolutions = resolution.ToLowerInvariant() == "all";
                var resolutions = allResolutions
                    ? new[] { Resolution.Minute, Resolution.Hour, Resolution.Daily }
                    : new[] { (Resolution)Enum.Parse(typeof(Resolution), resolution) };

                using var downloader = new ExanteDataDownloader();
                foreach (var ticker in tickers)
                {
                    // Download the data
                    var symbol = downloader.GetSymbol(ticker, market, securityType);
                    foreach (var castResolution in resolutions)
                    {
                        var data = downloader.Get(symbol, castResolution, fromDate, toDate);

                        // Save the data (single resolution)
                        var writer = new LeanDataWriter(castResolution, symbol, Globals.DataFolder);
                        writer.Write(data);
                    }
                }
            }
            catch (Exception err)
            {
                Log.Error(err);
            }
        }
    }
}
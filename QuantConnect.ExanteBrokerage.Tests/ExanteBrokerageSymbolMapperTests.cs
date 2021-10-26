﻿/*
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

using NUnit.Framework;

namespace QuantConnect.ExanteBrokerage.Tests
{
    [TestFixture]
    public class ExanteBrokerageSymbolMapperTests
    {
        ExanteSymbolMapper SymbolMapper()
        {
            var supportedCryptoCurrencies = ExanteBrokerage.SupportedCryptoCurrencies;
            var clientWrapper = new ExanteClientWrapper(ExanteBrokerageFactory.CreateExanteClientOptions());
            var symbolMapper = new ExanteSymbolMapper(clientWrapper, supportedCryptoCurrencies);
            return symbolMapper;
        }

        [Test]
        public void ReturnsCorrectLeanSymbol()
        {
            var mapper = SymbolMapper();

            var symbol = mapper.GetLeanSymbol("EUR/USD", SecurityType.Forex, Market.Oanda);
            Assert.AreEqual("EURUSD", symbol.Value);
            Assert.AreEqual(SecurityType.Forex, symbol.ID.SecurityType);
            Assert.AreEqual(Market.Oanda, symbol.ID.Market);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol()
        {
            var mapper = SymbolMapper();

            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.Oanda);
            var brokerageSymbol = mapper.GetBrokerageSymbol(symbol);
            Assert.AreEqual("EUR/USD.EXANTE", brokerageSymbol);
        }
    }
}
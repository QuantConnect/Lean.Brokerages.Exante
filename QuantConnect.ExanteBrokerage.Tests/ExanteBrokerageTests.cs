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
using System.Linq;
using NUnit.Framework;
using QuantConnect.Configuration;
using QuantConnect.Tests;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Securities;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.ExanteBrokerage.Tests
{
    [TestFixture]
    public partial class ExanteBrokerageTests : BrokerageTests
    {
        protected override Symbol Symbol { get; }
        protected override SecurityType SecurityType { get; }
        private readonly ExanteBrokerage _brokerage;
        private decimal? _askPrice;

        public ExanteBrokerageTests()
        {
            Symbol = Symbol.Create("ETHUSD", SecurityType.Crypto, Market.USA);

            var options = ExanteBrokerageOptions.FromConfig();

            _brokerage = new ExanteBrokerage(options, new AggregationManager(), new ExanteSecurityProvider());
        }

        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
        {
            return _brokerage;
        }

        protected override bool IsAsync()
        {
            return false;
        }

        protected override decimal GetAskPrice(Symbol symbol)
        {
            if (_askPrice is null)
            {
                var brokerageSymbol = _brokerage.SymbolMapper.GetBrokerageSymbol(symbol);
                const int ticksCount = 5;
                var ticks = _brokerage.Client.GetTicks(brokerageSymbol, limit: ticksCount).Data.ToList();

                var tick = ticks.Find(x => x.Ask?.ToList()[0].Price != null);
                _askPrice = tick?.Ask?.ToList()[0].Price;
                if (_askPrice is null)
                {
                    throw new Exception($"{ticksCount} ticks are without ask price. Try to increase `ticksCount`");
                }
            }

            return _askPrice.Value;
        }


        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static TestCaseData[] OrderParameters()
        {
            return new[]
            {
                new TestCaseData(new MarketOrderTestParameters(Symbols.ETHUSD)).SetName("MarketOrder"),
                new TestCaseData(new LimitOrderTestParameters(Symbols.ETHUSD, 10000m, 0.01m)).SetName("LimitOrder"),
                new TestCaseData(new StopMarketOrderTestParameters(Symbols.ETHUSD, 10000m, 0.01m)).SetName(
                    "StopMarketOrder"),
                new TestCaseData(new StopLimitOrderTestParameters(Symbols.ETHUSD, 10000m, 0.01m)).SetName(
                    "StopLimitOrder"),
                new TestCaseData(new LimitIfTouchedOrderTestParameters(Symbols.ETHUSD, 10000m, 0.01m)).SetName(
                    "LimitIfTouchedOrder").Ignore("`LimitIfTouchedOrder` Is not supported by Exante"),
            };
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CancelOrders(OrderTestParameters parameters)
        {
            base.CancelOrders(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromZero(OrderTestParameters parameters)
        {
            base.LongFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromLong(OrderTestParameters parameters)
        {
            base.CloseFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromZero(OrderTestParameters parameters)
        {
            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromShort(OrderTestParameters parameters)
        {
            base.CloseFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromLong(OrderTestParameters parameters)
        {
            if (parameters is LimitOrderTestParameters or StopLimitOrderTestParameters or StopMarketOrderTestParameters)
            {
                Assert.Ignore("Replacing is not supported for this type of instrument. " +
                              "Only cancellation and placing new order");
            }

            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromShort(OrderTestParameters parameters)
        {
            if (parameters is LimitOrderTestParameters or StopLimitOrderTestParameters)
            {
                Assert.Ignore("Replacing is not supported for this type of instrument. " +
                              "Only cancellation and placing new order");
            }

            base.LongFromShort(parameters);
        }
    }
}
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
using QuantConnect.Benchmarks;
using QuantConnect.Brokerages;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;
using static QuantConnect.StringExtensions;
using static QuantConnect.Util.SecurityExtensions;


namespace QuantConnect.ExanteBrokerage
{
    /// <summary>
    /// Exante Brokerage Model Implementation for Back Testing.
    /// </summary>
    public class ExanteBrokerageModel : DefaultBrokerageModel
    {
        private const decimal EquityLeverage = 1.2m;

        /// <summary>
        /// Get the benchmark for this model
        /// </summary>
        /// <param name="securities">SecurityService to create the security with if needed</param>
        /// <returns>The benchmark for this brokerage</returns>
        public override IBenchmark GetBenchmark(SecurityManager securities)
        {
            var symbol = Symbol.Create("SPY", SecurityType.Equity, Market.USA);
            return SecurityBenchmark.CreateInstance(securities, symbol);
        }

        /// <summary>
        /// Returns true if the brokerage could accept this order. This takes into account
        /// order type, security type, and order size limits.
        /// </summary>
        /// <remarks>
        /// For example, a brokerage may have no connectivity at certain times, or an order rate/size limit
        /// </remarks>
        /// <param name="security">The security being ordered</param>
        /// <param name="order">The order to be processed</param>
        /// <param name="message">If this function returns false, a brokerage message detailing why the order may not be submitted</param>
        /// <returns>True if the brokerage could process the order, false otherwise</returns>
        public override bool CanSubmitOrder(Security security, Order order, out BrokerageMessageEvent message)
        {
            message = null;

            if (security.Type != SecurityType.Forex &&
                security.Type != SecurityType.Equity &&
                security.Type != SecurityType.Index &&
                security.Type != SecurityType.Option &&
                security.Type != SecurityType.Future &&
                security.Type != SecurityType.Cfd &&
                security.Type != SecurityType.Crypto &&
                security.Type != SecurityType.Index)
            {
                message = new BrokerageMessageEvent(BrokerageMessageType.Warning, "NotSupported",
                    Invariant(
                        $"The {nameof(ExanteBrokerageModel)} does not support {security.Type} security type.")
                );
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a new fee model that represents this brokerage's fee structure
        /// </summary>
        /// <param name="security">The security to get a fee model for</param>
        /// <returns>The new fee model for this brokerage</returns>
        public override IFeeModel GetFeeModel(Security security)
        {
            return new ExanteFeeModel(
                forexCommissionRate: 0.25m
            );
        }

        /// <summary>
        /// Exante global leverage rule
        /// </summary>
        /// <param name="security"></param>
        /// <returns></returns>
        public override decimal GetLeverage(Security security)
        {
            if (AccountType == AccountType.Cash || security.IsInternalFeed() || security.Type == SecurityType.Base)
            {
                return 1m;
            }

            if (security.Type == SecurityType.Equity)
            {
                return EquityLeverage;
            }

            throw new ArgumentException($"Invalid security type: {security.Type}", nameof(security));
        }
    }
}

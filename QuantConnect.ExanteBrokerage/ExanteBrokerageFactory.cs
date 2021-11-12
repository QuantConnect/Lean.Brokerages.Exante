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
using QuantConnect.Packets;
using QuantConnect.Brokerages;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System.Collections.Generic;
using QLNet;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Util;

namespace QuantConnect.ExanteBrokerage
{
    /// <summary>
    /// </summary>
    public class ExanteBrokerageFactory : BrokerageFactory
    {
        /// <summary>
        /// Gets the brokerage data required to run the brokerage from configuration/disk
        /// </summary>
        /// <remarks>
        /// The implementation of this property will create the brokerage data dictionary required for
        /// running live jobs. See <see cref="IJobQueueHandler.NextJob"/>
        /// </remarks>
        public override Dictionary<string, string> BrokerageData => new()
        {
            { "exante-client-id", Config.Get("exante-client-id") },
            { "exante-application-id", Config.Get("exante-application-id") },
            { "exante-shared-key", Config.Get("exante-shared-key") },
            { "exante-account-id", Config.Get("exante-account-id") },
            { "exante-platform-type", Config.Get("exante-platform-type") },
        };

        public ExanteBrokerageFactory() : base(typeof(ExanteBrokerage))
        {
        }

        /// <summary>
        /// Gets a brokerage model that can be used to model this brokerage's unique behaviors
        /// </summary>
        /// <param name="orderProvider">The order provider</param>
        public override IBrokerageModel GetBrokerageModel(IOrderProvider orderProvider)
        {
            return new ExanteBrokerageModel();
        }

        /// <summary>
        /// Creates a new IBrokerage instance
        /// </summary>
        /// <param name="job">The job packet to create the brokerage for</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <returns>A new brokerage instance</returns>
        public override IBrokerage CreateBrokerage(LiveNodePacket job, IAlgorithm algorithm)
        {
            var errors = new List<string>();

            // read values from the brokerage data
            var clientId = Read<string>(job.BrokerageData, "exante-client-id", errors);
            var applicationId = Read<string>(job.BrokerageData, "exante-application-id", errors);
            var sharedKey = Read<string>(job.BrokerageData, "exante-shared-key", errors);
            var accountId = Read<string>(job.BrokerageData, "exante-account-id", errors);
            var platformType = Read<string>(job.BrokerageData, "exante-platform-type", errors);

            if (!errors.empty())
            {
                // if we had errors then we can't create the instance
                throw new ArgumentException(string.Join(Environment.NewLine, errors));
            }

            var options = new ExanteBrokerageOptions(accountId, clientId, applicationId, sharedKey, platformType);
            var aggregator = Composer.Instance.GetExportedValueByTypeName<IDataAggregator>(
                Config.Get("data-aggregator", "QuantConnect.Lean.Engine.DataFeeds.AggregationManager")
            );

            var brokerage = new ExanteBrokerage(options, aggregator);
            Composer.Instance.AddPart<IDataQueueHandler>(brokerage);

            return brokerage;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
        }
    }
}
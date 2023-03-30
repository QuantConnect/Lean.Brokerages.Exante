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

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using CryptoExchange.Net.Objects;
using Exante.Net;
using Exante.Net.Enums;
using Exante.Net.Objects;
using QuantConnect.Util;

namespace QuantConnect.ExanteBrokerage
{
    /// <summary>
    /// Wrapper class for a Exante REST API client
    /// </summary>
    public class ExanteClientWrapper : IDisposable
    {
        private readonly ConcurrentDictionary<string, ExanteSymbol> _symbolIdToSymbolCache = new();
        private readonly ExanteClient _client;
        private static readonly RateGate ExanteClientRateLimiter = new(1, TimeSpan.FromMinutes(1.15));

        /// <summary>Get Exante client for stream data</summary>
        public ExanteStreamClient StreamClient { get; private set; }

        /// <summary>
        /// Creates instance of wrapper class for a Exante REST API client
        /// </summary>
        public ExanteClientWrapper(ExanteClientOptions clientOptions)
        {
            _client = new ExanteClient(clientOptions);
            StreamClient = new ExanteStreamClient(clientOptions);
        }

        private static void CheckIfResponseOk<T>(
            WebCallResult<T> response,
            HttpStatusCode statusCode = HttpStatusCode.OK
        )
        {
            if (!(response.ResponseStatusCode == statusCode && response.Success))
            {
                throw new Exception(
                    $"ExanteBrokerage.GetActiveOrders: request failed: [{response.ResponseStatusCode}], Content: {response.Data}, ErrorMessage: {response.Error}");
            }
        }

        /// <summary>Get account summary</summary>
        /// <returns>Summary for the specified account</returns>
        public ExanteAccountSummary GetAccountSummary(string accountId, string reportCurrency)
        {
            var response =
                _client.GetAccountSummaryAsync(accountId, reportCurrency).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response.Data;
        }

        /// <summary>Place order</summary>
        /// <param name="accountId">User account to place order</param>
        /// <param name="symbolId">Order instrument</param>
        /// <param name="type">Order type</param>
        /// <param name="side">Order side</param>
        /// <param name="quantity">Order quantity</param>
        /// <param name="duration">Order duration</param>
        /// <param name="limitPrice">Order limit price if applicable</param>
        /// <param name="stopPrice">Order stop price if applicable</param>
        /// <param name="stopLoss">Optional price of stop loss order</param>
        /// <param name="takeProfit">Optional price of take profit order</param>
        /// <param name="placeInterval">Order place interval, twap orders only</param>
        /// <param name="clientTag">Optional client tag to identify or group orders</param>
        /// <param name="parentId">ID of an order on which this order depends</param>
        /// <param name="ocoGroupId">One-Cancels-the-Other group ID if set</param>
        /// <param name="gttExpiration">Order expiration if applicable</param>
        /// <param name="priceDistance">Order price distance, trailing stop orders only</param>
        /// <param name="partQuantity">Order partial quantity, twap and Iceberg orders only</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>New trading order</returns>
        public WebCallResult<IEnumerable<ExanteOrder>> PlaceOrder(
            string accountId,
            string symbolId,
            ExanteOrderType type,
            ExanteOrderSide side,
            decimal quantity,
            ExanteOrderDuration duration,
            decimal? limitPrice = null,
            decimal? stopPrice = null,
            decimal? stopLoss = null,
            decimal? takeProfit = null,
            int? placeInterval = null,
            string? clientTag = null,
            Guid? parentId = null,
            Guid? ocoGroupId = null,
            DateTime? gttExpiration = null,
            int? priceDistance = null,
            decimal? partQuantity = null,
            CancellationToken ct = default(CancellationToken)
        )
        {
            var response = _client.PlaceOrderAsync(
                accountId,
                symbolId,
                type,
                side,
                quantity,
                duration,
                limitPrice,
                stopPrice,
                stopLoss,
                takeProfit,
                placeInterval,
                clientTag,
                parentId,
                ocoGroupId,
                gttExpiration,
                priceDistance,
                partQuantity,
                ct).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response, HttpStatusCode.Created);
            return response;
        }

        /// <summary>Get active orders</summary>
        /// <returns>List of active trading orders</returns>
        public WebCallResult<IEnumerable<ExanteOrder>> GetActiveOrders()
        {
            var response = _client.GetActiveOrdersAsync().SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response;
        }

        /// <summary>Get ticks</summary>
        /// <returns>List of ticks for the specified financial instrument</returns>
        public WebCallResult<IEnumerable<ExanteTick>> GetTicks(
            string symbolId,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 60,
            ExanteTickType tickType = ExanteTickType.Quotes,
            CancellationToken ct = default(CancellationToken)
        )
        {
            var response = _client.GetTicksAsync(
                symbolId,
                from,
                to,
                limit,
                tickType,
                ct
            ).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response;
        }

        /// <summary>Get OHLC candles</summary>
        /// <returns>List of OHLC candles for the specified financial instrument and duration</returns>
        public WebCallResult<IEnumerable<ExanteCandle>> GetCandles(
            string symbolId,
            ExanteCandleTimeframe timeframe,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 60,
            ExanteTickType tickType = ExanteTickType.Quotes,
            CancellationToken ct = default(CancellationToken)
        )
        {
            ExanteClientRateLimiter.WaitToProceed();

            var response = _client.GetCandlesAsync(
                symbolId,
                timeframe,
                from,
                to,
                limit,
                tickType,
                ct
            ).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response;
        }

        /// <summary>Get instrument</summary>
        /// <returns>Instrument available for authorized user</returns>
        public ExanteSymbol GetSymbol(
            string symbolId,
            CancellationToken ct = default(CancellationToken)
        )
        {
            if (_symbolIdToSymbolCache.TryGetValue(symbolId, out var symbol))
            {
                return symbol;
            }

            var response =
                _client.GetSymbolAsync(symbolId, ct).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            symbol = response.Data;
            _symbolIdToSymbolCache.TryAdd(symbolId, symbol);

            return symbol;
        }

        /// <summary>Get order</summary>
        /// <returns>Order with specified identifier</returns>
        public WebCallResult<ExanteOrder> GetOrder(
            Guid orderId,
            CancellationToken ct = default
        )
        {
            var response =
                _client.GetOrderAsync(orderId, ct).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response;
        }

        /// <summary>Modify order</summary>
        /// <param name="orderId">Order identifier</param>
        /// <param name="action">Order modification action</param>
        /// <param name="quantity">New order quantity to replace</param>
        /// <param name="stopPrice">New order stop price if applicable</param>
        /// <param name="priceDistance">New order price distance if applicable</param>
        /// <param name="limitPrice">New order limit price if applicable</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns></returns>
        public WebCallResult<ExanteOrder> ModifyOrder(
            Guid orderId,
            ExanteOrderAction action,
            Decimal? quantity = null,
            Decimal? stopPrice = null,
            int? priceDistance = null,
            Decimal? limitPrice = null,
            CancellationToken ct = default(CancellationToken)
        )
        {
            var response = _client.ModifyOrderAsync(
                orderId,
                action,
                quantity,
                stopPrice,
                priceDistance,
                limitPrice,
                ct).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response, HttpStatusCode.Accepted);
            return response;
        }

        /// <summary>Get live feed last quote</summary>
        /// <returns>Last quote for the specified financial instrument</returns>
        public WebCallResult<IEnumerable<ExanteTickShort>> GetFeedLastQuote(
            IEnumerable<string> symbolIds,
            ExanteQuoteLevel level = ExanteQuoteLevel.BestPrice,
            CancellationToken ct = default(CancellationToken)
        )
        {
            var response =
                _client.GetFeedLastQuoteAsync(symbolIds, level, ct).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response;
        }

        /// <summary>
        /// Get instruments by exchange
        /// </summary>
        /// <param name="exchangeId">Exchange ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Exchange financial instruments</returns>
        public WebCallResult<IEnumerable<ExanteSymbol>> GetSymbolsByExchange(
            string exchangeId,
            CancellationToken ct = default(CancellationToken)
        )
        {
            var response =
                _client.GetSymbolsByExchangeAsync(exchangeId, ct).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response;
        }

        /// <summary>Get transactions</summary>
        /// <returns>List of transactions with the specified filter</returns>
        public WebCallResult<IEnumerable<ExanteTransaction>> GetTransactions(
            Guid? transactionId = null,
            string? accountId = null,
            string? symbolId = null,
            string? asset = null,
            IEnumerable<ExanteTransactionType>? types = null,
            int? offset = null,
            int? limit = null,
            ExanteArrayOrderType orderType = ExanteArrayOrderType.Desc,
            DateTime? from = null,
            DateTime? to = null,
            Guid? orderId = null,
            int? orderPosition = null,
            CancellationToken ct = default(CancellationToken)
        )
        {
            var response =
                _client.GetTransactionsAsync(
                    transactionId,
                    accountId,
                    symbolId,
                    asset,
                    types,
                    offset,
                    limit,
                    orderType,
                    from,
                    to,
                    orderId,
                    orderPosition,
                    ct
                ).SynchronouslyAwaitTaskResult();
            CheckIfResponseOk(response);
            return response;
        }

        public void Dispose()
        {
            _client?.Dispose();
            StreamClient?.Dispose();
        }
    }
}
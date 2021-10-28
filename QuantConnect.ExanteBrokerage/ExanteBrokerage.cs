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
using System.Collections.Concurrent;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Brokerages;
using System.Collections.Generic;
using Exante.Net;
using Exante.Net.Objects;
using Exante.Net.Enums;
using QuantConnect.Util;
using System.Threading;
using NodaTime;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.TimeInForces;
using Log = QuantConnect.Logging.Log;
using QuantConnect.Data.Market;
using CryptoExchange.Net.Objects;
using Newtonsoft.Json.Linq;
using QuantConnect.Configuration;

namespace QuantConnect.ExanteBrokerage
{
    /// <summary>
    /// The Exante brokerage
    /// </summary>
    [BrokerageFactory(typeof(ExanteBrokerageFactory))]
    public partial class ExanteBrokerage : Brokerage, IDataQueueHandler
    {
        private bool _isConnected;
        private readonly string _accountId;
        private readonly IDataAggregator _aggregator;
        private readonly ConcurrentDictionary<Guid, Order> _orderMap = new();
        private readonly Dictionary<Symbol, DateTimeZone> _symbolExchangeTimeZones = new();
        private readonly EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;
        private readonly BrokerageConcurrentMessageHandler<ExanteOrder> _messageHandler;

        private readonly ConcurrentDictionary<string, (Symbol, ExanteStreamSubscription, ExanteStreamSubscription)>
            _subscribedTickers = new();

        /// <summary>
        /// Returns true if we're currently connected to the broker
        /// </summary>
        public override bool IsConnected => _isConnected;

        /// <summary>
        /// Returns the brokerage account's base currency
        /// </summary>
        public override string AccountBaseCurrency => Currencies.USD;

        /// <summary>
        /// Provides the mapping between Lean symbols and Exante symbols.
        /// </summary>
        public ExanteSymbolMapper SymbolMapper { get; }

        /// <summary>
        /// Instance of the wrapper class for a Exante REST API client
        /// </summary>
        public ExanteClientWrapper Client { get; }

        public static readonly HashSet<string> SupportedCryptoCurrencies = new()
        {
            "ETC", "MKR", "BNB", "NEO", "IOTA", "QTUM", "XMR", "EOS", "ETH", "XRP", "DCR",
            "XLM", "ZRX", "BTC", "XAI", "ZEC", "BAT", "BCH", "VEO", "DEFIX", "OMG", "LTC", "DASH"
        };

        /// <summary>
        /// Parameterless constructor for brokerage
        /// </summary>
        /// <remarks>This parameterless constructor is required for brokerages implementing <see cref="IDataQueueHandler"/></remarks>
        public ExanteBrokerage()
            : this(ExanteBrokerageFactory.CreateExanteClientOptions(), Config.Get("exante-account-id"),
                Composer.Instance.GetPart<IDataAggregator>())
        {
        }

        /// <summary>
        /// Creates a new ExanteBrokerage
        /// </summary>
        /// <param name="client">Exante client options to create REST API client instance</param>
        /// <param name="accountId">Exante account id</param>
        /// <param name="aggregator">consolidate ticks</param>
        public ExanteBrokerage(
            ExanteClientOptions client,
            string accountId,
            IDataAggregator aggregator) : base("ExanteBrokerage")
        {
            Client = new ExanteClientWrapper(client);
            _accountId = accountId;

            _aggregator = aggregator;
            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += Subscribe;
            _subscriptionManager.UnsubscribeImpl += (s, _) => Unsubscribe(s);

            SymbolMapper = new ExanteSymbolMapper(Client, SupportedCryptoCurrencies);
            _messageHandler = new BrokerageConcurrentMessageHandler<ExanteOrder>(OnUserMessage);

            Client.StreamClient.GetOrdersStreamAsync(exanteOrder => { _messageHandler.HandleNewMessage(exanteOrder); });
        }

        #region IDataQueueHandler

        /// <summary>
        /// Subscribe to the specified configuration
        /// </summary>
        /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
        /// <param name="newDataAvailableHandler">handler to be fired on new data available</param>
        /// <returns>The new enumerator for this subscription request</returns>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            if (!CanSubscribe(dataConfig.Symbol))
            {
                return Enumerable.Empty<BaseData>().GetEnumerator();
            }

            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            _subscriptionManager.Subscribe(dataConfig);

            return enumerator;
        }

        /// <summary>
        /// Removes the specified configuration
        /// </summary>
        /// <param name="dataConfig">Subscription config to be removed</param>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            _subscriptionManager.Unsubscribe(dataConfig);
            _aggregator.Remove(dataConfig);
        }

        /// <summary>
        /// Sets the job we're subscribing for
        /// </summary>
        /// <param name="job">Job we're subscribing for</param>
        public void SetJob(LiveNodePacket job)
        {
        }

        #endregion

        #region Brokerage

        /// <summary>
        /// Gets all open orders on the account.
        /// NOTE: The order objects returned do not have QC order IDs.
        /// </summary>
        /// <returns>The open orders returned from IB</returns>
        public override List<Order> GetOpenOrders()
        {
            _messageHandler.WithLockedStream(() =>
            {
                var orders = Client.GetActiveOrders().Data;
                foreach (var item in orders)
                {
                    Order order;
                    var symbol = Client.GetSymbol(item.OrderParameters.SymbolId);

                    var orderQuantity = item.OrderParameters.Side switch
                    {
                        ExanteOrderSide.Buy => Math.Abs(item.OrderParameters.Quantity),
                        ExanteOrderSide.Sell => -Math.Abs(item.OrderParameters.Quantity),
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    switch (item.OrderParameters.Type)
                    {
                        case ExanteOrderType.Market:
                            order = new MarketOrder();
                            break;
                        case ExanteOrderType.Limit:
                            if (item.OrderParameters.LimitPrice == null)
                            {
                                throw new ArgumentNullException(nameof(item.OrderParameters.LimitPrice));
                            }

                            order = new LimitOrder(
                                symbol: ConvertSymbol(symbol),
                                quantity: orderQuantity,
                                limitPrice: item.OrderParameters.LimitPrice.Value,
                                time: item.Date
                            );
                            break;
                        case ExanteOrderType.Stop:
                            if (item.OrderParameters.StopPrice == null)
                            {
                                throw new ArgumentNullException(nameof(item.OrderParameters.StopPrice));
                            }

                            order = new StopMarketOrder(
                                symbol: ConvertSymbol(symbol),
                                quantity: orderQuantity,
                                stopPrice: item.OrderParameters.StopPrice.Value,
                                time: item.Date
                            );
                            break;
                        case ExanteOrderType.StopLimit:
                            if (item.OrderParameters.LimitPrice == null)
                            {
                                throw new ArgumentNullException(nameof(item.OrderParameters.LimitPrice));
                            }

                            if (item.OrderParameters.StopPrice == null)
                            {
                                throw new ArgumentNullException(nameof(item.OrderParameters.StopPrice));
                            }

                            order = new StopLimitOrder(
                                symbol: ConvertSymbol(symbol),
                                quantity: orderQuantity,
                                stopPrice: item.OrderParameters.StopPrice.Value,
                                limitPrice: item.OrderParameters.LimitPrice.Value,
                                time: item.Date
                            );
                            break;

                        default:
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1,
                                $"ExanteBrokerage.GetOpenOrders: Unsupported order type returned from brokerage: {item.OrderParameters.Type}"));
                            continue;
                    }

                    order.BrokerId.Add(item.OrderId.ToString());
                    order.Status = ConvertOrderStatus(item.OrderState.Status);
                    _orderMap[item.OrderId] = order;
                }
            });

            return _orderMap.Values.ToList();
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            var accountSummary = Client.GetAccountSummary(_accountId, AccountBaseCurrency);
            var positions = accountSummary.Positions
                .Where(position => position.Quantity != 0)
                .Select(ConvertHolding)
                .ToList();
            return positions;
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<CashAmount> GetCashBalance()
        {
            var accountSummary = Client.GetAccountSummary(_accountId, AccountBaseCurrency);
            var cashAmounts =
                from currencyData in accountSummary.Currencies
                select new CashAmount(currencyData.Value, currencyData.Currency);
            return cashAmounts.ToList();
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            var orderSide = ConvertOrderDirection(order.Direction);

            DateTime? goodTilDateTimeInForceExpiration = null;
            ExanteOrderDuration orderDuration;
            switch (order.TimeInForce)
            {
                case GoodTilCanceledTimeInForce _:
                    // NOTE:
                    // GTC order duration is not available for market orders due to its specifics.
                    //
                    // GTC duration implies an order to stay active until the trade is executed or
                    // canceled by investor. However, with market order type, the order gets executed
                    // immediately at the best available current price. GTC duration can be used with
                    // Limit or Stop (Stop-Limit) order types.
                    orderDuration = SymbolMapper.GetExchange(order.Symbol) switch
                    {
                        ExanteMarket.USD => ExanteOrderDuration.GoodTillCancel,
                        ExanteMarket.ARCA => ExanteOrderDuration.GoodTillCancel,
                        ExanteMarket.NASDAQ => ExanteOrderDuration.GoodTillCancel,
                        ExanteMarket.AMEX => ExanteOrderDuration.GoodTillCancel,
                        _ => ExanteOrderDuration.Day
                    };

                    break;
                case DayTimeInForce _:
                    orderDuration = ExanteOrderDuration.Day;
                    break;
                case GoodTilDateTimeInForce goodTilDateTimeInForce:
                    orderDuration = ExanteOrderDuration.GoodTillTime;
                    goodTilDateTimeInForceExpiration = goodTilDateTimeInForce.Expiry;
                    break;
                default:
                    throw new NotSupportedException(
                        $"ExanteBrokerage.ConvertOrderDuration: Unsupported order duration: {order.TimeInForce}");
            }

            var quantity = Math.Abs(order.Quantity);

            var orderPlacementSuccess = false;

            _messageHandler.WithLockedStream(() =>
            {
                WebCallResult<IEnumerable<ExanteOrder>> orderPlacement;
                switch (order.Type)
                {
                    case OrderType.Market:
                        orderPlacement = Client.PlaceOrder(
                            _accountId,
                            SymbolMapper.GetBrokerageSymbol(order.Symbol),
                            ExanteOrderType.Market,
                            orderSide,
                            quantity,
                            orderDuration,
                            gttExpiration: goodTilDateTimeInForceExpiration
                        );
                        break;

                    case OrderType.Limit:
                        var limitOrder = (LimitOrder)order;
                        orderPlacement = Client.PlaceOrder(
                            _accountId,
                            SymbolMapper.GetBrokerageSymbol(order.Symbol),
                            ExanteOrderType.Limit,
                            orderSide,
                            quantity,
                            orderDuration,
                            limitPrice: limitOrder.LimitPrice,
                            gttExpiration: goodTilDateTimeInForceExpiration
                        );
                        break;

                    case OrderType.StopMarket:
                        var stopMarketOrder = (StopMarketOrder)order;
                        orderPlacement = Client.PlaceOrder(
                            _accountId,
                            SymbolMapper.GetBrokerageSymbol(order.Symbol),
                            ExanteOrderType.Stop,
                            orderSide,
                            quantity,
                            orderDuration,
                            stopPrice: stopMarketOrder.StopPrice,
                            gttExpiration: goodTilDateTimeInForceExpiration
                        );
                        break;

                    case OrderType.StopLimit:
                        var stopLimitOrder = (StopLimitOrder)order;
                        orderPlacement = Client.PlaceOrder(
                            _accountId,
                            SymbolMapper.GetBrokerageSymbol(order.Symbol),
                            ExanteOrderType.Stop,
                            orderSide,
                            quantity,
                            orderDuration,
                            limitPrice: stopLimitOrder.LimitPrice,
                            stopPrice: stopLimitOrder.StopPrice,
                            gttExpiration: goodTilDateTimeInForceExpiration
                        );
                        break;

                    default:
                        throw new NotSupportedException(
                            $"ExanteBrokerage.ConvertOrderType: Unsupported order type: {order.Type}");
                }

                foreach (var exanteOrder in orderPlacement.Data)
                {
                    _orderMap[exanteOrder.OrderId] = order;
                    order.BrokerId.Add(exanteOrder.OrderId.ToString());
                }

                if (!orderPlacement.Success)
                {
                    var errorsJson =
                        JArray.Parse(orderPlacement.Error?.Message ?? throw new InvalidOperationException());
                    var errorMsg = string.Join(",", errorsJson.Select(x => x["message"]));
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, errorMsg));
                }

                orderPlacementSuccess = orderPlacement.Success;
            });

            return orderPlacementSuccess;
        }

        /// <summary>
        /// Updates the order with the same id
        /// </summary>
        /// <param name="order">The new order information</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        public override bool UpdateOrder(Order order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            var updateResult = true;
            foreach (var bi in order.BrokerId.Skip(1))
            {
                var d = Client.ModifyOrder(Guid.Parse(bi), ExanteOrderAction.Cancel);
                updateResult = updateResult && d.Success;
            }

            _messageHandler.WithLockedStream(() =>
            {
                WebCallResult<ExanteOrder> exanteOrder;
                switch (order.Type)
                {
                    case OrderType.Market:
                        exanteOrder = Client.ModifyOrder(
                            Guid.Parse(order.BrokerId.First()),
                            ExanteOrderAction.Replace,
                            order.Quantity);
                        break;

                    case OrderType.Limit:
                        var limitOrder = (LimitOrder)order;
                        exanteOrder = Client.ModifyOrder(
                            Guid.Parse(order.BrokerId.First()),
                            ExanteOrderAction.Replace,
                            order.Quantity,
                            limitPrice: limitOrder.LimitPrice);
                        break;

                    case OrderType.StopMarket:
                        var stopMarketOrder = (StopMarketOrder)order;
                        exanteOrder = Client.ModifyOrder(
                            Guid.Parse(order.BrokerId.First()),
                            ExanteOrderAction.Replace,
                            order.Quantity,
                            stopPrice: stopMarketOrder.StopPrice);
                        break;

                    case OrderType.StopLimit:
                        var stopLimitOrder = (StopLimitOrder)order;
                        exanteOrder = Client.ModifyOrder(
                            Guid.Parse(order.BrokerId.First()),
                            ExanteOrderAction.Replace,
                            order.Quantity,
                            limitPrice: stopLimitOrder.LimitPrice,
                            stopPrice: stopLimitOrder.StopPrice);
                        break;

                    default:
                        throw new NotSupportedException(
                            $"ExanteBrokerage.UpdateOrder: Unsupported order type: {order.Type}");
                }

                _orderMap[exanteOrder.Data.OrderId] = order;

                updateResult = updateResult && exanteOrder.Success;
            });
            return updateResult;
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            var cancelResult = true;
            _messageHandler.WithLockedStream(() =>
            {
                foreach (var bi in order.BrokerId)
                {
                    var orderBrokerGuid = Guid.Parse(bi);
                    var exanteOrder = Client.ModifyOrder(orderBrokerGuid, ExanteOrderAction.Cancel);
                    _orderMap.TryRemove(orderBrokerGuid, out order);
                    cancelResult = cancelResult && exanteOrder.Success;

                    if (exanteOrder.Success)
                    {
                        var orderEvent = new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                        {
                            Status = OrderStatus.Canceled,
                        };
                        OnOrderEvent(orderEvent);
                    }
                }
            });

            return cancelResult;
        }

        /// <summary>
        /// Connects the client to the broker's remote servers
        /// </summary>
        public override void Connect()
        {
            _isConnected = true;
        }

        /// <summary>
        /// Disconnects the client from the broker's remote servers
        /// </summary>
        public override void Disconnect()
        {
            _isConnected = false;
        }

        #endregion

        #region IDataQueueUniverseProvider

        /// <summary>
        /// Adds the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be added keyed by SecurityType</param>
        /// <param name="tickType">Type of tick data</param>
        private bool Subscribe(IEnumerable<Symbol> symbols, TickType tickType)
        {
            void OnNewQuote(ExanteTickShort tickShort)
            {
                var tick = CreateTick(tickShort);
                if (tick != null)
                {
                    _aggregator.Update(tick);
                }
            }

            void OnNewTrade(ExanteFeedTrade feedTrade)
            {
                var tick = CreateTick(feedTrade);
                if (tick != null)
                {
                    _aggregator.Update(tick);
                }
            }

            foreach (var symbol in symbols)
            {
                if (!CanSubscribe(symbol))
                {
                    var ticker = SymbolMapper.GetBrokerageSymbol(symbol);
                    if (!_subscribedTickers.ContainsKey(ticker))
                    {
                        var success = true;

                        var feedQuoteStream =
                            Client.StreamClient.GetFeedQuoteStreamAsync(new[] { ticker }, OnNewQuote,
                                level: ExanteQuoteLevel.BestPrice).SynchronouslyAwaitTaskResult();
                        if (!feedQuoteStream.Success)
                        {
                            Log.Error(
                                $"Exante.StreamClient.GetFeedQuoteStreamAsync({ticker}): " +
                                $"Error: {feedQuoteStream.Error}"
                            );
                            success = false;
                        }

                        var feedTradesStream =
                            Client.StreamClient.GetFeedTradesStreamAsync(new[] { ticker }, OnNewTrade)
                                .SynchronouslyAwaitTaskResult();
                        if (!feedTradesStream.Success)
                        {
                            Log.Error(
                                $"Exante.StreamClient.GetFeedTradesStreamAsync({ticker}): " +
                                $"Error: {feedTradesStream.Error}"
                            );
                            success = false;
                        }

                        if (success)
                        {
                            _subscribedTickers.TryAdd(ticker, (symbol, feedQuoteStream.Data, feedTradesStream.Data));
                        }
                        else
                        {
                            new[] { feedQuoteStream, feedTradesStream }
                                .Where(stream => stream is { Success: true })
                                .DoForEach(stream => Client.StreamClient.StopStream(stream.Data));
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Removes the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be removed keyed by SecurityType</param>
        private bool Unsubscribe(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (!CanSubscribe(symbol))
                {
                    var ticker = SymbolMapper.GetBrokerageSymbol(symbol);
                    if (_subscribedTickers.ContainsKey(ticker))
                    {
                        _subscribedTickers.TryRemove(ticker,
                            out (Symbol symbol, ExanteStreamSubscription stream1, ExanteStreamSubscription stream2)
                            streams);
                        Client.StreamClient.StopStream(streams.stream1);
                        Client.StreamClient.StopStream(streams.stream2);
                    }
                }
            }

            return true;
        }

        private static bool CanSubscribe(Symbol symbol)
        {
            if (!symbol.IsCanonical())
            {
                return false;
            }

            if (symbol.Value.IndexOfInvariant("universe", true) != -1)
            {
                return false;
            }

            // ignore unsupported security types
            if (symbol.ID.SecurityType is not
                (SecurityType.Forex or SecurityType.Equity or SecurityType.Future or SecurityType.Option or
                SecurityType.Cfd or SecurityType.Index or SecurityType.Crypto))
            {
                return false;
            }

            // ignore universe symbols
            return !symbol.Value.Contains("-UNIVERSE-");
        }

        /// <summary>
        /// Create a tick from the Exante feed stream data
        /// </summary>
        /// <param name="exanteFeedTrade">Exante feed stream data object</param>
        /// <returns>LEAN Tick object</returns>
        private Tick CreateTick(ExanteFeedTrade exanteFeedTrade)
        {
            if (exanteFeedTrade.Size == decimal.Zero)
            {
                return null;
            }

            var symbolId = exanteFeedTrade.SymbolId;
            if (!_subscribedTickers.TryGetValue(symbolId, out var item))
            {
                return null; // Not subscribed to this symbol.
            }

            var (symbol, _, _) = item;

            // Convert the timestamp to exchange timezone and pass into algorithm
            var time = GetRealTimeTickTime(exanteFeedTrade.Date, symbol);

            var instrument = Client.GetSymbol(symbolId);

            var size = exanteFeedTrade.Size ?? 0m;
            var price = exanteFeedTrade.Price ?? 0m;
            var tick = new Tick(time, symbol, "", instrument.Exchange, size, price);
            return tick;
        }

        /// <summary>
        /// Returns a timestamp for a tick converted to the exchange time zone
        /// </summary>
        private DateTime GetRealTimeTickTime(DateTime time, Symbol symbol)
        {
            DateTimeZone exchangeTimeZone;
            if (!_symbolExchangeTimeZones.TryGetValue(symbol, out exchangeTimeZone))
            {
                // read the exchange time zone from market-hours-database
                exchangeTimeZone = MarketHoursDatabase.FromDataFolder()
                    .GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType).TimeZone;
                _symbolExchangeTimeZones.Add(symbol, exchangeTimeZone);
            }

            return time.ConvertFromUtc(exchangeTimeZone);
        }

        /// <summary>
        /// Create a tick from the Exante tick shorts stream data
        /// </summary>
        /// <param name="exanteTickShort">Exante tick short stream data object</param>
        /// <returns>LEAN Tick object</returns>
        private Tick CreateTick(ExanteTickShort exanteTickShort)
        {
            if (!_subscribedTickers.TryGetValue(exanteTickShort.SymbolId, out var item))
            {
                // Not subscribed to this symbol.
                return null;
            }

            var (symbol, _, _) = item;

            var time = GetRealTimeTickTime(exanteTickShort.Date, symbol);
            var bids = exanteTickShort.Bid.ToList();
            var asks = exanteTickShort.Ask.ToList();
            return new Tick(time, symbol, "", "",
                bids.IsNullOrEmpty() ? decimal.Zero : bids[0].Size,
                bids.IsNullOrEmpty() ? decimal.Zero : bids[0].Price,
                asks.IsNullOrEmpty() ? decimal.Zero : asks[0].Size,
                asks.IsNullOrEmpty() ? decimal.Zero : asks[0].Price);
        }

        #endregion

        private void OnUserMessage(ExanteOrder exanteOrder)
        {
            Order order;
            if (!_orderMap.TryGetValue(exanteOrder.OrderId, out order))
            {
                return;
            }

            Thread.Sleep(1_000); // Need to wait for `Client.GetTransactions(...)`

            var transactions = Client.GetTransactions(
                orderId: exanteOrder.OrderId, types: new[] { ExanteTransactionType.Commission }
            );

            var commission = transactions.Data.FirstOrDefault();
            var fee = commission == null
                ? OrderFee.Zero
                : new OrderFee(new CashAmount(commission.Amount, commission.Asset));

            var orderEvent = new OrderEvent(order, DateTime.UtcNow, fee)
            {
                Status = ConvertOrderStatus(exanteOrder.OrderState.Status),
            };
            if (exanteOrder.OrderState.Status == ExanteOrderStatus.Filled)
            {
                orderEvent.FillQuantity = exanteOrder.OrderParameters.Quantity;
                _messageHandler.WithLockedStream(() => { _orderMap.TryRemove(exanteOrder.OrderId, out _); });
            }

            OnOrderEvent(orderEvent);
        }

        private IEnumerable<BaseData> GetCandlesHistory(Data.HistoryRequest request)
        {
            var symbol = SymbolMapper.GetBrokerageSymbol(request.Symbol);

            var exanteTickType = request.TickType switch
            {
                TickType.Quote => ExanteTickType.Quotes,
                TickType.Trade => ExanteTickType.Trades,
                TickType.OpenInterest => throw new ArgumentException(),
                _ => throw new ArgumentOutOfRangeException()
            };

            var exanteTimeframe = request.Resolution switch
            {
                Resolution.Tick => throw new ArgumentException(),
                Resolution.Second => throw new ArgumentException(),
                Resolution.Minute => ExanteCandleTimeframe.Minute1,
                Resolution.Hour => ExanteCandleTimeframe.Hour1,
                Resolution.Daily => ExanteCandleTimeframe.Day1,
                _ => throw new ArgumentOutOfRangeException()
            };

            var history = Client.GetCandles(
                symbol,
                exanteTimeframe,
                from: request.StartTimeUtc,
                to: request.EndTimeUtc,
                tickType: exanteTickType
            ).Data.ToList();

            var period = request.Resolution.ToTimeSpan();

            foreach (var kline in history)
            {
                yield return new TradeBar()
                {
                    Time = kline.Date,
                    Symbol = request.Symbol,
                    Low = kline.Low,
                    High = kline.High,
                    Open = kline.Open,
                    Close = kline.Close,
                    Volume = kline.Volume ?? 0m,
                    Value = kline.Close,
                    DataType = MarketDataType.TradeBar,
                    Period = period,
                };
            }
        }

        /// <summary>
        /// Gets the history for the requested security
        /// </summary>
        /// <param name="request">The historical data request</param>
        /// <returns>An enumerable of bars covering the span specified in the request</returns>
        public override IEnumerable<BaseData> GetHistory(Data.HistoryRequest request)
        {
            return request.Resolution switch
            {
                Resolution.Tick => throw new ArgumentException(),
                Resolution.Second => throw new ArgumentException(),
                Resolution.Minute or Resolution.Hour or Resolution.Daily => GetCandlesHistory(request),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}
﻿using System.Threading.Tasks;
using Clockwise.Tests;
using Pocket;
using StackExchange.Redis;

namespace Clockwise.Redis.Tests
{
    public class RedisCircuitBreakerBrokerTests : CircuitBreakerBrokerTests
    {
        protected override async Task<ICircuitBreakerBroker> CreateBroker(string circuitBreakerId)
        {
            var db = 1; // QUESTION: (CreateBroker) why?
            var cb01 = new CircuitBreakerBroker("127.0.0.1", db);

            AddToDisposable(cb01);

            AddToDisposable(Disposable.Create(() =>
            {
                var connection = ConnectionMultiplexer.Connect("127.0.0.1");
                connection.GetDatabase().Execute("FLUSHALL");
                cb01.Dispose();
                connection.Dispose();
            }));

            await cb01.InitializeAsync(circuitBreakerId);

            return cb01;
        }
    }
}

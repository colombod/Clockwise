﻿using System;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Extensions;
using Pocket;
using Xunit;
using Xunit.Abstractions;

namespace Clockwise.Tests
{
    public abstract class BudgetExtensionsTests : IDisposable
    {
        private readonly IClock clock;

        private readonly CompositeDisposable disposables = new CompositeDisposable();

        protected BudgetExtensionsTests(IClock clock, ITestOutputHelper output)
        {
            this.clock = clock;
            disposables.Add(LogEvents.Subscribe(e => output.WriteLine(e.ToLogString())));

            if (clock is IDisposable disposable)
            {
                disposables.Add(disposable);
            }
        }

        public void Dispose() => disposables.Dispose();

        [Fact]
        public void CancelIfExceeds_throws_if_task_time_exceeds_budget()
        {
            clock.Schedule(c => {  }, 500.Milliseconds());

            var budget = new TimeBudget(1.Seconds(), clock);

            Func<Task> timeout = () => clock.Wait(10.Seconds())
                                            .CancelIfExceeds(budget);

            timeout.Should().Throw<BudgetExceededException>();
        }

        [Fact]
        public async Task CancelIfExceeds_can_perform_an_action_rather_than_throw_if_cancellation_occurs()
        {
            var actionPerformed = false;
            var budget = new TimeBudget(2.Seconds(), clock);

            clock.Schedule(async c => await Task.Delay(10));

            await clock.Wait(10.Seconds())
                       .CancelIfExceeds(budget,
                                        ifCancelled: () => actionPerformed = true);

            actionPerformed.Should().BeTrue();
        }

        [Fact]
        public void CancelIfExceeds_T_throws_if_task_time_exceeds_budget()
        {
            var budget = new TimeBudget(4.Seconds(), clock);

            Func<Task> timeout = async () => await Task.Run(async () =>
            {
                await clock.Wait(10.Seconds());

                return "not cancelled";
            }).CancelIfExceeds(budget);

            timeout.Should().Throw<BudgetExceededException>();
        }

        [Fact]
        public async Task CancelIfExceeds_T_returns_the_expected_value_if_task_time_does_not_exceed_budget()
        {
            var budget = new TimeBudget(1.Seconds(), clock);

            var result = await Task.Run(async () => "not cancelled")
                                   .CancelIfExceeds(budget, ifCancelled: () => "cancelled");

            result.Should().Be("not cancelled");
        }
    }
}

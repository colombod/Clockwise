﻿using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions.Extensions;
using Pocket;
using Xunit;
using Xunit.Abstractions;

namespace Clockwise.Tests
{
    public class VirtualClockTests : IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();

        public VirtualClockTests(ITestOutputHelper output)
        {
            disposables.Add(LogEvents.Subscribe(e => output.WriteLine(e.ToLogString())));
        }

        public void Dispose() => disposables.Dispose();

        [Fact]
        public void Clock_can_be_overridden_using_VirtualClock()
        {
            var virtualTime = DateTimeOffset.Parse("2027-06-02 12:23am", CultureInfo.InvariantCulture);

            using (VirtualClock.Start(virtualTime))
            {
                Clock.Now().Should().Be(virtualTime);
            }
        }

        [Fact]
        public async Task Instantiating_a_VirtualClock_freezes_time()
        {
            var virtualTime = DateTimeOffset.Parse("2027-06-02 12:23am", CultureInfo.InvariantCulture);

            using (VirtualClock.Start(virtualTime))
            {
                await Task.Delay(200);

                Clock.Now().Should().Be(virtualTime);
            }
        }

        [Fact]
        public async Task The_clock_can_be_advanced_to_a_specific_time()
        {
            var initialTime = DateTimeOffset.Parse("2017-01-15 1:00pm", CultureInfo.InvariantCulture);
            var newTime = DateTimeOffset.Parse("2018-06-16 1:50pm", CultureInfo.InvariantCulture);

            using (var clock = VirtualClock.Start(initialTime))
            {
                await clock.AdvanceTo(newTime);

                Clock.Now().Should().Be(newTime);
            }
        }

        [Fact]
        public async Task The_clock_can_be_advanced_by_a_specific_timespan()
        {
            var initialTime = DateTimeOffset.Parse("2017-6-15 1:00pm", CultureInfo.InvariantCulture);

            using (var clock = VirtualClock.Start(initialTime))
            {
                await clock.AdvanceBy(12.Minutes());

                Clock.Now().Should().Be(initialTime + 12.Minutes());
            }
        }

        [Fact]
        public void The_virtual_clock_cannot_be_moved_backwards_using_AdvanceBy()
        {
            using (var clock = VirtualClock.Start())
            {
                Func<Task> moveBackwards = () => clock.AdvanceBy(-1.Minutes());

                moveBackwards.Should().Throw<ArgumentException>()
                             .Which
                             .Message
                             .Should()
                             .Be("The clock cannot be moved backward in time.");
            }
        }

        [Fact]
        public void The_virtual_clock_cannot_be_moved_backwards_using_AdvanceTo()
        {
            using (var clock = VirtualClock.Start())
            {
                Func<Task> moveBackwards = () => clock.AdvanceTo(clock.Now().Subtract(1.Minutes()));
                moveBackwards.Should().Throw<ArgumentException>()
                             .Which
                             .Message
                             .Should()
                             .Be("The clock cannot be moved backward in time.");
            }
        }

        [Fact]
        public async Task Schedule_can_specify_actions_that_are_invoked_when_the_virtual_clock_is_advanced()
        {
            var actions = new List<string>();

            using (var clock = VirtualClock.Start())
            {
                clock.Schedule(_ => actions.Add("one"), 2.Seconds());
                clock.Schedule(_ => actions.Add("two"), 1.Hours());

                await clock.AdvanceBy(3.Seconds());

                actions.Should().BeEquivalentTo("one");

                await clock.AdvanceBy(3.Hours());

                actions.Should().BeEquivalentTo("one", "two");
            }
        }

        [Fact]
        public async Task A_schedule_action_can_read_the_current_time_from_the_schedule()
        {
            var events = new List<DateTimeOffset>();

            var startTime = DateTimeOffset.Parse("2018-04-01 1:00pm +00:00", CultureInfo.InvariantCulture);

            using (var clock = VirtualClock.Start(startTime))
            {
                clock.Schedule(c => events.Add(c.Now()), 2.Seconds());
                clock.Schedule(c => events.Add(c.Now()), 3.Seconds());
                clock.Schedule(c => events.Add(c.Now()), 1.Hours());

                await clock.AdvanceBy(20.Days());
            }

            events.Should().BeEquivalentTo(new[]
            {
                startTime.Add(2.Seconds()),
                startTime.Add(3.Seconds()),
                startTime.Add(1.Hours())
            });
        }

        [Fact]
        public async Task A_scheduled_action_can_schedule_additional_actions()
        {
            DateTimeOffset secondActionExecutedAt;
            var startTime = DateTimeOffset.Parse("2018-01-01 1:00pm +00:00", CultureInfo.InvariantCulture);

            using (var clock = VirtualClock.Start(startTime))
            {
                clock.Schedule(
                    c => c.Schedule(
                        c2 => secondActionExecutedAt = c2.Now(),
                        2.Minutes()),
                    1.Minutes());

                await clock.AdvanceBy(5.Minutes());
            }

            secondActionExecutedAt
                .Should()
                .Be(startTime.Add(3.Minutes()));
        }

        [Fact]
        public async Task A_schedule_action_can_be_scheduled_for_as_soon_as_possible_by_not_specifying_a_due_time()
        {
            var events = new List<(DateTimeOffset, string)>();

            var startTime = DateTimeOffset.Parse("2018-01-01 1:00pm +00:00", CultureInfo.InvariantCulture);

            using (var clock = VirtualClock.Start(startTime))
            {
                clock.Schedule(c => events.Add((c.Now(), "first")));
                clock.Schedule(c => events.Add((c.Now(), "second")));
                clock.Schedule(c => events.Add((c.Now(), "third")));

                await clock.AdvanceBy(1.Ticks());

                events.Should().BeEquivalentTo(new[]
                {
                    (startTime, "first"),
                    (startTime, "second"),
                    (startTime, "third")
                });
            }
        }

        [Fact]
        public async Task When_the_clock_is_advanced_then_scheduled_async_actions_are_awaited()
        {
            var done = false;

            using (var clock = VirtualClock.Start())
            {
                clock.Schedule(async s =>
                {
                    await Task.Delay(20);

                    done = true;
                });

                await clock.AdvanceBy(5.Seconds());

                done.Should().BeTrue();
            }
        }

        [Fact]
        public void When_one_virtual_clock_is_active_another_cannot_be_started()
        {
            using (VirtualClock.Start())
            {
                Action startAnother = () => VirtualClock.Start();

                startAnother.Should().Throw<InvalidOperationException>()
                            .Which
                            .Message
                            .Should()
                            .Be("A virtual clock cannot be started while another is still active in the current context.");
            }
        }

        [Fact]
        public void When_actions_are_scheduled_in_the_future_then_TimeUntilNextActionIsDue_returns_the_expected_time()
        {
            using (var clock = VirtualClock.Start())
            {
                clock.Schedule(_ =>
                {
                }, 1.Minutes());

                clock.Schedule(_ =>
                {
                }, 1.Seconds());

                clock.Schedule(_ =>
                {
                }, 1.Hours());

                clock.TimeUntilNextActionIsDue.Should().Be(1.Seconds());
            }
        }

        [Fact]
        public async Task
            When_actions_were_scheduled_in_the_past_and_are_scheduled_in_the_future_then_TimeUntilNextActionIsDue_returns_the_time_until_the_next_future_scheduled_action()
        {
            using (var clock = VirtualClock.Start())
            {
                clock.Schedule(_ =>
                {
                }, 1.Minutes());

                clock.Schedule(_ =>
                {
                }, 1.Seconds());

                clock.Schedule(_ =>
                {
                }, 1.Hours());

                await clock.AdvanceBy(1.Seconds());

                clock.TimeUntilNextActionIsDue.Should().Be(1.Minutes() - 1.Seconds());
            }
        }

        [Fact]
        public void When_no_actions_have_been_scheduled_then_TimeUntilNextActionIsDue_returns_null()
        {
            using (var clock = VirtualClock.Start())
            {
                clock.TimeUntilNextActionIsDue.Should().BeNull();
            }
        }

        [Fact]
        public async Task Clock_Now_is_correct_when_clock_is_advanced_from_within_scheduled_actions()
        {
            var startTime = DateTimeOffset.Parse("2017-01-01 12:00am +00:00", CultureInfo.InvariantCulture);

            using (var clock = VirtualClock.Start(startTime))
            {
                clock.Schedule(async c =>
                {
                    await c.Wait(1.Minutes());
                    c.Now().Should().Be(startTime + 2.Minutes());
                }, 1.Minutes());

                clock.Schedule(async c =>
                {
                    await c.Wait(2.Minutes());
                    c.Now().Should().Be(startTime + 4.Minutes());
                }, 2.Minutes());

                clock.Schedule(async c =>
                {
                    await c.Wait(3.Minutes());
                    c.Now().Should().Be(startTime + 6.Minutes());
                }, 3.Minutes());

                await clock.Wait(90.Minutes());
                clock.Now().Should().Be(startTime + 90.Minutes());
            }
        }

        [Fact]
        public void VirtualClock_logs_the_time_on_start()
        {
            var startTime = DateTimeOffset.Parse("2017-09-02 12:03:04pm", CultureInfo.InvariantCulture);
            var log = new List<string>();

            using (LogEvents.Subscribe(e => log.Add(e.ToLogString())))
            using (VirtualClock.Start(startTime))
            {
                log.Single().Should().Match($"*[Clockwise.VirtualClock]*Starting at {startTime}*");
            }
        }

        [Fact]
        public async Task When_advanced_it_logs_the_time_and_ticks_at_start_and_stop_of_operation()
        {
            var startTime = DateTimeOffset.Parse("2017-09-02 12:03:04pm", CultureInfo.InvariantCulture);
            var log = new List<string>();

            using (LogEvents.Subscribe(e => log.Add(e.ToLogString()), new[] { typeof(VirtualClock).Assembly }))
            using (var clock = VirtualClock.Start(startTime))
            {
                await clock.AdvanceBy(1.Milliseconds());

                log[1].Should().Match($"*[Clockwise.VirtualClock] [AdvanceTo]  ▶ Advancing from {startTime} ({startTime.Ticks}) to {clock.Now()} ({clock.Now().Ticks})*");
                log[2].Should().Match($"*[Clockwise.VirtualClock] [AdvanceTo]  ⏹ -> ✔ (*ms)  +[ (nowAt, {Clock.Now()}) ]*");
            }
        }

        [Fact]
        public void Recording_of_budget_entries_can_be_observed()
        {
            VirtualClock receivedClock = null;
            Budget receivedBudget = null;
            BudgetEntry receivedBudgetEntry = null;

            using (var clock = VirtualClock.Start())
            {
                var budget = new TimeBudget(1.Minutes());

                clock.OnBudgetEntryRecorded((c, b, e) =>
                {
                    receivedClock = c;
                    receivedBudget = b;
                    receivedBudgetEntry = e;
                });

                budget.RecordEntry();

                receivedClock.Should().BeSameAs(clock);
                receivedBudget.Should().BeSameAs(budget);
                receivedBudgetEntry.Name.Should().Be(nameof(Recording_of_budget_entries_can_be_observed));
            }
        }

        [Fact]
        public void OnBudgetEntryRecorded_stops_receiving_notifications_after_disposal_of_returned_disposable()
        {
            var budgetEntryNotificationCount = 0;

            using (var clock = VirtualClock.Start())
            {
                var budget = new TimeBudget(1.Minutes());
                using (clock.OnBudgetEntryRecorded((c, b, e) => budgetEntryNotificationCount++))
                {
                    budget.RecordEntry();
                }

                budget.RecordEntry();

                budgetEntryNotificationCount.Should().Be(1);
            }
        }
    }
}

using SimpleEventViewer_WinUI.Models;
using SimpleEventViewer_WinUI.Services;

namespace SimpleEventViewer.Tests;

public class FilterEventsTests
{
    private static EventLogEntry MakeEntry(
        int id = 1,
        LogLevel level = LogLevel.Information,
        string provider = "TestProvider",
        DateTime? time = null,
        string user = "DOMAIN\\user",
        int processId = 1234,
        string computer = "TESTPC",
        string channel = "Application",
        string message = "")
    {
        return new EventLogEntry
        {
            Id = id,
            Level = level,
            TimeCreated = time ?? new DateTime(2026, 1, 15, 10, 0, 0),
            ProviderName = provider,
            Username = user,
            ProcessId = processId,
            Computer = computer,
            Channel = channel,
            Message = message
        };
    }

    [Fact]
    public void NoFilters_ReturnsAllEvents()
    {
        var events = new[] { MakeEntry(1), MakeEntry(2), MakeEntry(3) };
        var result = EventFilter.Apply(events);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FilterBySource_ReturnsMatching()
    {
        var events = new[]
        {
            MakeEntry(1, provider: "App"),
            MakeEntry(2, provider: "Service"),
            MakeEntry(3, provider: "App")
        };
        var result = EventFilter.Apply(events, source: "App");
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal("App", e.ProviderName));
    }

    [Fact]
    public void FilterByLevel_ReturnsMatching()
    {
        var events = new[]
        {
            MakeEntry(1, level: LogLevel.Error),
            MakeEntry(2, level: LogLevel.Information),
            MakeEntry(3, level: LogLevel.Error)
        };
        var result = EventFilter.Apply(events, level: LogLevel.Error);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterByTimeRange_ExcludesOutsideRange()
    {
        var events = new[]
        {
            MakeEntry(1, time: new DateTime(2026, 1, 1)),
            MakeEntry(2, time: new DateTime(2026, 1, 15)),
            MakeEntry(3, time: new DateTime(2026, 1, 31))
        };
        var result = EventFilter.Apply(events,
            startTime: new DateTime(2026, 1, 10),
            endTime: new DateTime(2026, 1, 20));
        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public void FilterByMessage_IsCaseInsensitive()
    {
        var events = new[]
        {
            MakeEntry(1, message: "Connection failed"),
            MakeEntry(2, message: "FAILED to start"),
            MakeEntry(3, message: "OK")
        };
        var result = EventFilter.Apply(events, searchTerms: "failed");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterByUser_RequiresExactMatch()
    {
        var events = new[]
        {
            MakeEntry(1, user: "CORP\\alice"),
            MakeEntry(2, user: "CORP\\bob"),
            MakeEntry(3, user: "CORP\\alice")
        };
        var result = EventFilter.Apply(events, username: "CORP\\alice");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterByProcessId_MatchesByStringForm()
    {
        var events = new[]
        {
            MakeEntry(1, processId: 100),
            MakeEntry(2, processId: 200),
            MakeEntry(3, processId: 100)
        };
        var result = EventFilter.Apply(events, processId: "100");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterByComputer_RequiresExactMatch()
    {
        var events = new[]
        {
            MakeEntry(1, computer: "PC-A"),
            MakeEntry(2, computer: "PC-B")
        };
        var result = EventFilter.Apply(events, computer: "PC-A");
        Assert.Single(result);
    }

    [Fact]
    public void FilterByChannel_RequiresExactMatch()
    {
        var events = new[]
        {
            MakeEntry(1, channel: "Application"),
            MakeEntry(2, channel: "Security"),
            MakeEntry(3, channel: "System")
        };
        var result = EventFilter.Apply(events, channel: "Security");
        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public void EmptyStringFilter_TreatedAsNoFilter()
    {
        var events = new[] { MakeEntry(1, provider: "A"), MakeEntry(2, provider: "B") };
        var result = EventFilter.Apply(events, source: "");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MultipleFilters_AreCombinedWithAnd()
    {
        var events = new[]
        {
            MakeEntry(1, provider: "App", level: LogLevel.Error, user: "alice"),
            MakeEntry(2, provider: "App", level: LogLevel.Information, user: "alice"),
            MakeEntry(3, provider: "Service", level: LogLevel.Error, user: "alice")
        };
        var result = EventFilter.Apply(events,
            source: "App",
            level: LogLevel.Error,
            username: "alice");
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public void Result_IsSortedDescendingByTime()
    {
        var events = new[]
        {
            MakeEntry(1, time: new DateTime(2026, 1, 10)),
            MakeEntry(2, time: new DateTime(2026, 1, 20)),
            MakeEntry(3, time: new DateTime(2026, 1, 15))
        };
        var result = EventFilter.Apply(events);
        Assert.Equal(new[] { 2, 3, 1 }, result.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var result = EventFilter.Apply(Array.Empty<EventLogEntry>());
        Assert.Empty(result);
    }
}

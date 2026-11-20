using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class WindowsPathRegistrarTests
{
    private const string Entry = @"C:\tools\synchro";

    private static PathRegistryChange Add(FakeUserPathStore store, string entry) =>
        WindowsPathRegistrar.Add(store, entry).ShouldBeOfType<Result<PathRegistryChange>.Ok>().Value;

    private static PathRegistryChange Remove(FakeUserPathStore store, string entry) =>
        WindowsPathRegistrar.Remove(store, entry).ShouldBeOfType<Result<PathRegistryChange>.Ok>().Value;

    [Fact]
    public void Add_WhenStoreEmpty_WritesSingleEntry()
    {
        var store = new FakeUserPathStore();

        var change = Add(store, Entry);

        change.Changed.ShouldBeTrue();
        change.AlreadyPresent.ShouldBeFalse();
        store.Value.ShouldBe(Entry);
        store.WriteCount.ShouldBe(1);
    }

    [Fact]
    public void Add_WhenEntryPresent_MakesNoChange()
    {
        var store = new FakeUserPathStore { Value = Entry };

        var change = Add(store, Entry);

        change.Changed.ShouldBeFalse();
        change.AlreadyPresent.ShouldBeTrue();
        store.WriteCount.ShouldBe(0);
    }

    [Fact]
    public void Add_WhenEntryPresentInDifferentCase_MakesNoChange()
    {
        var store = new FakeUserPathStore { Value = Entry.ToLowerInvariant() };

        var change = Add(store, Entry);

        change.AlreadyPresent.ShouldBeTrue();
        store.WriteCount.ShouldBe(0);
    }

    [Fact]
    public void Add_WhenOtherEntriesPresent_AppendsAndPreservesThem()
    {
        var store = new FakeUserPathStore { Value = @"C:\tools;%JAVA_HOME%\bin" };

        var change = Add(store, Entry);

        change.Changed.ShouldBeTrue();
        store.Value.ShouldBe(@"C:\tools;%JAVA_HOME%\bin;" + Entry);
    }

    [Fact]
    public void Remove_WhenEntryPresent_RemovesOnlyThatEntry()
    {
        var store = new FakeUserPathStore { Value = Entry + @";C:\tools;C:\other" };

        var change = Remove(store, Entry);

        change.Changed.ShouldBeTrue();
        store.Value.ShouldBe(@"C:\tools;C:\other");
    }

    [Fact]
    public void Remove_WhenEntryAbsent_MakesNoChange()
    {
        var store = new FakeUserPathStore { Value = @"C:\tools;C:\other" };

        var change = Remove(store, Entry);

        change.Changed.ShouldBeFalse();
        store.Value.ShouldBe(@"C:\tools;C:\other");
        store.WriteCount.ShouldBe(0);
    }

    [Fact]
    public void Remove_WhenStoreEmpty_MakesNoChange()
    {
        var store = new FakeUserPathStore();

        var change = Remove(store, Entry);

        change.Changed.ShouldBeFalse();
        store.WriteCount.ShouldBe(0);
    }
}

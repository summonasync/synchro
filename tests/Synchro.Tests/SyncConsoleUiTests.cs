using Shouldly;
using Synchro.App.Commands;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class SyncConsoleUiTests
{
    [Fact]
    public void ResolveConflict_EnterDefaultsToMaster_GarbageReasks_SChoosesSlave()
    {
        var originalIn = Console.In;
        try
        {
            Console.SetIn(new StringReader("\njunk\ns\n\n"));
            var ui = new SyncConsoleUi();

            ui.ResolveConflict("a.txt").ShouldBe(ConflictWinner.Master);
            ui.ResolveConflict("b.txt").ShouldBe(ConflictWinner.Slave);
            ui.ResolveConflict("c.txt").ShouldBe(ConflictWinner.Master);
        }
        finally
        {
            Console.SetIn(originalIn);
        }
    }

    [Fact]
    public void ResolveConflict_SlaveWordAccepted()
    {
        var originalIn = Console.In;
        try
        {
            Console.SetIn(new StringReader("SLAVE\n"));
            new SyncConsoleUi().ResolveConflict("d.txt").ShouldBe(ConflictWinner.Slave);
        }
        finally
        {
            Console.SetIn(originalIn);
        }
    }
}

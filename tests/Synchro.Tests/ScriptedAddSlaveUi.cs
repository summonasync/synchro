using Synchro.Core.Slave;

namespace Synchro.Tests;

public sealed class ScriptedAddSlaveUi(
    string name = "slave-a",
    string syncBranch = "",
    string? baseBranch = null,
    bool consent = true) : IAddSlaveUi
{
    public string AskSlaveName() => name;

    public string AskSyncBranch(string suggestion) => syncBranch;

    public string AskBaseBranch(string? suggestion) => baseBranch ?? string.Empty;

    public bool ConfirmBootstrap(string root) => consent;
}

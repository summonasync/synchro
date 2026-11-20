namespace Synchro.Core.Slave;

public interface IAddSlaveUi
{
    string AskSlaveName();

    string AskSyncBranch(string suggestion);

    string AskBaseBranch(string? suggestion);

    bool ConfirmBootstrap(string root);
}

namespace Synchro.Core.Install;

public interface IUserPathStore
{
    Result<string?> Read();

    Result<string> Write(string value);
}

using Synchro.Core;
using Synchro.Core.Install;

namespace Synchro.Tests;

public sealed class FakeUserPathStore : IUserPathStore
{
    public string? Value { get; set; }

    public int WriteCount { get; private set; }

    public Result<string?> Read() => new Result<string?>.Ok(Value);

    public Result<string> Write(string value)
    {
        WriteCount++;
        Value = value;
        return new Result<string>.Ok(value);
    }
}

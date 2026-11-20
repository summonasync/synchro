namespace Synchro.Core.Install;

public interface IInstallPlatform
{
    string Name { get; }

    string Root { get; }

    string BinaryPathFor(string version);

    Result<BinaryPlacement> PlaceBinary(string sourcePath, string version);

    Result<NameRegistration> RegisterName(string version, bool pathFixRequested, bool forceName);

    Result<IReadOnlyList<string>> RemoveOtherVersions(string keepVersion);

    Result<UninstallOutcome> Uninstall();
}

namespace Synchro.Core.Install;

public sealed record PathRegistryChange(bool Changed, bool AlreadyPresent, string Value);

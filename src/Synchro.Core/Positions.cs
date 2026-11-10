using System.Text.Json.Serialization;

namespace Synchro.Core;

public enum SyncMode
{
    Git,
    Plain
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(GitPosition), "git")]
[JsonDerivedType(typeof(ChainPosition), "chain")]
public abstract record Position
{
    public abstract string Display { get; }
}

public sealed record GitPosition(string Sha) : Position
{
    public override string Display => Sha[..Math.Min(12, Sha.Length)];
}

public sealed record ChainPosition(int Seq, string RootHash) : Position
{
    public override string Display => $"#{Seq}:{RootHash[..Math.Min(12, RootHash.Length)]}";
}

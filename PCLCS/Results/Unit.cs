namespace PCLCS.Results;

/// <summary>没有成功返回值时使用；default 是唯一值。</summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static Unit Value => default;

    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";
}
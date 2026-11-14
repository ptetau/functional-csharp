namespace System;

// List patterns (`xs is [var first, ..]`) and `^1` need this type, which netstandard2.0 doesn't have. The compiler
// uses its constructor, GetOffset and Value. The code fixes compile this file too (each assembly needs its own copy).
internal readonly struct Index
{
    private readonly int value;

    public Index(int value, bool fromEnd = false) => this.value = fromEnd ? ~value : value;

    public int Value => value < 0 ? ~value : value;

    public bool IsFromEnd => value < 0;

    public int GetOffset(int length) => IsFromEnd ? length - Value : Value;

    public static implicit operator Index(int value) => new(value);
}

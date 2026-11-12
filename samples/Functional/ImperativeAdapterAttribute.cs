namespace Functional;

/// <summary>Marks a type or member as an adapter to imperative code. FunctionalLint exempts it from its rules.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property)]
public sealed class ImperativeAdapterAttribute : Attribute;

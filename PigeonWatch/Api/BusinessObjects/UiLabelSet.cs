namespace PigeonWatch.BusinessObjects;

public sealed record UiLabelSet(string Culture, IReadOnlyDictionary<string, string> Labels);

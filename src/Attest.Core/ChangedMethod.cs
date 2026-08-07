namespace Attest.Core;

/// <summary>
/// A method the DiffScope considers in scope, either because the diff touched its body
/// directly or because it directly calls one that did.
/// </summary>
/// <param name="FilePath">Absolute path of the file declaring the method.</param>
/// <param name="ContainingType">Fully qualified name of the declaring type.</param>
/// <param name="MethodName">The method's name, not its full signature.</param>
/// <param name="StartLine">First line of the method declaration, 1-based.</param>
/// <param name="EndLine">Last line of the method declaration, 1-based.</param>
public sealed record ChangedMethod(
    string FilePath,
    string ContainingType,
    string MethodName,
    int StartLine,
    int EndLine);

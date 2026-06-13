namespace IronGOAL.Scripting;

/// <summary>
/// One top-level Scheme datum read from a source via
/// <c>(read (open-string-input-port ...))</c>, plus a human-readable
/// preview for error reporting.
/// </summary>
/// <param name="Datum">
/// The parsed Scheme datum - typically a <c>Cons</c> for
/// <c>(define ...)</c>/<c>(foo ...)</c> forms, but may be a bare symbol,
/// number, or string for a top-level expression that is just a literal or
/// variable reference.  <c>null</c> only for the <see cref="Index"/> == 0
/// pseudo-form used when the source could not be read at all (see
/// <see cref="ScriptLoader.LoadSource"/>).
/// </param>
/// <param name="Index">
/// 1-based position of this form within its source (the order
/// <c>read</c> returned it in).  <c>0</c> is reserved for the pseudo-form
/// representing a whole-source syntax error.
/// </param>
/// <param name="Preview">
/// A short, single-line, whitespace-collapsed rendering of
/// <see cref="Datum"/> (via Scheme <c>write</c>) - or, for the
/// <see cref="Index"/> == 0 pseudo-form, a preview of the raw source text
/// that <c>read</c> rejected.
/// </param>
public readonly record struct SchemeForm(object? Datum, int Index, string Preview)
{
    public override string ToString() => Index == 0 ? Preview : $"#{Index}: {Preview}";
}

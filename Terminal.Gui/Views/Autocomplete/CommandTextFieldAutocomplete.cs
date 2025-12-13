namespace Terminal.Gui.Views;
/// <summary>
///     Renders an overlay on another view at a given point that allows selecting from a range of 'autocomplete'
///     options. An implementation on a TextField.
/// </summary>
public class CommandTextFieldAutocomplete : CommandPopupAutocomplete
{
    /// <inheritdoc/>
    protected override string GetText() { return ((TextField)HostControl).Text; }

    /// <inheritdoc/>
    protected override void DeleteTextBackwards() { ((TextField)HostControl).DeleteCharLeft(false); }

    /// <inheritdoc/>
    protected override void InsertText(string accepted) { ((TextField)HostControl).InsertText(accepted, false); }

    // <inheritdoc/>
    protected override int GetCursorPosition() { return ((TextField)HostControl).CursorPosition; }

    /// <inheritdoc/>
    protected override void SetCursorPosition(int column) { ((TextField)HostControl).CursorPosition = column; }
}


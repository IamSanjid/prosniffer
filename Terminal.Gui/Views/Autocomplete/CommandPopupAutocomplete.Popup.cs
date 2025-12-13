using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Terminal.Gui.Views;

public abstract partial class CommandPopupAutocomplete
{
    private sealed class Popup : View
    {
        public Popup(CommandPopupAutocomplete autoComplete)
        {
            _autoComplete = autoComplete;
            CanFocus = true;
            TabStop = TabBehavior.NoStop;
            WantMousePositionReports = true;
        }

        private readonly CommandPopupAutocomplete _autoComplete;

        protected override bool OnDrawingContent(DrawContext? context)
        {
            if (!_autoComplete.LastPopupPos.HasValue)
            {
                return true;
            }

            _autoComplete.RenderOverlay(_autoComplete.LastPopupPos.Value);

            return true;
        }

        protected override bool OnMouseEvent(MouseEventArgs mouseEvent) { return _autoComplete.OnMouseEvent(mouseEvent); }
    }
}

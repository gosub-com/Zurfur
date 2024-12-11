using System;
using System.Text;
using System.Globalization;
using System.Diagnostics;

using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;

using Avalonia.Threading;

using Gosub.Lex;
namespace Gosub.Avalonia.Controls;

/// <summary>
/// Avalonia editor control.
/// </summary>
public class Editor : UserControl
{
    const double DEFAULT_FONT_SIZE = 16;
    readonly float SHRUNK_EMPTY_LINE_SCALE = 0.99f;
    readonly float SHRUNK_TEXT_LINE_SCALE = 0.5f;
    readonly float SHRUNK_FONT_SCALE = 0.65f;
    readonly Point SHRUNK_FONT_OFFSET = new Point(0.2f, -0.12f); // Scaled by font size
    const int FILL_X_OFFSET = 3;
    const int LEFT_MARGIN = 0; // TBD: Change to variable and increase to add line numbers

    static TextDecorationCollection s_underline = [new()];
    static Token s_normalToken = new();


    // TBD: From template?
    const string FONT_NAME = "Courier New";

    static Editor()
    {
        FocusableProperty.OverrideDefaultValue(typeof(Editor), true);
    }


    // Lexer and text
    Lexer _lexer = new();
    List<UndoOp> _undo = new();
    List<UndoOp> mRedo = new();
    bool _readOnly;
    bool _shrinkLines = true;
    int _modifySaved;
    int _modifyCount;
    int _modifyTotal;

    // Tabs and character
    double[] _tabSpacing = new double[32];
    string[] _insertOneString = [""];
    int _tabSize = 4;
    bool _tabInsertsSpaces = true;

    // Mouse and drawing info
    bool _mouseDownSelect;
    bool _mouseDownDrag;   // TBD: Use gesture and allow scroll after release
    Point? _mouseCurrentPosition;   // Current mouse position or NULL when not on screen
    Point _mouseDownPosition;
    double _mouseDownVerticalScroll;
    Token? _mouseHoverToken;
    Point _topLeft;
    Point? _testPoint;
    Token? _testToken;
    Size _fontSize = new(9, 19);
    Size _clientSize;


    // NOTE: Invalidating can cause so much screen updating that the screen doesn't scroll
    // via the timer.  TBD: This was true in winforms, not sure about Avalonia. 
    bool _delayedInvalidate;

    // There is an extra one for top of the next line after the last line
    int[] _lineTops = [];
    bool[] _lineShrunk = [];

    // Cursor info
    TokenLoc _cursorLoc;
    int _cursorUpDownColumn = -1;
    DateTime _cursorBaseTime;
    Rect _cursorRect;
    bool _cursorVisible;
    bool _overwriteMode;


    // Selection
    TokenLoc _selStart;
    TokenLoc _selEnd;

    // Fonts, colors, and misc.
    Dictionary<TokenType, FontInfo> _tokenFonts = new();
    Dictionary<TokenType, FontInfo> _tokenFontsBold = new();
    Dictionary<TokenType, FontInfo> _tokenFontsGrayed = new();
    Typeface _shrunkFont = new(FONT_NAME); // TBD: Remove
    Brush _selectColor = new SolidColorBrush(new Color(255, 208, 208, 255));
    Brush _selectColorNoFocus = new SolidColorBrush(new Color(255, 224, 224, 224));
    EventArgs _eventArgs = new();
    Brush _errorColor = new SolidColorBrush(Colors.Pink);
    Brush _warnColor = new SolidColorBrush(Colors.Yellow);
    Brush _codeInCommentColor = new SolidColorBrush(new Color(255, 208, 255, 208));
    Brush _continuationBrush = new SolidColorBrush(Colors.LightGray);
    Brush _textUnderOverwriteCursor = new SolidColorBrush(Colors.White);
    Pen _scopeLinePen = new Pen(Colors.LightGray.ToUInt32(), 1, new DashStyle([4,4], 0));
    Pen _scopeLineErrorPen = new Pen(Colors.Red.ToUInt32(), 1, new DashStyle([4, 4], 0));
    TokenColorOverride[] _tokenColorOverrides = [];
    DispatcherTimer _timer = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(20) };
    Cursor _beamCursor = new(StandardCursorType.Ibeam);
    Cursor _arrowCursor = new(StandardCursorType.Arrow);
    Cursor? _cursorOverride = null;

    VerticalMarkInfo[] _verticalMarks = [];

    ScrollBar _vScrollBar = new();
    public int TopVisibleLine => (int)_vScrollBar.Value;

    ScrollBar _hScrollBar = new();


    // TBD: Port to Avalonia
    void MessageBox(string message) { }


    public bool RemoveWhiteSpaceAtEndOnEnter = true;
    public bool ShowMetaTokens;

    // Internal quick access to mLexer
    int LineCount { get { return _lexer.LineCount; } }
    string GetLine(int line) { return _lexer.GetLine(line); }

    // Delegate types
    public delegate void EditorTokenDelegate(object sender, Token? previousToken, Token? newToken);

    /// <summary>
    /// This event occurs when the mouse hover token changes.
    /// </summary>
    public event EditorTokenDelegate? MouseHoverTokenChanged;

    /// <summary>
    /// This event occurs when the cursor location changes
    /// </summary>
    public event EventHandler? CursorLocChanged;

    /// <summary>
    /// This event happens if a text change is blocked because
    /// the ReadOnly flag is set.  Resetting the ReadOnly flag
    /// inside this event allows the text to be changed.
    /// </summary>
    public event EventHandler? BlockedByReadOnly;

    /// <summary>
    /// This event happens after the text has been changed.
    /// </summary>
    public event EventHandler? TextChanged;

    /// <summary>
    /// Occurs when Modify changes
    /// </summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>
    /// Occurs after the lexer has been set (even if the object does not change)
    /// </summary>
    public event EventHandler? LexerChanged;

    public Editor()
    {
        _cursorBaseTime = DateTime.Now;
        _timer.Tick += _timer_Tick;
        FontSize = DEFAULT_FONT_SIZE;
    }

    /// <summary>
    /// Set this to false when the file is saved
    /// </summary>
    public bool Modified
    {
        get { return _modifyCount != _modifySaved; }
        set
        {
            if (value == Modified)
                return;
            _modifySaved = value ? -1 : _modifyCount;
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
    }


    /// <summary>
    /// Setup horizontal & vertical scroll bars
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Content != null)
            return;

        // Vertical scroll bar
        _vScrollBar.Visibility = ScrollBarVisibility.Visible;
        _vScrollBar.HorizontalAlignment = HorizontalAlignment.Right;
        _vScrollBar.VerticalAlignment = VerticalAlignment.Stretch;
        _vScrollBar.Visibility = ScrollBarVisibility.Visible;
        _vScrollBar.ValueChanged += _vScrollBar_ValueChanged;
        DockPanel.SetDock(_vScrollBar, Dock.Right);
           
        // Horizontal scroll bar
        _hScrollBar.Visibility = ScrollBarVisibility.Visible;
        _hScrollBar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _hScrollBar.VerticalAlignment = VerticalAlignment.Bottom;
        _hScrollBar.Visibility = ScrollBarVisibility.Visible;
        _hScrollBar.Orientation = Orientation.Horizontal;
        _hScrollBar.ValueChanged += _hScrollBar_ValueChanged;
        DockPanel.SetDock(_hScrollBar, Dock.Bottom);

        // Dock them
        var panel = new DockPanel();
        Content = panel;
        panel.Children.Add(_vScrollBar);
        panel.Children.Add(_hScrollBar);
    }


    /// <summary>
    /// TBD: Copied from Avalonia.Controls.TextBox
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Sets all text, returns only the first line of text
    /// </summary>
    public string Text
    {
        get
        {
            return GetLine(0);
        }
        set
        {
            if (LineCount == 1 && GetLine(0) == value)
                return;
            _lexer.ReplaceText(new string[] { value },
                new TokenLoc(0, 0),
                new TokenLoc(GetLine(LineCount - 1).Length, LineCount));
            OnTextChangedInternal();
        }
    }

    /// <summary>
    /// Called whenever the text is changed.  Calls the user delegate TextChanged2
    /// </summary>
    void OnTextChangedInternal()
    {
        TextChanged?.Invoke(this, _eventArgs);
        UpdateMouseHoverToken();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Read only mode - Do not allow user to change text
    /// </summary>
    public bool ReadOnly
    {
        get { return _readOnly; }
        set
        {
            if (value == _readOnly)
                return;
            _readOnly = value;
            InvalidateVisual();
        }
    }

    public bool ShrinkLines
    {
        get { return _shrinkLines; }
        set
        {
            if (value == _shrinkLines)
                return;
            _shrinkLines = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }


    /// <summary>
    /// Show marks on the vertical scroll bar
    /// </summary>
    public void SetMarks(VerticalMarkInfo[] marks)
    {
        // TBD: Port to Avalonia
        _verticalMarks = marks;
        InvalidateVisual();
    }

    /// <summary>
    /// Overwrite mode
    /// </summary>
    public bool OverwriteMode
    {
        get { return _overwriteMode; }
        set
        {
            if (_overwriteMode == value)
                return;
            _overwriteMode = value;
            UpdateCursorBlinker();
        }
    }

    /// <summary>
    /// Token the mouse is currenty hovering over
    /// </summary>
    public Token? MouseHoverToken
    {
        get { return _mouseHoverToken; }
    }

    /// <summary>
    /// Array of tokens to override the default coloring
    /// </summary>
    public TokenColorOverride[] TokenColorOverrides
    {
        get { return _tokenColorOverrides; }
        set
        {
            _tokenColorOverrides = value;
            InvalidateVisual();
        }
    }


    /// <summary>
    /// Set the cursor (null to set the default)
    /// </summary>
    public Cursor? CursorOverride
    {
        get => _cursorOverride;
        set
        {
            if (value != _cursorOverride)
            {
                _cursorOverride = value;
                UpdateMouseHoverToken();
            }
        }
    }

    /// <summary>
    /// Fixes the chararcter and line index to be within bounds.
    /// NOTE: The char index can be equal to the end of char index
    /// </summary>
    void FixCursorLocation(ref TokenLoc cursor)
    {
        cursor.Y = Math.Min(cursor.Y, LineCount - 1);
        cursor.Y = Math.Max(cursor.Y, 0);
        cursor.X = Math.Min(cursor.X, GetLine(cursor.Y).Length);
        cursor.X = Math.Max(cursor.X, 0);
    }


    /// <summary>
    /// Allow user code to change cursor location.
    /// Adjusts cursor location to be within text bounds.
    /// </summary>
    public TokenLoc CursorLoc
    {
        get { return _cursorLoc; }
        set
        {
            FixCursorLocation(ref value);
            if (value == _cursorLoc)
                return;
            _lexer.Cursor = _cursorLoc;
            _cursorLoc = value;
            CursorLocChanged?.Invoke(this, _eventArgs);
            UpdateCursorBlinker();
        }
    }


    /// <summary>
    /// Returns the number of full lines in the window (partial lines don't count).
    /// </summary>
    public int LinesInWindow()
    {
        const int REMOVE_PARTIALS = 2; // Add 1 for bottom of line, then 1 more to remove partials
        var height = Math.Min(10000, _clientSize.Height);
        var lines = 0;
        var offset = TopVisibleLine + REMOVE_PARTIALS;
        while (PointY(lines + offset) < height)
            lines++;

        return Math.Max(1, lines);
    }

    /// <summary>
    /// Returns the size
    /// </summary>
    int CharsAcrossWindow()
    {
        return Math.Max(0, (int)(_clientSize.Width / _fontSize.Width) - 1);
    }

    // Set the top left corner of the viewport
    void SetTopLeft(int col, int line)
    {
        _topLeft = new Point((int)PointXAbs(col), (int)PointYAbs(line));
    }

    double PointXAbs(int col)
    {
        return col * _fontSize.Width;
    }

    double PointYAbs(int line)
    {
        if (line < 0 || _lineTops.Length == 0)
            return line * _fontSize.Height;
        if (line < _lineTops.Length)
            return _lineTops[line];
        return _lineTops[_lineTops.Length - 1] + (line - _lineTops.Length + 1) * _fontSize.Height;
    }

    /// <summary>
    /// Return X position in window, given the column number
    /// </summary>
    double PointX(int col)
    {
        return PointXAbs(col) - _topLeft.X + LEFT_MARGIN;
    }

    /// <summary>
    /// Return Y position in window, given the line number
    /// </summary>
    double PointY(int line)
    {
        return PointYAbs(line) - _topLeft.Y;
    }

    Point ScreenToText(double x, double y)
    {
        x += _topLeft.X - LEFT_MARGIN;
        y += _topLeft.Y;
        var pointX = (int)(x / _fontSize.Width + 0.5f);
        if (y < 0 || _lineTops.Length == 0)
            return new Point(pointX, (int)(y / _fontSize.Height));

        // This function isn't used often, so it's OK to be slow
        for (int line = 1; line < _lineTops.Length; line++)
            if (y < _lineTops[line])
                return new Point(pointX, line - 1);

        return new Point(pointX, (int)(_lineTops.Length - 1 + y / _fontSize.Height));
    }


    /// <summary>
    /// Returns the position of the text at the given token location
    /// </summary>
    public Point LocationToken(TokenLoc loc)
    {
        return new Point((int)PointX(IndexToCol(loc)), (int)PointY(loc.Y));
    }



    /// <summary>
    /// Convert the index to a column
    /// </summary>
    int IndexToCol(TokenLoc loc)
    {
        if (loc.Y < 0 || loc.Y >= LineCount)
            return 0;
        return IndexToCol(GetLine(loc.Y), loc.X);
    }

    /// <summary>
    /// Convert character index to column index.  If charIndex is
    /// too large, the end of the line is returned.
    /// NOTE: The column index accounts for extra spaces
    /// inserted because of TABS.
    /// </summary>
    public int IndexToCol(string line, int charIndex)
    {
        const int MAX_TAB_COL = 256;
        // For long lines, this is horrendously slow.  Need to fix. 
        // As a quick & dirty fix, just ignore tabs past column MAX_TAB_COL
        int col = 0;
        for (int i = 0; i < charIndex && i < line.Length; i++)
        {
            if (line[i] == '\t')
                col += _tabSize - col % _tabSize;
            else
                col++;
            if (i > MAX_TAB_COL)
                return col + charIndex - i - 1;
        }
        return col + Math.Max(0, charIndex - line.Length);
    }

    /// <summary>
    /// Convert column index to character index.  If colIndex is
    /// too large, the end of the line is returned.
    /// NOTE: The column index accounts for extra spaces
    /// inserted because of TABS.
    /// </summary>
    public int ColToIndex(string line, int colIndex)
    {
        int col = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\t')
                col += _tabSize - col % _tabSize;
            else
                col++;

            if (col > colIndex)
                return i;
        }
        return line.Length;
    }



    /// <summary>
    /// Get the next character position (can return next line)
    /// </summary>
    public TokenLoc CharIndexInc(TokenLoc inc)
    {
        if (inc.X < GetLine(inc.Y).Length)
        {
            inc.X++;
        }
        else if (inc.Y < LineCount - 1)
        {
            inc.X = 0;
            inc.Y++;
        }
        return inc;
    }

    /// <summary>
    /// Get the previous character position (can return previous line)
    /// </summary>
    public TokenLoc CharIndexDec(TokenLoc dec)
    {
        if (dec.X > 0)
        {
            dec.X--;
        }
        else if (dec.Y > 0)
        {
            dec.Y--;
            dec.X = GetLine(dec.Y).Length;
        }
        return dec;
    }

    /// <summary>
    /// Returns the font used to draw the given token type
    /// </summary>
    FontInfo GetFontInfo(Token token)
    {
        if (_tokenFonts.Count == 0)
        {
            var normalFont = new Typeface(FONT_NAME, FontStyle.Normal, FontWeight.Medium);
            var boldFont = new Typeface(FONT_NAME, FontStyle.Normal, FontWeight.Bold);
            _shrunkFont = new Typeface(FONT_NAME, FontStyle.Normal, FontWeight.Normal);

            // TBD: These should come from a Json config file, and
            //      eTokenType should be an open ended index (i.e. integer)
            _tokenFonts = new Dictionary<TokenType, FontInfo>()
            {
                { TokenType.Normal, new FontInfo(normalFont, Colors.Black) },
                { TokenType.Identifier, new FontInfo(normalFont, Colors.Black) },
                { TokenType.Reserved, new FontInfo(normalFont, Colors.Blue) },
                { TokenType.ReservedControl, new FontInfo(boldFont, Colors.Blue) },
                { TokenType.ReservedVar, new FontInfo(boldFont, Colors.DarkBlue) },
                { TokenType.ReservedType, new FontInfo(boldFont, new Color(255, 20,125,160)) },
                { TokenType.Quote, new FontInfo(normalFont, Colors.Brown) },
                { TokenType.Comment, new FontInfo(normalFont, Colors.Green) },
                { TokenType.NewVarSymbol, new FontInfo(normalFont, Colors.Blue) },
                { TokenType.DefineField, new FontInfo(boldFont, Colors.Black) },
                { TokenType.DefineMethod, new FontInfo(boldFont, Colors.Black) },
                { TokenType.DefineFunParam, new FontInfo(boldFont, Colors.Black) },
                { TokenType.DefineTypeParam, new FontInfo(boldFont, Colors.Black) },
                { TokenType.DefineLocal, new FontInfo(boldFont, Colors.Black) },
                { TokenType.TypeName, new FontInfo(normalFont, new Color(255, 20,125,160)) },
                { TokenType.BoldSymbol, new FontInfo(boldFont, Colors.Black) },
            };

            // Setup bold and grayed fonts
            // NOTE: Bold for token types is set above, this is bold for the token bold bit
            foreach (var font in _tokenFonts)
            {
                _tokenFontsBold[font.Key] = new FontInfo(
                    new Typeface(font.Value.Font.FontFamily, weight: FontWeight.Bold), font.Value.Color);
                _tokenFontsGrayed[font.Key] = new FontInfo(
                    font.Value.Font, Lerp(font.Value.Color, Colors.LightGray, 0.5f));
            }
        }
        // Font info: normal, bold, or grayed (only one can be selected for now)
        Dictionary<TokenType, FontInfo> colorTable;
        if (token.Bold)
            colorTable = _tokenFontsBold;
        else if (token.Grayed)
            colorTable = _tokenFontsGrayed;
        else
            colorTable = _tokenFonts;

        if (!colorTable.TryGetValue(token.Type, out var fontInfo))
            return colorTable[TokenType.Normal];
        return fontInfo;
    }

    static Color Lerp(Color from, Color to, float percent)
    {
        int a1 = (int)(percent * 256);
        int a2 = 256 - a1;
        return new Color(255, (byte)((a2 * from.R + a1 * to.R) >> 8),
                              (byte)((a2 * from.G + a1 * to.R) >> 8),
                              (byte)((a2 * from.B + a1 * to.B) >> 8));
    }

    /// <summary>
    /// Set tab size
    /// </summary>
    public int TabSize
    {
        get { return _tabSize; }
        set
        {
            if (_tabSize == value)
                return;
            InvalidateVisual();
            InvalidateMeasure();
        }
    }


    void SetupScrollBars()
    {
        // Vertical properties
        int linesInFile = LineCount;
        int linesInWindow = LinesInWindow();
        _vScrollBar.Maximum = Math.Max(1, linesInFile - linesInWindow);
        _vScrollBar.LargeChange = linesInWindow;
        _vScrollBar.ViewportSize = linesInWindow;
        //vScrollBar.IsEnabled = linesInFile > linesInWindow;
        //vScrollBar.IsVisible = linesInFile > linesInWindow && linesInFile > 1;
        _vScrollBar.SmallChange = 1;

        // Horizontal properties
        int charsAccross = 0;
        int charsAcrossWindow = CharsAcrossWindow();
        for (int i = 0; i < LineCount; i++)
            charsAccross = Math.Max(charsAccross, IndexToCol(GetLine(i), GetLine(i).Length));
        _hScrollBar.Maximum = charsAccross;
        _hScrollBar.LargeChange = Math.Max(1, charsAcrossWindow);
        _hScrollBar.IsEnabled = charsAccross > charsAcrossWindow;
        _hScrollBar.IsVisible = charsAccross > charsAcrossWindow && charsAccross > 1;
        if (!_hScrollBar.IsEnabled)
            _hScrollBar.Value = 0;
        _hScrollBar.SmallChange = 1;

        // Location & Size
        //vScrollBar.Location = new Point(Math.Max(0, _clientSize.Width - vScrollBar.Width), 0);
        //vScrollBar.Height = Math.Max(0, _clientSize.Height);
        //hScrollBarLocation = new Point(0, Math.Max(0, _clientSize.Height - hScrollBarHeight));
        //hScrollBarWidth = Math.Max(0, _clientSize.Width - (_vScrollBar.IsVisible ? _vScrollBar.Width : 0));
    }
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        SetupScrollBars();
    }


    // TBD: Port to Avalonia
    protected 
    // override
    void OnFontChanged(EventArgs e)
    {
        InvalidateMeasure();
        _tokenFonts.Clear();
        var vScrollWidth = _vScrollBar.Width; // Preserve vScrollBar width which gets changed when font is changed
        //base.OnFontChanged(e);
        _vScrollBar.Width = vScrollWidth;
    }


    // TBD: Port to Avalonia
    protected
    // override 
    void OnVisibleChanged(EventArgs e)
    {
        //base.OnVisibleChanged(e);
        UpdateCursorBlinker();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        UpdateCursorBlinker();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        UpdateCursorBlinker();
        base.OnLostFocus(e);
        InvalidateVisual();
    }


    /// <summary>
    /// Returns TRUE if the cursor should be visible, based
    /// on the control visibility, focus, and cursor blink time.
    /// </summary>
    bool IsCursorVisible()
    {
        return IsVisible && IsFocused
                && (DateTime.Now - _cursorBaseTime).Milliseconds < 600;
    }


    /// <summary>
    /// Sets the cursor location, and resets the cursor blink timer.
    /// </summary>
    void UpdateCursorBlinker()
    {

        _timer.IsEnabled = IsVisible && IsFocused;
        _cursorBaseTime = DateTime.Now;
        int column = IndexToCol(CursorLoc);

        var x = (int)PointX(column);
        var y = (int)PointY(CursorLoc.Y);
        Rect cursorRect = new Rect(x + 1 + (_overwriteMode ? 2 : 0), y + 1,
                                             _overwriteMode && !HasSel() ? (int)_fontSize.Width : 2,
                                            (int)(PointY(CursorLoc.Y + 1) - y) - 2);

        if (cursorRect != _cursorRect)
        {
            InvalidateVisual();
            _cursorRect = cursorRect;
        }
        bool visibile = IsCursorVisible();
        if (visibile != _cursorVisible)
        {
            InvalidateVisual();
            _cursorVisible = visibile;
        }
    }


    /// <summary>
    /// Move the cursor down (or up if lines is negative)
    /// </summary>
    TokenLoc ArrowKeyDown(TokenLoc cursor, int lines)
    {
        // Calculate new cursor location
        int oldCursorLine = cursor.Y;
        cursor.Y += lines;
        cursor.Y = Math.Min(LineCount - 1, cursor.Y);
        cursor.Y = Math.Max(0, cursor.Y);

        if (cursor.Y == oldCursorLine)
            return cursor;

        // Set column index
        int oldArrowColumn = _cursorUpDownColumn;
        if (_cursorUpDownColumn < 0)
            _cursorUpDownColumn = IndexToCol(CursorLoc);

        if (oldArrowColumn >= 0)
            cursor.X = ColToIndex(GetLine(cursor.Y), oldArrowColumn);
        else
            cursor.X = ColToIndex(GetLine(cursor.Y), _cursorUpDownColumn);

        FixCursorLocation(ref cursor);
        return cursor;
    }

    /// <summary>
    /// Move the cursor to the new location (arrow keys), maintain 
    /// or clear the selection as necessary. 
    /// </summary>
    void MoveCursor(TokenLoc newCursor, bool left, bool shift)
    {
        if (shift)
        {
            // Set beginning of selected text
            if (!HasSel())
                _selStart = _selEnd = CursorLoc;

            // Move cursor to new location
            TokenLoc oldCursor = CursorLoc;
            CursorLoc = newCursor;

            // Update text selction
            if (_selStart == oldCursor)
                _selStart = CursorLoc;
            else if (_selEnd == oldCursor)
                _selEnd = CursorLoc;
            TokenLoc.FixOrder(ref _selStart, ref _selEnd);
        }
        else
        {
            // Move the cursor (clear the selection)
            if (HasSel())
                SelClear(left);
            else
                CursorLoc = newCursor;
        }
    }


    /// <summary>
    /// Ensure the cursor is on the screen (with a small margin)
    /// </summary>
    void EnsureCursorOnScreen()
    {
        // Vertical
        int linesInWindow = LinesInWindow();
        int marginY = Math.Min(2, Math.Max(0, linesInWindow - 4));
        if (CursorLoc.Y < _vScrollBar.Value + marginY)
            _vScrollBar.Value = Math.Max(0, CursorLoc.Y - marginY);
        if (CursorLoc.Y > _vScrollBar.Value + linesInWindow - marginY)
            _vScrollBar.Value = Math.Min(_vScrollBar.Maximum, CursorLoc.Y - linesInWindow + marginY);

        // Horizontal
        int charsAcrossWindow = CharsAcrossWindow();
        int marginX = Math.Min(4, Math.Max(0, charsAcrossWindow - 5));
        if (IndexToCol(CursorLoc) < _hScrollBar.Value + marginX)
            _hScrollBar.Value = Math.Max(0, IndexToCol(CursorLoc) - marginX);
        
        // TBD: Can scrolls off screen (CTRL-click a symbol)
        //if (IndexToCol(CursorLoc) > _hScrollBar.Value + charsAcrossWindow - marginX)
        //    _hScrollBar.Value = Math.Min(_hScrollBar.Maximum, IndexToCol(CursorLoc) - charsAcrossWindow + marginX);
    }

    /// <summary>
    /// Cut to clipboard
    /// </summary>
    void Cut()
    {
        Copy();
        SelDel();
    }

    /// <summary>
    /// Copy to clipboard
    /// </summary>
    void Copy()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        try
        {
            string[] copy = _lexer.GetText(_selStart, _selEnd);
            if (copy.Length == 1)
            {
                clipboard.SetTextAsync(copy[0]);
            }
            else if (copy.Length > 1)
            {
                int length = 0;
                foreach (string s in copy)
                    length += s.Length + 2;
                var sb = new StringBuilder(length + 3);
                sb.Append(copy[0]);
                for (int i = 1; i < copy.Length; i++)
                {
                    sb.Append("\r\n");
                    sb.Append(copy[i]);
                }
                clipboard.SetTextAsync(sb.ToString());
            }
            else
            {
                clipboard.SetTextAsync("");
            }
        }
        catch (Exception e)
        {
            MessageBox("Error copying to clipboard: " + e.Message);
        }
    }


    /// <summary>
    /// Paste from clipboard
    /// </summary>
    async void Paste()
    {
        SelDel();

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;
        try
        {
            string clip = await clipboard.GetTextAsync() ?? "";
            string[] clipArray = clip.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            CursorLoc = ReplaceText(clipArray, CursorLoc, CursorLoc);

        }
        catch (Exception e)
        {
            MessageBox("Error copying from clipboard: " + e.Message);
        }

    }

    /// <summary>
    /// Returns an undo operation with the current state filled in.
    /// NOTE: You must fill in the Text, TextStart, and TextEnd
    /// </summary>
    UndoOp GetDefaultUndo()
    {
        UndoOp undo = new UndoOp();
        undo.TopIndex = (int)_vScrollBar.Value;
        undo.LeftIndex = (int)_hScrollBar.Value;
        undo.ModifyCount = _modifyCount;
        undo.SelStart = _selStart;
        undo.SelEnd = _selEnd;
        undo.Cursor = CursorLoc;
        return undo;
    }


    /// <summary>
    /// Replace some text.  This function consolidates all
    /// text changes (except undo/redo) so that the user 
    /// delegate can be called and the undo buffer maintained.
    /// RETURNS: The end of the replaced text
    /// </summary>
    public TokenLoc ReplaceText(string[] ?replacementText,
                     TokenLoc start, TokenLoc end)
    {
        TokenLoc.FixOrder(ref start, ref end);

        bool hasInsert = replacementText != null && replacementText.Length != 0;
        bool hasDelete = start != end;

        // Exit if no change
        if (!hasInsert && !hasDelete)
            return end;

        // Allow user code to cancel read only mode
        if (_readOnly)
        {
            // Call user delegate, exit if still in ReadOnly mode
            BlockedByReadOnly?.Invoke(this, _eventArgs);
            if (_readOnly)
                return end;
        }

        // Save UNDO
        UndoOp undo = GetDefaultUndo();
        undo.Text = _lexer.GetText(start, end);
        undo.TextStart = start;

        // Replace text
        end = _lexer.ReplaceText(replacementText, start, end);
        undo.TextEnd = end;

        bool modified = Modified;
        _modifyTotal++;
        _modifyCount = _modifyTotal;
        if (Modified != modified)
            ModifiedChanged?.Invoke(this, EventArgs.Empty);

        // If inserting just one char, try to append this undo operation 
        // to the previous one (group them in to one user operation)
        UndoOp? fundo = _undo.Count == 0 ? null : _undo[_undo.Count - 1];
        if (!hasDelete
            // Replacement text is exactly one char
            && replacementText != null
            && replacementText.Length == 1
            && replacementText[0].Length == 1
            // This undo deletes exactly one char
            && undo.TextStart.Y == undo.TextEnd.Y
            && undo.TextStart.X == undo.TextEnd.X - 1
            // Previous undo is a delete on this line (max 12 chars)
            && fundo != null
            && (fundo.Text == null || fundo.Text.Length == 0)
            && fundo.TextStart.Y == fundo.TextEnd.Y
            && fundo.TextEnd.X - fundo.TextStart.X <= 12
            // Previous undo and this one match up
            && fundo.TextStart.Y == undo.TextStart.Y
            && fundo.TextEnd.X == undo.TextEnd.X - 1)
        {
            // If inserting multiple single characters, group them
            fundo.TextEnd.X++;
        }
        // If deleting just one char, try to append this undo operation 
        // to the previous one (group them in to one user operation)
        else if (hasDelete
            // Replacement text is empty
            && (replacementText == null || replacementText.Length == 0)
            // This undo adds exactly one char
            && undo.TextStart.Y == undo.TextEnd.Y
            && undo.TextStart.X == undo.TextEnd.X
            && undo.Text != null
            && undo.Text.Length == 1
            && undo.Text[0].Length == 1
            // Previous adds no more than 12 chars
            && fundo != null
            && fundo.Text != null
            && fundo.Text.Length == 1
            && fundo.Text[0].Length < 12
            && fundo.TextStart.Y == fundo.TextEnd.Y
            && fundo.TextEnd.X == fundo.TextStart.X
            && fundo.TextStart.Y == undo.TextStart.Y
            // Previous undo and this one match up
            && fundo.TextStart.Y == undo.TextStart.Y
            && (fundo.TextEnd.X - undo.TextEnd.X == 1
                || fundo.TextEnd.X == undo.TextEnd.X))
        {
            if (fundo.TextEnd.X == undo.TextEnd.X)
            {
                // Delete key
                fundo.Text[0] += undo.Text[0];
            }
            else
            {
                // Back space
                fundo.Text[0] = undo.Text[0] + fundo.Text[0];
                fundo.TextStart = undo.TextStart;
                fundo.TextEnd = undo.TextEnd;
            }
        }
        else
        {
            // Save a new undo operation
            _undo.Add(undo);
        }
        mRedo.Clear();

        // Call user delegate
        OnTextChangedInternal();
        return end;
    }


    /// <summary>
    /// Undo/redo the previously saved operation
    /// </summary>
    void Undo(List<UndoOp> undoList, List<UndoOp> redoList)
    {
        // Get undo op
        if (undoList.Count == 0)
            return;

        // Allow user to cancel read only mode
        if (_readOnly)
        {
            // Call user delegate, exit if still in ReadOnly mode
            BlockedByReadOnly?.Invoke(this, _eventArgs);
            if (_readOnly)
                return;
        }

        // Get undo/redo operation
        UndoOp undo = undoList[undoList.Count - 1];
        undoList.RemoveAt(undoList.Count - 1);

        // Perform undo/redo operation
        UndoOp redo = GetDefaultUndo();
        redo.Text = _lexer.GetText(undo.TextStart, undo.TextEnd);
        redo.TextStart = undo.TextStart;
        redo.TextEnd = _lexer.ReplaceText(undo.Text, undo.TextStart, undo.TextEnd);
        redoList.Add(redo);

        // Move cursor and selection
        _selStart = undo.SelStart;
        _selEnd = undo.SelEnd;
        CursorLoc = undo.Cursor;

        // Set top of screen
        _vScrollBar.Value = Math.Max(0, Math.Min(_vScrollBar.Maximum, undo.TopIndex));
        _hScrollBar.Value = Math.Max(0, Math.Min(_hScrollBar.Maximum, undo.LeftIndex));

        var modified = Modified;
        _modifyCount = undo.ModifyCount;
        if (modified != Modified)
            ModifiedChanged?.Invoke(this, EventArgs.Empty);

        // Call user delegate
        OnTextChangedInternal();
    }

    /// <summary>
    /// Gets/sets the lexer to hold and lex the text. When set, if the text
    /// is different, the undo-redo is deleted.  Always TextEditor.ReplaceText
    /// (not Lexer.ReplaceText) so undo is handled.
    /// </summary>
    public Lexer Lexer
    {
        get
        {
            _lexer.Cursor = _cursorLoc;
            return _lexer;
        }
        set
        {
            bool eq = _lexer.Equals(value);
            _lexer = value;
            if (!eq)
            {
                _undo.Clear(); // TBD: Fix undo/redo
                mRedo.Clear();
                FixCursorLocation(ref _cursorLoc);
                _lexer.Cursor = _cursorLoc;
                OnTextChangedInternal();
            }
            InvalidateVisual();
            LexerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Returns the start of selected text
    /// </summary>
    public TokenLoc SelStart
    {
        get
        {
            return _selStart;
        }
    }

    /// <summary>
    /// Returns the end of selected text
    /// </summary>
    public TokenLoc SelEnd
    {
        get
        {
            return _selEnd;
        }
    }

    /// <summary>
    /// Returns TRUE if this control has selected text
    /// </summary>
    public bool HasSel()
    {
        return _selStart != _selEnd;
    }

    /// <summary>
    /// Clear the seleced text
    /// </summary>
    public void SelClear(bool left)
    {
        if (HasSel())
        {
            if (left)
                CursorLoc = _selStart;
            else
                CursorLoc = _selEnd;
        }
        _selStart = new TokenLoc();
        _selEnd = new TokenLoc();
    }

    /// <summary>
    /// Sets the selected text.  Cursor is set to the end of the
    /// selected text and is forced to be on screen.
    /// </summary>
    public void SelSet(TokenLoc selStart, TokenLoc selEnd)
    {
        FixCursorLocation(ref selStart);
        FixCursorLocation(ref selEnd);

        _selStart = selStart;
        _selEnd = selEnd;
        _cursorLoc = selEnd;
        _lexer.Cursor = _cursorLoc;

        EnsureCursorOnScreen();
        UpdateCursorBlinker();
        InvalidateVisual();
    }

    /// <summary>
    /// If there is any selected text, delete it and return TRUE.
    /// If there is not any selected text, return FALSE.
    /// </summary>
    bool SelDel()
    {
        if (HasSel())
        {
            ReplaceText(null, _selStart, _selEnd);
            SelClear(true);
            return true;
        }
        return false;
    }

    private TokenColorOverride? FindColorOverride(Token token)
    {
        foreach (var over in _tokenColorOverrides)
            if (over.Token == token)
                return over;
        return null;
    }

    /// <summary>
    /// Render the screen
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var timer = Stopwatch.StartNew();
        
        // NOTE: Must draw over the entire control, otherwise mouse hit-tests don't work
        context.DrawRectangle(new SolidColorBrush(Colors.Transparent), null, new(new(0,0), _clientSize));

        DrawTokens(context, true);
        DrawSelection(context);
        DrawTokens(context, false);
        DrawLines(context);
        DrawCursor(context);
        DrawVerticalMarks(context);

        base.Render(context);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Take all the space given (within reason)
        _clientSize = availableSize.Constrain(new(10000, 10000));

        MeasureFont();
        RecalcLineTops();
        SetupScrollBars();
        SetTopLeft((int)_hScrollBar.Value, (int)_vScrollBar.Value);
        UpdateCursorBlinker();
        UpdateMouseHoverToken();

        // For now, just take all the space we can get, so the user
        // is completely responsible for controlling the size.
        // TBD: We could act like a TextBox and expand accordingly.
        // _clientSize = availableSize.Constrain(new(double.PositiveInfinity, mLineTops[^1]));

        return _clientSize;
    }

    private void MeasureFont()
    {
        // Measure the font size
        var normalFont = GetFontInfo(s_normalToken).Font;
        var formattedText1 = new FormattedText("MM\nMM", CultureInfo.InvariantCulture, 
            FlowDirection.LeftToRight, normalFont, FontSize, Brushes.Black);
        var formattedText2 = new FormattedText("MMM\nMMM\nMMM", CultureInfo.InvariantCulture, 
            FlowDirection.LeftToRight, normalFont, FontSize, Brushes.Black);

        var size1 = new Size(formattedText1.Width, formattedText1.Height);
        var size2 = new Size(formattedText2.Width, formattedText2.Height);
        var width = Math.Max(1, size2.Width - size1.Width);
        var height = Math.Max(1, (int)(size2.Height - size1.Height + 1 + 0.5f));
        _fontSize = new Size(width, height);
        for (int i = 0; i < _tabSpacing.Length; i++)
            _tabSpacing[i] = _fontSize.Width * _tabSize;
        _tabSpacing[0] = 0;
    }

    /// <summary>
    /// Draw tokens on the screen (either the foreground or background).
    /// When gr is NULL, a test is performed to see if the token is
    /// under the cursor (_testPoint) and the result is put in to _testToken.
    /// </summary>
    void DrawTokens(DrawingContext? context, bool background)
    {
        GetTextClipRegion(context, out var startLine, out var endLine);

        // Draw metatokens
        foreach (var metaToken in _lexer.MetaTokens)
            if (metaToken.Y >= startLine && metaToken.Y <= endLine)
                if (background || ShowMetaTokens)
                    DrawToken(context, metaToken, background);

        // Draw all tokens on the screen
        for (int y = startLine; y < _lexer.LineCount && y <= endLine; y++)
        {
            var line = _lexer.GetLineTokens(y);
            for (int x = 0; x < line.Length; x++)
            {
                var token = line[x];
                if (PointX(token.X) > _clientSize.Width)
                    break;  // Ignore tabs here which can only push text out
                DrawToken(context, token, background);
            }
        }
    }

    private void GetTextClipRegion(DrawingContext? context, out int startLine, out int endLine)
    {
        // Get clipping region (entire screen when gr is NULL)
        var minY = -_fontSize.Height - 1;
        var maxY = _clientSize.Height + 1;
        if (context != null)
        {
            // Optionally adjust clipping region
            // TBD: Port to Avalonia
            //minY = gr.VisibleClipBounds.Top - mFontSize.Height - 1;
            //maxY = gr.VisibleClipBounds.Bottom + 1;
        }

        // Find first and last visible line
        startLine = TopVisibleLine;
        while (startLine < LineCount && PointY(startLine) < minY)
            startLine++;
        endLine = startLine;
        while (endLine <= LineCount && PointY(endLine) < maxY)
            endLine++;
    }

    /// <summary>
    /// Print a token (either the foreground or background)
    /// Sets _testToken if a token is under _testPoint
    /// </summary>
    void DrawToken(DrawingContext? context, Token token, bool background)
    {
        // Find token position and bounds
        int col = IndexToCol(token.Location);
        var x = PointX(col);
        var y = PointY(token.Y);
        if (x > _clientSize.Width || y > _clientSize.Height)
            return; // Off screen

        int tokenLength = token.Name.Length == 0 ? 4 : token.Name.Length; // Give EOF some width
        var xEnd = PointX(col + tokenLength);
        var yEnd = PointY(token.Y + 1);
        if (xEnd < 0 || yEnd < 0)
            return; // Off screen

        // Check if _testToken is under _testPoint, keep first hit which is the meta token
        if (_testToken == null && _testPoint != null
            && _testPoint.Value.X >= x && _testPoint.Value.X < xEnd
            && _testPoint.Value.Y >= y && _testPoint.Value.Y < yEnd)
        {
            _testToken = token;
        }
        if (context == null)
            return;

        // Shift meta tokens
        if (token.Meta)
        {
            x += 2;
            y -= 4;
        }

        // Print the token
        var backRect = new Rect((int)(x - 1) + FILL_X_OFFSET, (int)y, (int)(xEnd - x + 1), (int)(yEnd - y));
        var overrides = FindColorOverride(token);

        if (background)
        {
            // Draw background color
            if (overrides != null && overrides.BackColor != null)
            {
                context.FillRectangle(overrides.BackColor, backRect);
            }
            else
            {
                // TBD: This should be looked up in GetFontInfo based on Type and Subtype
                if (token.Subtype == TokenSubType.Error)
                    context.FillRectangle(_errorColor, backRect);
                else if (token.Subtype == TokenSubType.Warn)
                    context.FillRectangle(_warnColor, backRect);
                else if (token.Subtype == TokenSubType.CodeInComment)
                    context.FillRectangle(_codeInCommentColor, backRect);
            }
            return;
        }

        // Font color & underline
        var fontInfo = GetFontInfo(token);
        var font = fontInfo.Font;
        var brush = fontInfo.Brush;
        var decorations = token.Underline ? s_underline : null;
        if (overrides != null)
        {
            if (overrides.Font != null)
                font = overrides.Font.Value;
            if (overrides.ForeColor != null)
                brush = overrides.ForeColor;
            if (overrides.Decorations != null)
                decorations = overrides.Decorations;
        }

        if (token.Meta)
            brush = new SolidColorBrush(Colors.DimGray);

        if (token.Y >= 0 && token.Y < _lineShrunk.Length && _lineShrunk[token.Y])
        {
            // Draw shrunk text
            x = (int)(x + _fontSize.Width * SHRUNK_FONT_OFFSET.X);
            y = (int)(y + _fontSize.Height * SHRUNK_FONT_OFFSET.Y);
            DrawString(context, token.Name, _shrunkFont, brush, x, y, decorations);
        }
        else
        {
            // Draw normal text
            DrawString(context, token.Name, font, brush, x, y, decorations);
        }
        // Draw outline
        if (overrides != null && overrides.OutlineColor != null)
            context.DrawRectangle(overrides.OutlineColor, backRect);
    }

    void DrawString(DrawingContext contex, string text, Typeface font, Brush brush, 
        double x, double y, TextDecorationCollection ?decorations = null)
    {
        var formattedText = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, font, FontSize, brush);
        if (decorations != null)
            formattedText.SetTextDecorations(decorations);
        contex.DrawText(formattedText, new(x, y));
    }

    /// <summary>
    /// Draw the selection background
    /// </summary>
    void DrawSelection(DrawingContext context)
    {
        if (!HasSel())
            return;

        // Get selection start/end
        TokenLoc selStart = _selStart;
        TokenLoc selEnd = _selEnd;
        TokenLoc.FixOrder(ref selStart, ref selEnd);

        var mMinY = -_fontSize.Height;
        for (int line = Math.Max(0, selStart.Y);
             line <= selEnd.Y && line < LineCount;
             line++)
        {
            // Skip lines not in window
            var y = PointY(line);
            if (y < mMinY)
                continue;
            if (y > _clientSize.Height)
                break;

            // Get default start and end of draw location
            var x = 0.0;
            var xEnd = _fontSize.Width;

            // Middle line xEnd
            if (GetLine(line).Length != 0)
                xEnd = IndexToCol(GetLine(line), GetLine(line).Length) * _fontSize.Width;

            // Start/end lines
            if (line == selStart.Y)
                x = PointX(IndexToCol(selStart));
            if (line == selEnd.Y)
                xEnd = PointX(IndexToCol(selEnd));

            context.FillRectangle(IsFocused ? _selectColor : _selectColorNoFocus,
                new Rect(x + FILL_X_OFFSET, y, Math.Max(0, xEnd - x), (int)(PointY(line + 1) - y)));
        }
    }

    void DrawLines(DrawingContext context)
    {
        // Draw continuation lines
        GetTextClipRegion(context, out var startLine, out var endLine);
        for (int i = 0; i < _lexer.LineCount; i++)
        {
            var line = _lexer.GetLineTokens(i);
            if (line.Length == 0)
                continue;

            // Draw continuation marks
            var token = line[0];
            if (i >= startLine && i < endLine && token.Continuation)
            {
                var col = IndexToCol(line[0].Location);
                var x = (int)PointX(col - 1) - 2;
                var y = (int)PointY(i);
                DrawString(context, "►", GetFontInfo(s_normalToken).Font, _continuationBrush, x, y);
            }

            // Draw vertical lines
            if (token.VerticalLine && token.Y < endLine)
            {
                var verticleLine = token.GetInfo<TokenVerticalLine>();
                if (verticleLine != null
                    && verticleLine.Y + verticleLine.Lines >= startLine
                    && verticleLine.Lines > 0)
                {
                    var col = IndexToCol(line[0].Location);
                    var x = (int)PointX(col) + _fontSize.Width / 2;
                    var y = (int)PointY(verticleLine.Y) + 2;
                    var y2 = (int)PointY(verticleLine.Y + verticleLine.Lines) - 8;
                    context.DrawLine(verticleLine.Error ? _scopeLineErrorPen : _scopeLinePen, new(x, y), new(x, y2));
                }
            }

        }
    }

    private void DrawCursor(DrawingContext context)
    {
        if (!_cursorVisible || !IsFocused)
            return;

        // Draw the cursor
        context.FillRectangle(Brushes.DarkBlue, _cursorRect);

        // Draw text under cursor in over-write mode
        if (_overwriteMode && !HasSel()
                && CursorLoc.Y < LineCount
                && CursorLoc.X >= 0
                && CursorLoc.X < GetLine(CursorLoc.Y).Length)
        {
            var x = PointX(IndexToCol(CursorLoc));
            var y = PointY(CursorLoc.Y);
            DrawString(context, GetLine(CursorLoc.Y)[CursorLoc.X].ToString(),
                GetFontInfo(s_normalToken).Font, _textUnderOverwriteCursor, x, y);
        }
    }

    void DrawVerticalMarks(DrawingContext context)
    {
        foreach (var mark in _verticalMarks)
            DrawMark(mark.Start, mark.Color);

        if (_cursorVisible)
            DrawMark(CursorLoc.Y, Colors.Blue);

        return;

        void DrawMark(int line, Color color)
        {
            var brush = new SolidColorBrush(color);
            var pos = LineToVerticalPixel(line);
            context.FillRectangle(brush, new(_clientSize.Width - 12, pos, 6, 3));
        }

        double LineToVerticalPixel(int line)
        {
            const double ARROW_HEIGHT = 16; // Height of scroll bar arrows
            return line / (double)Math.Max(1, LineCount) * (_clientSize.Height - 2 * ARROW_HEIGHT) + ARROW_HEIGHT;
        }

    }



    /// <summary>
    /// Calculate locations of lines whenever the text or font changes
    /// </summary>
    void RecalcLineTops()
    {
        if (!_shrinkLines)
        {
            _lineTops = Array.Empty<int>();
            _lineShrunk = Array.Empty<bool>();
            return;
        }
        // Resize array if necessary
        if (_lineTops.Length != _lexer.LineCount + 1)
        {
            _lineTops = new int[_lexer.LineCount + 1];
            _lineShrunk = new bool[_lexer.LineCount + 1];
        }
        // Measure lines
        int top = 0;
        int index = 0;
        var e = _lexer.GetEnumerator();
        while (e.MoveNextLine())
        {
            if (e.CurrentLineTokenCount == 0)
            {
                // Empty space
                _lineShrunk[index] = true;
                _lineTops[index++] = top;
                top += (int)(_fontSize.Height * SHRUNK_EMPTY_LINE_SCALE);
            }
            else if (e.CurrentLineTokenCount == 1 // && e.Current.Shrink) *** TBD: See comment below
                        && (e.Current.Name == "{" || e.Current.Name == "}"))
            {
                // *** TBD: Shrink should come from the token, not be hard coded here.
                // Problem is that a change un-shrinks the symbol, then it gets re-shrunk
                // during parsing.  There is a delay and it becomes overly annoying when
                // lines shrink and un-shrink.  So leave this for now and fix later.

                // Shrink entire line only if there is one symbol on line
                _lineShrunk[index] = true;
                _lineTops[index++] = top;
                top += (int)(_fontSize.Height * SHRUNK_TEXT_LINE_SCALE);
            }
            else
            {
                // Normal text
                _lineShrunk[index] = false;
                _lineTops[index++] = top;
                top += (int)(_fontSize.Height);
            }
        }
        _lineTops[index] = top;
        _lineShrunk[index] = false;
    }



    /// <summary>
    /// Scroll screen for mouse wheel event
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Delta.Y > 0)
            {
                // TBD: Port to Avalonia
                if (FontSize < 64)
                    FontSize = FontSize * 1.1f;
            }
            else
            {
                // TBD: Port to Avalonia
                if (FontSize > 4)
                    FontSize = FontSize / 1.1f;
            }
        }
        else
        {
            if (e.Delta.Y > 0)
                _vScrollBar.Value = Math.Max(0, _vScrollBar.Value - 3);
            else
                _vScrollBar.Value = Math.Min(_vScrollBar.Maximum, _vScrollBar.Value + 3);
        }
        InvalidateMeasure();
        InvalidateVisual();
    }


    /// <summary>
    /// Move cursor when user clicks
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        _mouseCurrentPosition = position;
        _mouseDownPosition = position;
        _mouseDownVerticalScroll = _vScrollBar.Value;
        if (e.ClickCount < 2)
        {
            // Single click
            // Set cursor location, remove selection
            SetCursorByMouse(position);
            _selStart = _selEnd = CursorLoc;
            _mouseDownSelect = PointerOverText(position);
            _mouseDownDrag = !_mouseDownSelect;
            UpdateCursorBlinker();
            UpdateMouseHoverToken();
        }
        else
        {
            // Double click
            // Select a single token when the user double clicks
            _mouseDownSelect = false;
            _mouseDownDrag = false;
            InvalidateVisual();
            SetCursorByMouse(position);

            if (_mouseHoverToken != null)
            {
                // Set selected text (and cursor)
                _selStart = _selEnd = CursorLoc;
                _selStart.X = _mouseHoverToken.X;
                _selEnd.X = _mouseHoverToken.X + _mouseHoverToken.Name.Length;
                CursorLoc = new TokenLoc(_selEnd.X, CursorLoc.Y);
            }
            UpdateCursorBlinker();
        }

        base.OnPointerPressed(e);
        InvalidateVisual();
    }

    /// <summary>
    /// Update the hover token or selected text when the mouse moves
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        _mouseCurrentPosition = position;
        UpdateMouseHoverToken();

        // If mouse button is down, move selection
        if (_mouseDownSelect)
        {
            SetCursorByMouse(position);
            _selEnd = CursorLoc;
            _delayedInvalidate = true;
        }

        if (_mouseDownDrag)
        {
            // TBD: Use some built in gesture that can add velocity after release
            var v = _mouseDownVerticalScroll + (_mouseDownPosition.Y - position.Y) / (_fontSize.Height/2);
            _vScrollBar.Value = Math.Clamp(v, 0, _vScrollBar.Maximum);
        }    

        base.OnPointerMoved(e);
    }

    /// <summary>
    /// Done selecting text
    /// </summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        _mouseCurrentPosition = position;
        _mouseDownSelect = false;
        _mouseDownDrag = false;
        TokenLoc.FixOrder(ref _selStart, ref _selEnd);
        base.OnPointerReleased(e);
    }

    /// <summary>
    /// Hover token is cleared when mouse leaves the window
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        _mouseCurrentPosition = null;
        if (_mouseHoverToken != null)
        {
            Token previousToken = _mouseHoverToken;
            _mouseHoverToken = null;
            MouseHoverTokenChanged?.Invoke(this, previousToken, _mouseHoverToken);
            InvalidateVisual();
        }
        base.OnPointerExited(e);
    }

    /// <summary>
    /// Set the cursor to the mouse location
    /// </summary>
    void SetCursorByMouse(Point position)
    {
        // Ensure we can't go more than one line above/below the window
        var y = Math.Max(position.Y, -(int)_fontSize.Height);
        y = Math.Min(y, _clientSize.Height);// + (int)mFontSize.Height);

        // Set cursor according to line
        var cursor = CursorLoc;
        var text = ScreenToText(position.X, y);
        cursor.Y = (int)text.Y;
        cursor.Y = Math.Min(cursor.Y, LineCount - 1);
        cursor.Y = Math.Max(cursor.Y, 0);
        cursor.X = ColToIndex(GetLine(cursor.Y), (int)text.X);
        CursorLoc = cursor;
        UpdateCursorBlinker();
    }

    /// <summary>
    /// Update the mouse hover token and set mouse cursor.
    /// Send event when the hover token changes.
    /// </summary>
    private void UpdateMouseHoverToken(bool forceEvent = false)
    {
        if (_cursorOverride != null)
            Cursor = _cursorOverride;
        else if (_mouseCurrentPosition != null && PointerOverText(_mouseCurrentPosition.Value))
            Cursor = _beamCursor;
        else
            Cursor = _arrowCursor;

        // Find the token by drawing to NULL context and testing for _testPoint
        _testPoint = _mouseCurrentPosition;
        _testToken = null;
        DrawTokens(null, true);

        // Set new mouse hover token
        if (forceEvent || _testToken != _mouseHoverToken)
        {
            var previousToken = _mouseHoverToken;
            _mouseHoverToken = _testToken;
            MouseHoverTokenChanged?.Invoke(this, previousToken, _mouseHoverToken);
            InvalidateVisual();
        }
    }

    // Returns true when hovering over text (false when over blank space)
    bool PointerOverText(Point screenPoint)
    {
        var p = ScreenToText(screenPoint.X, screenPoint.Y);
        return !(p.Y >= 0 && p.Y < _lexer.LineCount
                && p.X > _lexer.GetLine((int)p.Y).Length + 4);
    }

    /// <summary>
    /// User scrolls text
    /// </summary>
    void ScrollBar_Changed()
    {
        SetTopLeft((int)_hScrollBar.Value, (int)_vScrollBar.Value);
        UpdateCursorBlinker();
        UpdateMouseHoverToken();
        InvalidateVisual();
    }

    private void _vScrollBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ScrollBar_Changed();
    }

    /// <summary>
    /// User scrolls text
    /// </summary>
    private void _hScrollBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ScrollBar_Changed();
    }
    
    /// <summary>
    /// Handle all control type keys
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Allow user event to intercept key strokes
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        // This handles many keys, but allow other controls to handle them as well
        if (e.Key == Key.Tab)
            e.Handled = true; // Prevent default tab to next control

        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl
            || e.Key == Key.LeftShift || e.Key == Key.RightShift
            || e.Key == Key.LeftAlt || e.Key == Key.RightAlt)
        { 
            return; 
        }
        
        var key = e.Key;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (!control && !alt)
            if (OnKeyText(e))
                return;

        // TBD: Port to Avalonia
        // Display search form
        //if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        //    FormSearchInstance.Show(ParentForm, this);
        //if (e.Key == Key.F3)
        //    FormSearchInstance.FindNext(ParentForm, this);

        var ensureCursorOnScreen = true;

        // ESC key
        if (key == Key.Escape)
        {
            _selStart = _selEnd = new TokenLoc();
            ensureCursorOnScreen = false;
        }

        // Handle DOWN arrow
        if (key == Key.Down)
            MoveCursor(ArrowKeyDown(CursorLoc, 1), false, shift);

        // Handle UP arrow
        if (key == Key.Up)
            MoveCursor(ArrowKeyDown(CursorLoc, -1), true, shift);

        // Handle RIGHT arrow
        if (key == Key.Right)
        {
            var cursor = CharIndexInc(CursorLoc);
            if (control)
            {
                // Move word right (max 32 chars)
                int i = 32;
                while (--i > 0 && cursor.X < GetLine(cursor.Y).Length
                        && char.IsWhiteSpace(GetLine(cursor.Y)[cursor.X]))
                    cursor = CharIndexInc(cursor);
                while (--i > 0 && cursor.X < GetLine(cursor.Y).Length
                        && char.IsLetterOrDigit(GetLine(cursor.Y)[cursor.X]))
                    cursor = CharIndexInc(cursor);
            }
            MoveCursor(cursor, false, shift);
            _cursorUpDownColumn = -1;
        }
        // Handle LEFT arrow
        if (key == Key.Left)
        {
            var cursor = CharIndexDec(CursorLoc);
            if (control)
            {
                // Move word left (max 32 chars)
                int i = 32;
                while (--i > 0 && cursor.X > 0
                        && char.IsWhiteSpace(GetLine(cursor.Y)[cursor.X - 1]))
                    cursor = CharIndexDec(cursor);
                while (--i > 0 && cursor.X > 0
                        && char.IsLetterOrDigit(GetLine(cursor.Y)[cursor.X - 1]))
                    cursor = CharIndexDec(cursor);
            }
            MoveCursor(cursor, true, shift);
            _cursorUpDownColumn = -1;
        }
        // HOME
        if (key == Key.Home)
        {
            // Find first non-white space
            var line = GetLine(CursorLoc.Y);
            var firstText = 0;
            while (firstText < GetLine(CursorLoc.Y).Length
                        && char.IsWhiteSpace(line, firstText))
                firstText++;

            // Go home, or to beginning of text
            var newCursor = CursorLoc;
            if (newCursor.X > firstText || newCursor.X == 0)
                newCursor.X = firstText;
            else
                newCursor.X = 0;

            // CTRL-HOME goes to beginning
            if (control)
                newCursor = new TokenLoc();
            MoveCursor(newCursor, true, shift);
            _cursorUpDownColumn = -1;
        }
        // END
        if (key == Key.End)
        {
            TokenLoc newCursor = new TokenLoc(0, CursorLoc.Y);
            if (control)
                newCursor = new TokenLoc(0, LineCount - 1);
            newCursor.X = GetLine(newCursor.Y).Length;
            MoveCursor(newCursor, true, shift);
            _cursorUpDownColumn = -1;
        }
        // PAGE UP
        if (key == Key.PageUp && !control)
        {
            var linesInWindow = LinesInWindow();
            _vScrollBar.Value = Math.Max(0, _vScrollBar.Value - linesInWindow);
            MoveCursor(ArrowKeyDown(CursorLoc, -linesInWindow), true, shift);
        }
        // PAGE DOWN
        if (key == Key.PageDown && !control)
        {
            var linesInWindow = LinesInWindow();
            _vScrollBar.Value = Math.Max(0, Math.Min(_vScrollBar.Maximum - linesInWindow, _vScrollBar.Value + linesInWindow));
            MoveCursor(ArrowKeyDown(CursorLoc, linesInWindow), false, shift);
        }
        // DELETE
        if (key == Key.Delete && !shift && !control)
        {
            if (!SelDel())
            {
                // Delete char
                TokenLoc inc = CharIndexInc(CursorLoc);
                ReplaceText(null, CursorLoc, inc);
            }
        }
        // BACK SPACE
        if (key == Key.Back && !shift && !control)
        {
            if (!SelDel())
            {
                // Delete BACK char
                TokenLoc dec = CharIndexDec(CursorLoc);
                ReplaceText(null, dec, CursorLoc);
                CursorLoc = dec;
            }
        }
        // CTRL-A (select all)
        if (key == Key.A && control)
        {
            CursorLoc = new TokenLoc(GetLine(LineCount - 1).Length, LineCount - 1);
            _selStart = new TokenLoc();
            _selEnd = CursorLoc;
            ensureCursorOnScreen = false;
        }

        // CTRL-Z: undo
        if (key == Key.Z && control && !shift)
            Undo(_undo, mRedo);

        // CTRL-Y or SHIFT_CTRL-Z: redo
        if (key == Key.Y && control
            || key == Key.Z && control && shift)
            Undo(mRedo, _undo);

        // CTRL-X: cut
        if (key == Key.X && control
            || key == Key.Delete && shift)
            Cut();

        // CTRL-C: copy
        if (key == Key.C && control
            || key == Key.Insert && control)
        {
            Copy();
            ensureCursorOnScreen = false;
        }

        // CTRL-V - paste
        if (key == Key.V && control
            || key == Key.Insert && shift)
            Paste();

        if (key == Key.Insert && !control && !shift)
            OverwriteMode = !OverwriteMode;

        // '\t' with selection (without selection handled in OnKeyPress)
        if (key == Key.Tab && HasSel())
            TabWithSelection(shift);

        // Update the cursor and re-draw the screen
        if (ensureCursorOnScreen)
            EnsureCursorOnScreen();
        UpdateCursorBlinker();
        SetupScrollBars();
        InvalidateVisual();
    }

    /// <summary>
    /// Shift all selected lines to the right or left
    /// </summary>
    void TabWithSelection(bool shift)
    {
        if (!HasSel())
            return;

        // Get section of text to move
        var lines = new List<string>();
        for (int i = _selStart.Y; i <= _selEnd.Y; i++)
            lines.Add(GetLine(i));

        // Add or remove spaces
        for (int i = 0; i < lines.Count; i++)
        {
            // If there is nothing on the last line, don't bother moving
            if (i != 0 && i == lines.Count - 1 && _selEnd.X == 0)
                break;

            int moveCh = 0;
            string line = lines[i];
            if (!shift)
            {
                // Insert space
                line = " " + line;
                moveCh = 1;
            }
            else
            {
                // Remove space or tab
                if (line.StartsWith(" ") || line.StartsWith("\t"))
                {
                    line = line.Substring(1);
                    moveCh = -1;
                }
            }
            lines[i] = line;

            // Mover cursor and selection to match inserted/removed chars
            if (CursorLoc.Y == _selStart.Y + i)
                CursorLoc = new TokenLoc(Math.Max(0, CursorLoc.X + moveCh), CursorLoc.Y);
            if (_selStart.Y == _selStart.Y + i)
                _selStart.X = Math.Max(0, _selStart.X + moveCh);
            if (_selEnd.Y == _selStart.Y + i)
                _selEnd.X = Math.Max(0, _selEnd.X + moveCh);
        }

        ReplaceText(lines.ToArray(), new TokenLoc(0, _selStart.Y),
                                     new TokenLoc(GetLine(_selEnd.Y).Length, _selEnd.Y));
    }

    /// <summary>
    /// Handle normal text keys (including enter and tab).
    /// Special case: Ignore tab when there is a selection.
    /// Returns true if the key was processed.
    /// </summary>
    bool OnKeyText(KeyEventArgs e)
    {
        char keyChar;
        if (e.Key == Key.Enter)
            keyChar = '\r';
        else if (e.Key == Key.Tab)
            keyChar = '\t';
        else if (e.KeySymbol != null && e.KeySymbol.Length > 0)
            keyChar = e.KeySymbol[0];
        else
            return false;

        // NOTE: Special chars are handled in OnKeyDown
        if (!(keyChar >= ' ' && keyChar <= '~'
            || char.IsLetterOrDigit(keyChar)
            // NOTE: '\t' with selection is handled in OnKeyDown
            || keyChar == '\t' && !HasSel()
            || keyChar == '\r'))
            return false;

        // Setup to replace selection (or insert new char)
        var start = CursorLoc;
        var end = CursorLoc;

        if (OverwriteMode && !HasSel() && keyChar != '\r')
            end.X++;

        if (HasSel())
        {
            CursorLoc = _selStart;
            start = _selStart;
            end = _selEnd;
        }
        // Setup to insert the char (or '\r')
        string[] insert;
        if (keyChar == '\r')
        {
            // Insert ENTER (and space before cursor)
            string line = GetLine(CursorLoc.Y);
            if (RemoveWhiteSpaceAtEndOnEnter)
            {
                // Remove white space before, then after the cursor
                while (start.X > 0 && start.X - 1 < line.Length && char.IsWhiteSpace(line, start.X - 1))
                    start.X--;
                while (end.X < line.Length && char.IsWhiteSpace(line, end.X))
                    end.X++;
            }

            // Copy white space from line above
            int wsX = 0;
            while (wsX < line.Length && char.IsWhiteSpace(line, wsX))
                wsX++;

            string[] insertCR = new string[] { "", "" };
            insertCR[1] = line.Substring(0, wsX);
            insert = insertCR;
        }
        else if (keyChar == '\t' && _tabInsertsSpaces)
        {
            insert = _insertOneString;
            _insertOneString[0] = new string(' ', _tabSize - (IndexToCol(CursorLoc) % _tabSize));
        }
        else
        {
            // Insert a single char
            insert = _insertOneString;
            _insertOneString[0] = char.ToString(keyChar);
        }

        // Insert/replace the typed char
        CursorLoc = ReplaceText(insert, start, end);
        _cursorUpDownColumn = -1;
        _selStart = _selEnd = new TokenLoc();
        EnsureCursorOnScreen();
        UpdateCursorBlinker();
        SetupScrollBars();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Update the cursor and scroll while user is selecting text.
    /// </summary>
    private void _timer_Tick(object? sender, EventArgs e)
    {
        // Update cursor visibility
        bool cursorVisible = IsCursorVisible();
        if (cursorVisible != _cursorVisible)
        {
            _cursorVisible = cursorVisible;
            InvalidateVisual();
            // TBD: Port to Avalonia
            // vMarksLeft.ShowCursor = mCursorVisible;
        }

        // While selecting text, scroll the screen
        if (_mouseDownSelect && _mouseCurrentPosition != null)
        {
            int linesInWindow = LinesInWindow();
            if (CursorLoc.Y - _vScrollBar.Value > linesInWindow
                    && _vScrollBar.Value < _vScrollBar.Maximum - linesInWindow
                    && _mouseCurrentPosition.Value.Y > _clientSize.Height)
                _vScrollBar.Value++;

            if (CursorLoc.Y < _vScrollBar.Value
                    && _vScrollBar.Value > 0)
                _vScrollBar.Value--;
        }

        // Optionally invalidate
        if (_delayedInvalidate)
            InvalidateVisual();
        _delayedInvalidate = false;
        _timer.IsEnabled = IsVisible && IsFocused;
    }
    
    /// <summary>
    /// Class to keep track of undo info
    /// </summary>
    class UndoOp
    {
        public string[] Text = [];
        public int TopIndex;
        public int LeftIndex;
        public int ModifyCount;
        public TokenLoc TextStart;
        public TokenLoc TextEnd;
        public TokenLoc Cursor;
        public TokenLoc SelStart;
        public TokenLoc SelEnd;
    }

    /// <summary>
    /// Class to keep track of font foreground color
    /// </summary>
    class FontInfo
    {
        public Typeface Font;
        public Color Color;
        public Brush Brush;

        public FontInfo(Typeface font, Color color)
        {
            Font = font;
            Color = color;
            Brush = new SolidColorBrush(color);
        }
    }
}

/// <summary>
/// Show marks on the vertical scroll bar
/// </summary>
public struct VerticalMarkInfo
{
    public int Start;
    public int Length;
    public Color Color;
}

/// <summary>
/// Override the background color of a token (use with Editor.TokenColorOverrides)
/// </summary>
public class TokenColorOverride
{
    public Token Token;
    public Typeface? Font;
    public Brush? ForeColor;
    public Pen? OutlineColor;
    public Brush? BackColor;
    public TextDecorationCollection? Decorations;

    public TokenColorOverride(Token token)
    {
        Token = token;
    }

    public TokenColorOverride(Token token, Pen? outlineColor)
    {
        Token = token;
        OutlineColor = outlineColor;
    }
    public TokenColorOverride(Token token, Brush? backColor)
    {
        Token = token;
        BackColor = backColor;
    }
    public TokenColorOverride(Token token, Pen ?outlineColor, Brush ?backColor)
    {
        Token = token;
        OutlineColor = outlineColor;
        BackColor = backColor;
    }
}


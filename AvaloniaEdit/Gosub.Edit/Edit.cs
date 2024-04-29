using System.Text;
using System.Globalization;
using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;

using Gosub.Lex;

namespace Gosub.Edit;

/// <summary>
/// Avalonia editor control.
/// </summary>
public class Edit : TemplatedControl
{

    readonly float SHRUNK_EMPTY_LINE_SCALE = 0.99f;
    readonly float SHRUNK_TEXT_LINE_SCALE = 0.5f;
    readonly float SHRUNK_FONT_SCALE = 0.65f;
    readonly Point SHRUNK_FONT_OFFSET = new Point(0.2f, -0.12f); // Scaled by font size
    const int FILL_X_OFFSET = 3;
    const int LEFT_MARGIN = 0; // TBD: Change to variable and increase to add line numbers

    // TBD: From template?
    const string FONT_NAME = "Courier New";

    static Edit()
    {
        FocusableProperty.OverrideDefaultValue(typeof(Edit), true);
    }


    // Lexer and text
    Lexer mLexer = new();
    List<UndoOp> mUndo = new();
    List<UndoOp> mRedo = new();
    bool mReadOnly;
    bool mShrinkLines = true;
    int mModifySaved;
    int mModifyCount;
    int mModifyTotal;
    string mFilePath = "";

    // Tabs and character
    double[] mTabSpacing = new double[32];
    int mTabStartColumnPrevious = -1;
    string[] mInsertOneString = [""];
    int mTabSize = 4;
    bool mTabInsertsSpaces = true;

    // Mouse and drawing info
    bool mMouseDown;
    Point? mMousePosition;
    Token? mMouseHoverToken;
    Point mTopLeft;
    Point mTestPoint;
    Token? mTestToken;
    Size mFontSize = new Size(9, 19);
    bool mMeasureFont = true;
    bool mDelayedInvalidate;
    int[] mLineTops = Array.Empty<int>();
    bool[] mLineShrunk = Array.Empty<bool>();

    // Cursor info
    TokenLoc mCursorLoc;
    int mCursorUpDownColumn = -1;
    DateTime mCursorBaseTime;
    Rect mCursorRect;
    bool mCursorVisible;
    bool mOverwriteMode;
    bool mControlKeyDown;


    // Selection
    TokenLoc mSelStart;
    TokenLoc mSelEnd;

    // Fonts, colors, and misc.
    Dictionary<eTokenType, FontInfo> mTokenFonts = new Dictionary<eTokenType, FontInfo>();
    Dictionary<eTokenType, FontInfo> mTokenFontsBold = new Dictionary<eTokenType, FontInfo>();
    Dictionary<eTokenType, FontInfo> mTokenFontsGrayed = new Dictionary<eTokenType, FontInfo>();
    Dictionary<eTokenType, FontInfo> mTokenFontsUnderlined = new Dictionary<eTokenType, FontInfo>();
    Typeface mShrunkFont = new(FONT_NAME); // TBD: Remove
    Brush mSelectColor = new SolidColorBrush(new Color(255, 208, 208, 255));
    Brush mSelectColorNoFocus = new SolidColorBrush(new Color(255, 224, 224, 224));
    EventArgs mEventArgs = new EventArgs();
    Brush mErrorColor = new SolidColorBrush(Colors.Pink);
    Brush mWarnColor = new SolidColorBrush(Colors.Yellow);
    Brush mCodeInCommentColor = new SolidColorBrush(new Color(255, 208, 255, 208));
    Brush mContinuationBrush = new SolidColorBrush(Colors.LightGray);
    Brush mTextUnderOverwriteCursor = new SolidColorBrush(Colors.White);
    Pen mScopeLinePen = new Pen(Colors.LightGray.ToUInt32(), 1, new DashStyle([4,4], 0));
    Pen mScopeLineErrorPen = new Pen(Colors.Red.ToUInt32(), 1, new DashStyle([4, 4], 0));
    static Token sNormalToken = new Token();

    TokenColorOverride[] mTokenColorOverrides = [];


    // TBD: Port to Avalonia
    ScrollViewer _scrollViewer = new();
    public int TopVisibleLine => (int)_scrollViewer.Offset.Y;
    Rect ClientRectangle => new Rect(0, 0, Width, Height);
    bool hScrollBarVisible = true;
    int hScrollBarHeight = 10000;
    public Size FontSize => new Size((int)mFontSize.Width, (int)mFontSize.Height);
    int vScrollBarMaximum { get; set; }
    int vScrollBarLargeChange { get; set; }
    bool vScrollBarEnabled { get; set; }
    bool vScrollBarVisible { get; set; }
    int vScrollBarSmallChange { get; set; }
    int hScrollBarMaximum { get; set; }
    int hScrollBarLargeChange { get; set; }
    bool hScrollBarEnabled { get; set; }
    int hScrollBarSmallChange { get; set; }
    Point vScrollBarLocation { get; set; }
    double vScrollBarHeight { get; set; }
    Point hScrollBarLocation { get; set; }
    double hScrollBarWidth { get; set; }
    double vScrollBarWidth { get; set; }
    bool timer1Enabled = true;


    // TBD: Remove these (this is a byprodocut of porting to Avalonia)
    bool mInRender;
    void Invalidate() { if (!mInRender) InvalidateVisual(); }
    void Invalidate(Rect r) { if (!mInRender) InvalidateVisual(); }


    double vScrollBarValue;
    double hScrollBarValue;
    void MessageBox(string message) { }





    public bool RemoveWhiteSpaceAtEndOnEnter = true;

    // Internal quick access to mLexer
    int LineCount { get { return mLexer.LineCount; } }
    string GetLine(int line) { return mLexer.GetLine(line); }

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
    public event EventHandler? TextChanged2;

    /// <summary>
    /// Occurs when Modify changes
    /// </summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>
    /// Occurs when the file path is changed
    /// </summary>
    public event EventHandler? FilePathChanged;

    /// <summary>
    /// Occurs when the lexer is set (even if the object does not change)
    /// </summary>
    public event EventHandler? LexerChanged;

    public Edit()
    {
        mCursorBaseTime = DateTime.Now;
        mLexer.ReplaceText(["Hello world 1!", "Hello world 2!","","This is an editor"], new TokenLoc(), new TokenLoc());
    }

    /// <summary>
    /// File info from when file was last loaded or saved 
    /// (or null when not from file system via LoadFile/SaveFile)
    /// </summary>
    public FileInfo? FileInfo { get; set; }

    /// <summary>
    /// File path (or "" when not from file system via LoadFile/SaveFile)
    /// </summary>
    public string FilePath
    {
        get { return mFilePath; }
        set
        {
            if (mFilePath == value)
                return;
            mFilePath = value;
            FilePathChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Set this to false when the file is saved
    /// </summary>
    public bool Modified
    {
        get { return mModifyCount != mModifySaved; }
        set
        {
            if (value == Modified)
                return;
            mModifySaved = value ? -1 : mModifyCount;
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// TBD: Copied from Avalonia.Controls.TextBox
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        //_presenter = e.NameScope.Get<TextPresenter>("PART_TextPresenter");
        //_scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        //_imClient.SetPresenter(_presenter, this);
        //if (IsFocused)
        //{
        //    _presenter?.ShowCaret();
        //}
    }

    /// <summary>
    /// TBD: Copied from Avalonia.Controls.TextBox
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        //if (_presenter != null)
        //{
        //    if (IsFocused)
        //    {
        //        _presenter.ShowCaret();
        //    }
        //    _presenter.PropertyChanged += PresenterPropertyChanged;
        //}
    }

    /// <summary>
    /// TBD: Copied from Avalonia.Controls.TextBox
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        //if (_presenter != null)
        //{
        //    _presenter.HideCaret();
        //    _presenter.PropertyChanged -= PresenterPropertyChanged;
        //}
        //_imClient.SetPresenter(null, null);
    }



    public void LoadFile(string filePath)
    {
        mUndo.Clear(); // TBD: Should handle Undo
        mRedo.Clear();
        Lexer.Path = filePath;
        Lexer.Scan(File.ReadAllLines(filePath));
        FileInfo = new FileInfo(filePath);
        FileInfo.Refresh(); // This seems to be needed for some reason
        Modified = false;
        FilePath = filePath;
        OnTextChangedInternal();
    }

    public void SaveFile(string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        File.WriteAllLines(filePath, Lexer.GetText());
        FileInfo = new FileInfo(filePath);
        FileInfo.Refresh();
        Modified = false;
        FilePath = filePath;
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
            mLexer.ReplaceText(new string[] { value },
                new TokenLoc(0, 0),
                new TokenLoc(GetLine(LineCount - 1).Length, LineCount));
            OnTextChangedInternal();
            Invalidate();
        }
    }

    /// <summary>
    /// Called whenever the text is changed.  Calls the user delegate TextChanged2
    /// </summary>
    void OnTextChangedInternal()
    {
        RecalcLineTops();
        TextChanged2?.Invoke(this, mEventArgs);
        UpdateMouseHoverToken();
        Invalidate();
    }

    public void InvalidateAll()
    {
        RecalcLineTops();
        UpdateMouseHoverToken();
        Invalidate();
    }

    /// <summary>
    /// Read only mode - Do not allow user to change text
    /// </summary>
    public bool ReadOnly
    {
        get { return mReadOnly; }
        set
        {
            if (value == mReadOnly)
                return;
            mReadOnly = value;
            Invalidate();
        }
    }

    public bool ShrinkLines
    {
        get { return mShrinkLines; }
        set
        {
            if (value == mShrinkLines)
                return;
            mShrinkLines = value;
            RecalcLineTops();
            Invalidate();
        }
    }


    /// <summary>
    /// Show marks on the vertical scroll bar
    /// </summary>
    public void SetMarks(VerticalMarkInfo[] marks)
    {
        // TBD: Port to Avalonia
        // vMarksLeft.SetMarks(marks);
    }

    /// <summary>
    /// Overwrite mode
    /// </summary>
    public bool OverwriteMode
    {
        get { return mOverwriteMode; }
        set
        {
            if (mOverwriteMode == value)
                return;
            mOverwriteMode = value;
            UpdateCursorBlinker();
        }
    }

    /// <summary>
    /// Token the mouse is currenty hovering over
    /// </summary>
    public Token? MouseHoverToken
    {
        get { return mMouseHoverToken; }
    }

    /// <summary>
    /// Array of tokens to override the default coloring
    /// </summary>
    public TokenColorOverride[] TokenColorOverrides
    {
        get { return mTokenColorOverrides; }
        set
        {
            mTokenColorOverrides = value;
            Invalidate();
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
        get { return mCursorLoc; }
        set
        {
            FixCursorLocation(ref value);
            if (value == mCursorLoc)
                return;
            mLexer.Cursor = mCursorLoc;
            mCursorLoc = value;
            CursorLocChanged?.Invoke(this, mEventArgs);
            UpdateCursorBlinker();
        }
    }


    /// <summary>
    /// Returns the number of full lines in the window (partial lines don't count)
    /// </summary>
    public int LinesInWindow()
    {
        const int REMOVE_PARTIALS = 2; // Add 1 for bottom of line, then 1 more to remove partials
        var height = ClientRectangle.Height - (hScrollBarVisible ? hScrollBarHeight : 0);
        var lines = 0;
        while (PointY(TopVisibleLine + lines + REMOVE_PARTIALS) < height)
            lines++;

        return Math.Max(1, lines);
    }

    /// <summary>
    /// Returns the size
    /// </summary>
    int CharsAcrossWindow()
    {
        return Math.Max(0, (int)(ClientRectangle.Width / mFontSize.Width) - 1);
    }

    // Set the top left corner of the viewport
    void SetTopLeft(int col, int line)
    {
        mTopLeft = new Point((int)PointXAbs(col), (int)PointYAbs(line));
    }

    double PointXAbs(int col)
    {
        return col * mFontSize.Width;
    }

    double PointYAbs(int line)
    {
        if (line < 0 || mLineTops.Length == 0)
            return line * mFontSize.Height;
        if (line < mLineTops.Length)
            return mLineTops[line];
        return mLineTops[mLineTops.Length - 1] + (line - mLineTops.Length + 1) * mFontSize.Height;
    }

    /// <summary>
    /// Return X position in window, given the column number
    /// </summary>
    double PointX(int col)
    {
        return PointXAbs(col) - mTopLeft.X + LEFT_MARGIN;
    }

    /// <summary>
    /// Return Y position in window, given the line number
    /// </summary>
    double PointY(int line)
    {
        return PointYAbs(line) - mTopLeft.Y;
    }

    Point ScreenToText(double x, double y)
    {
        x += mTopLeft.X - LEFT_MARGIN;
        y += mTopLeft.Y;
        var pointX = (int)(x / mFontSize.Width + 0.5f);
        if (y < 0 || mLineTops.Length == 0)
            return new Point(pointX, (int)(y / mFontSize.Height));

        // This function isn't used often, so it's OK to be slow
        for (int line = 1; line < mLineTops.Length; line++)
            if (y < mLineTops[line])
                return new Point(pointX, line - 1);

        return new Point(pointX, (int)(mLineTops.Length - 1 + y / mFontSize.Height));
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
                col += mTabSize - col % mTabSize;
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
                col += mTabSize - col % mTabSize;
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
        if (mTokenFonts.Count == 0)
        {
            // TBD: Port font sizes
            //Font normalFont = new Font(Font, Font.Bold ? FontStyle.Bold : FontStyle.Regular);
            //Font boldFont = new Font(Font, FontStyle.Bold);
            //mShrunkFont = new Font(Font.Name, Font.Size * SHRUNK_FONT_SCALE, FontStyle.Bold);
            var normalFont = new Typeface(FONT_NAME, FontStyle.Normal, FontWeight.Medium);
            var boldFont = new Typeface(FONT_NAME, FontStyle.Normal, FontWeight.Bold);
            mShrunkFont = new Typeface(FONT_NAME, FontStyle.Normal, FontWeight.Normal);


            // TBD: These should come from a Json config file, and
            //      eTokenType should be an open ended index (i.e. integer)
            mTokenFonts = new Dictionary<eTokenType, FontInfo>()
            {
                { eTokenType.Normal, new FontInfo(normalFont, Colors.Black) },
                { eTokenType.Identifier, new FontInfo(normalFont, Colors.Black) },
                { eTokenType.Reserved, new FontInfo(normalFont, Colors.Blue) },
                { eTokenType.ReservedControl, new FontInfo(boldFont, Colors.Blue) },
                { eTokenType.ReservedVar, new FontInfo(boldFont, Colors.DarkBlue) },
                { eTokenType.ReservedType, new FontInfo(boldFont, new Color(255, 20,125,160)) },
                { eTokenType.Quote, new FontInfo(normalFont, Colors.Brown) },
                { eTokenType.Comment, new FontInfo(normalFont, Colors.Green) },
                { eTokenType.NewVarSymbol, new FontInfo(normalFont, Colors.Blue) },
                { eTokenType.DefineField, new FontInfo(boldFont, Colors.Black) },
                { eTokenType.DefineMethod, new FontInfo(boldFont, Colors.Black) },
                { eTokenType.DefineFunParam, new FontInfo(boldFont, Colors.Black) },
                { eTokenType.DefineTypeParam, new FontInfo(boldFont, Colors.Black) },
                { eTokenType.DefineLocal, new FontInfo(boldFont, Colors.Black) },
                { eTokenType.TypeName, new FontInfo(normalFont, new Color(255, 20,125,160)) },
                { eTokenType.BoldSymbol, new FontInfo(boldFont, Colors.Black) },
            };

            // Setup bold, underlined, and grayed fonts
            foreach (var font in mTokenFonts)
            {
                mTokenFontsBold[font.Key] = new FontInfo(new Typeface(
                    font.Value.Font.FontFamily
                    //, FontStyle.Bold | font.Value.Font.Style // TBD: Port
                    ), font.Value.Color);
                mTokenFontsUnderlined[font.Key] = new FontInfo(new Typeface(
                    font.Value.Font.FontFamily
                    //, FontStyle.Underline | font.Value.Font.Style// TBD: Port
                    ), font.Value.Color);
                mTokenFontsGrayed[font.Key] = new FontInfo(font.Value.Font,
                    Lerp(font.Value.Color, Colors.LightGray, 0.5f));
            }
        }
        // Font info: normal, bold, or grayed (only one can be selected for now)
        Dictionary<eTokenType, FontInfo> colorTable;
        if (token.Bold)
            colorTable = mTokenFontsBold;
        else if (token.Grayed)
            colorTable = mTokenFontsGrayed;
        else if (token.Underline)
            colorTable = mTokenFontsUnderlined;
        else
            colorTable = mTokenFonts;

        if (!colorTable.TryGetValue(token.Type, out var fontInfo))
            return colorTable[eTokenType.Normal];
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
        get { return mTabSize; }
        set
        {
            if (mTabSize == value)
                return;
            Invalidate();
            mMeasureFont = true;
            mTabStartColumnPrevious = -1;
        }
    }


    void SetupScrollBars()
    {
        // Vertical properties
        int linesInFile = LineCount;
        int linesInWindow = LinesInWindow();
        vScrollBarMaximum = linesInFile;
        vScrollBarLargeChange = linesInWindow;
        vScrollBarEnabled = linesInFile > linesInWindow;
        vScrollBarVisible = linesInFile > linesInWindow && linesInFile > 1;
        vScrollBarSmallChange = 1;

        // Horizontal properties
        int charsAccross = 0;
        int charsAcrossWindow = CharsAcrossWindow();
        for (int i = 0; i < LineCount; i++)
            charsAccross = Math.Max(charsAccross, IndexToCol(GetLine(i), GetLine(i).Length));
        hScrollBarMaximum = charsAccross;
        hScrollBarLargeChange = Math.Max(1, charsAcrossWindow);
        hScrollBarEnabled = charsAccross > charsAcrossWindow;
        hScrollBarVisible = charsAccross > charsAcrossWindow && charsAccross > 1;
        hScrollBarSmallChange = 1;

        // Location & Size
        vScrollBarLocation = new Point(Math.Max(0, ClientRectangle.Width - vScrollBarWidth), 0);
        vScrollBarHeight = Math.Max(0, ClientRectangle.Height);
        hScrollBarLocation = new Point(0, Math.Max(0, ClientRectangle.Height - hScrollBarHeight));
        hScrollBarWidth = Math.Max(0, ClientRectangle.Width - (vScrollBarVisible ? vScrollBarWidth : 0));

        // TBD: Port to Avalonia
        //vMarksLeft.Visible = vScrollBar.Visible;
        //vMarksLeft.ArrowHight = vScrollBar.Width;
        //vMarksLeft.Location = new Point(vScrollBar.Left - vMarksLeft.Width + 1, vScrollBar.Top);
        //vMarksLeft.Height = vScrollBar.Height;
        //vMarksLeft.CursorMark = CursorLoc.Y;
        //vMarksLeft.Maximum = linesInFile - 1;
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
        mMeasureFont = true;
        mTokenFonts.Clear();
        mTabStartColumnPrevious = -1;
        var vScrollWidth = vScrollBarWidth; // Preserve vScrollBar width which gets changed when font is changed
        //base.OnFontChanged(e);
        vScrollBarWidth = vScrollWidth;
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
        Debug.WriteLine("Edit got focus"); // TBD: Remove
        base.OnGotFocus(e);
        UpdateCursorBlinker();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        Debug.WriteLine("Edit lost focus"); // TBD: Remove
        UpdateCursorBlinker();
        base.OnLostFocus(e);
        Invalidate();
    }


    /// <summary>
    /// Returns TRUE if the cursor should be visible, based
    /// on the control visibility, focus, and cursor blink time.
    /// </summary>
    bool IsCursorVisible()
    {
        return IsVisible && IsFocused
                && (DateTime.Now - mCursorBaseTime).Milliseconds < 600;
    }


    /// <summary>
    /// Sets the cursor location, and resets the cursor blink timer.
    /// </summary>
    void UpdateCursorBlinker()
    {
        timer1Enabled = IsVisible && IsFocused;
        mCursorBaseTime = DateTime.Now;
        int column = IndexToCol(CursorLoc);

        var x = (int)PointX(column);
        var y = (int)PointY(CursorLoc.Y);
        Rect cursorRect = new Rect(x + 1 + (mOverwriteMode ? 2 : 0), y + 1,
                                             mOverwriteMode && !HasSel() ? (int)mFontSize.Width : 2,
                                            (int)(PointY(CursorLoc.Y + 1) - y) - 2);

        if (cursorRect != mCursorRect)
        {
            Invalidate(mCursorRect);
            Invalidate(cursorRect);
            mCursorRect = cursorRect;
        }
        bool visibile = IsCursorVisible();
        if (visibile != mCursorVisible)
        {
            Invalidate(mCursorRect);
            mCursorVisible = visibile;
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
        int oldArrowColumn = mCursorUpDownColumn;
        if (mCursorUpDownColumn < 0)
            mCursorUpDownColumn = IndexToCol(CursorLoc);

        if (oldArrowColumn >= 0)
            cursor.X = ColToIndex(GetLine(cursor.Y), oldArrowColumn);
        else
            cursor.X = ColToIndex(GetLine(cursor.Y), mCursorUpDownColumn);

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
                mSelStart = mSelEnd = CursorLoc;

            // Move cursor to new location
            TokenLoc oldCursor = CursorLoc;
            CursorLoc = newCursor;

            // Update text selction
            if (mSelStart == oldCursor)
                mSelStart = CursorLoc;
            else if (mSelEnd == oldCursor)
                mSelEnd = CursorLoc;
            TokenLoc.FixOrder(ref mSelStart, ref mSelEnd);
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
        if (CursorLoc.Y < vScrollBarValue + marginY)
            vScrollBarValue = Math.Max(0, CursorLoc.Y - marginY);
        if (CursorLoc.Y > vScrollBarValue + linesInWindow - marginY)
            vScrollBarValue = Math.Min(vScrollBarMaximum, CursorLoc.Y - linesInWindow + marginY);

        // Horizontal
        int charsAcrossWindow = CharsAcrossWindow();
        int marginX = Math.Min(4, Math.Max(0, charsAcrossWindow - 5));
        if (IndexToCol(CursorLoc) < hScrollBarValue + marginX)
            hScrollBarValue = Math.Max(0, IndexToCol(CursorLoc) - marginX);
        if (IndexToCol(CursorLoc) > hScrollBarValue + charsAcrossWindow - marginX)
            hScrollBarValue = Math.Min(hScrollBarMaximum, IndexToCol(CursorLoc) - charsAcrossWindow + marginX);
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
            string[] copy = mLexer.GetText(mSelStart, mSelEnd);
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
        undo.TopIndex = (int)vScrollBarValue;
        undo.LeftIndex = (int)hScrollBarValue;
        undo.ModifyCount = mModifyCount;
        undo.SelStart = mSelStart;
        undo.SelEnd = mSelEnd;
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
        if (mReadOnly)
        {
            // Call user delegate, exit if still in ReadOnly mode
            BlockedByReadOnly?.Invoke(this, mEventArgs);
            if (mReadOnly)
                return end;
        }

        // Save UNDO
        UndoOp undo = GetDefaultUndo();
        undo.Text = mLexer.GetText(start, end);
        undo.TextStart = start;

        // Replace text
        end = mLexer.ReplaceText(replacementText, start, end);
        undo.TextEnd = end;

        bool modified = Modified;
        mModifyTotal++;
        mModifyCount = mModifyTotal;
        if (Modified != modified)
            ModifiedChanged?.Invoke(this, EventArgs.Empty);

        // If inserting just one char, try to append this undo operation 
        // to the previous one (group them in to one user operation)
        UndoOp? fundo = mUndo.Count == 0 ? null : mUndo[mUndo.Count - 1];
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
            mUndo.Add(undo);
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
        if (mReadOnly)
        {
            // Call user delegate, exit if still in ReadOnly mode
            BlockedByReadOnly?.Invoke(this, mEventArgs);
            if (mReadOnly)
                return;
        }

        // Get undo/redo operation
        UndoOp undo = undoList[undoList.Count - 1];
        undoList.RemoveAt(undoList.Count - 1);

        // Perform undo/redo operation
        UndoOp redo = GetDefaultUndo();
        redo.Text = mLexer.GetText(undo.TextStart, undo.TextEnd);
        redo.TextStart = undo.TextStart;
        redo.TextEnd = mLexer.ReplaceText(undo.Text, undo.TextStart, undo.TextEnd);
        redoList.Add(redo);

        // Move cursor and selection
        mSelStart = undo.SelStart;
        mSelEnd = undo.SelEnd;
        CursorLoc = undo.Cursor;

        // Set top of screen
        vScrollBarValue = Math.Max(0, Math.Min(vScrollBarMaximum, undo.TopIndex));
        hScrollBarValue = Math.Max(0, Math.Min(hScrollBarMaximum, undo.LeftIndex));

        var modified = Modified;
        mModifyCount = undo.ModifyCount;
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
            mLexer.Cursor = mCursorLoc;
            return mLexer;
        }
        set
        {
            bool eq = mLexer.Equals(value);
            mLexer = value;
            if (!eq)
            {
                mUndo.Clear(); // TBD: Fix undo/redo
                mRedo.Clear();
                FixCursorLocation(ref mCursorLoc);
                mLexer.Cursor = mCursorLoc;
                OnTextChangedInternal();
            }
            Invalidate();
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
            return mSelStart;
        }
    }

    /// <summary>
    /// Returns the end of selected text
    /// </summary>
    public TokenLoc SelEnd
    {
        get
        {
            return mSelEnd;
        }
    }

    /// <summary>
    /// Returns TRUE if this control has selected text
    /// </summary>
    public bool HasSel()
    {
        return mSelStart != mSelEnd;
    }

    /// <summary>
    /// Clear the seleced text
    /// </summary>
    public void SelClear(bool left)
    {
        if (HasSel())
        {
            if (left)
                CursorLoc = mSelStart;
            else
                CursorLoc = mSelEnd;
        }
        mSelStart = new TokenLoc();
        mSelEnd = new TokenLoc();
    }

    /// <summary>
    /// Sets the selected text.  Cursor is set to the end of the
    /// selected text and is forced to be on screen.
    /// </summary>
    public void SelSet(TokenLoc selStart, TokenLoc selEnd)
    {
        FixCursorLocation(ref selStart);
        FixCursorLocation(ref selEnd);

        mSelStart = selStart;
        mSelEnd = selEnd;
        mCursorLoc = selEnd;
        mLexer.Cursor = mCursorLoc;

        EnsureCursorOnScreen();
        UpdateCursorBlinker();
        Invalidate();
    }

    /// <summary>
    /// If there is any selected text, delete it and return TRUE.
    /// If there is not any selected text, return FALSE.
    /// </summary>
    bool SelDel()
    {
        if (HasSel())
        {
            ReplaceText(null, mSelStart, mSelEnd);
            SelClear(true);
            return true;
        }
        return false;
    }

    private TokenColorOverride? FindColorOverride(Token token)
    {
        foreach (var over in mTokenColorOverrides)
            if (over.Token == token)
                return over;
        return null;
    }

    public override void Render(DrawingContext context)
    {
        var timer = Stopwatch.StartNew();
        mInRender = true;
        context.FillRectangle(new SolidColorBrush(Colors.White), ClientRectangle, 5);
        context.DrawRectangle(new Pen(IsFocused ? Brushes.Blue : Brushes.LightGray, 3), ClientRectangle, 5);
        OnPaint(context);
        base.Render(context);
        mInRender = false;
        Debug.WriteLine($"Render time {timer.ElapsedMilliseconds} ms");  // TBD: Remove
    }


    /// <summary>
    /// Paint the screen
    /// </summary>
    void OnPaint(DrawingContext context)
    {
        MeasureFont(context);
        DrawTokens(context, true);
        DrawSelection(context);
        DrawTokens(context, false);
        DrawLines(context);
        DrawCursor(context);

        // Set the scroll bar properties
        // TBD - Do only when text is changed
        SetupScrollBars();
    }

    private void MeasureFont(DrawingContext context)
    {
        if (!mMeasureFont)
            return;

        // Measure the font size
        const int EM_SIZE_TBD_PORT = 24;
        mMeasureFont = false;
        var normalFont = GetFontInfo(sNormalToken).Font;
        var formattedText1 = new FormattedText("MM\nMM", CultureInfo.InvariantCulture, 
            FlowDirection.LeftToRight, normalFont, EM_SIZE_TBD_PORT, Brushes.Black);
        var formattedText2 = new FormattedText("MMM\nMMM\nMMM", CultureInfo.InvariantCulture, 
            FlowDirection.LeftToRight, normalFont, EM_SIZE_TBD_PORT, Brushes.Black);

        var size1 = new Size(formattedText1.Width, formattedText1.Height);
        var size2 = new Size(formattedText2.Width, formattedText2.Height);
        var width = Math.Max(1, size2.Width - size1.Width);
        var height = Math.Max(1, (int)(size2.Height - size1.Height + 1 + 0.5f));
        mFontSize = new Size(width, height);
        for (int i = 0; i < mTabSpacing.Length; i++)
            mTabSpacing[i] = mFontSize.Width * mTabSize;
        mTabSpacing[0] = 0;
        RecalcLineTops();
        SetupScrollBars();
        SetTopLeft((int)hScrollBarValue, (int)vScrollBarValue);
        UpdateCursorBlinker();
        UpdateMouseHoverToken();
    }

    /// <summary>
    /// Draw tokens on the screen (either the foreground or background).
    /// When gr is NULL, a test is performed to see if the token is
    /// under the cursor (mTestPoint) and the result is put in to mTestToken.
    /// </summary>
    void DrawTokens(DrawingContext? context, bool background)
    {
        GetTextClipRegion(context, out var startLine, out var endLine);

        // Draw metatokens
        foreach (var metaToken in mLexer.MetaTokens)
            if (metaToken.Y >= startLine && metaToken.Y <= endLine)
                if (background || mLexer.ShowMetaTokens)
                    DrawToken(context, metaToken, background);

        // Draw all tokens on the screen
        for (int y = startLine; y < mLexer.LineCount && y <= endLine; y++)
        {
            var line = mLexer.GetLineTokens(y);
            for (int x = 0; x < line.Length; x++)
            {
                var token = line[x];
                if (PointX(token.X) > Width)
                    break;  // Ignore tabs here which can only push text out
                DrawToken(context, token, background);
            }
        }
    }

    private void GetTextClipRegion(DrawingContext? context, out int startLine, out int endLine)
    {
        // Get clipping region (entire screen when gr is NULL)
        var minY = -mFontSize.Height - 1;
        var maxY = ClientRectangle.Height + 1;
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
    /// Sets mTestToken if a token is under mTestPoint
    /// </summary>
    void DrawToken(DrawingContext? context, Token token, bool background)
    {
        // Find token position and bounds
        int col = IndexToCol(token.Location);
        var x = PointX(col);
        var y = PointY(token.Y);
        if (x > Width || y > Height)
            return; // Off screen

        int tokenLength = token.Name.Length == 0 ? 4 : token.Name.Length; // Give EOF some width
        var xEnd = PointX(col + tokenLength);
        var yEnd = PointY(token.Y + 1);
        if (xEnd < 0 || yEnd < 0)
            return; // Off screen

        // Check if mTestToken is under mTestPoint, keep first hit which is the meta token
        if (mTestToken == null
            && mTestPoint.X >= x && mTestPoint.X < xEnd
            && mTestPoint.Y >= y && mTestPoint.Y < yEnd)
        {
            mTestToken = token;
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
                if (token.Subtype == eTokenSubtype.Error)
                    context.FillRectangle(mErrorColor, backRect);
                else if (token.Subtype == eTokenSubtype.Warn)
                    context.FillRectangle(mWarnColor, backRect);
                else if (token.Subtype == eTokenSubtype.CodeInComment)
                    context.FillRectangle(mCodeInCommentColor, backRect);
            }
            return;
        }

        // Adjust tabs (TBD: Port to Avalonia, split tabs and draw correctly)
        int tabStartColumn = mTabSize - col % mTabSize;
        if (tabStartColumn != mTabStartColumnPrevious && token.Name.IndexOf('\t') >= 0)
        {
            mTabStartColumnPrevious = tabStartColumn;

            // TBD: Port to Avalonia to split strings and display tabbed text correctly
            //mTabFormat.SetTabStops(tabStartColumn * mFontSize.Width, mTabSpacing);
        }

        // Font color
        var fontInfo = GetFontInfo(token);
        var font = overrides != null && overrides.Font != null ? overrides.Font.Value : fontInfo.Font;
        var brush = overrides != null && overrides.ForeColor != null ? overrides.ForeColor : fontInfo.Brush;
        if (token.Meta)
            brush = new SolidColorBrush(Colors.DimGray);

        if (token.Y >= 0 && token.Y < mLineShrunk.Length && mLineShrunk[token.Y])
        {
            // Draw shrunk text
            x = (int)(x + mFontSize.Width * SHRUNK_FONT_OFFSET.X);
            y = (int)(y + mFontSize.Height * SHRUNK_FONT_OFFSET.Y);
            DrawString(context, token.Name, mShrunkFont, brush, x, y);
        }
        else
        {
            // Draw normal text
            DrawString(context, token.Name, font, brush, x, y);
        }
        // Draw outline
        if (overrides != null && overrides.OutlineColor != null)
            context.DrawRectangle(overrides.OutlineColor, backRect);
    }

    void DrawString(DrawingContext contex, string text, Typeface font, Brush brush, double x, double y)
    {
        const int EM_SIZE_TBD_PORT = 24;
        var formattedText = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, font, EM_SIZE_TBD_PORT, brush);
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
        TokenLoc selStart = mSelStart;
        TokenLoc selEnd = mSelEnd;
        TokenLoc.FixOrder(ref selStart, ref selEnd);

        var mMinY = -mFontSize.Height;
        for (int line = Math.Max(0, selStart.Y);
             line <= selEnd.Y && line < LineCount;
             line++)
        {
            // Skip lines not in window
            var y = PointY(line);
            if (y < mMinY)
                continue;
            if (y > ClientRectangle.Height)
                break;

            // Get default start and end of draw location
            var x = 0.0;
            var xEnd = mFontSize.Width;

            // Middle line xEnd
            if (GetLine(line).Length != 0)
                xEnd = IndexToCol(GetLine(line), GetLine(line).Length) * mFontSize.Width;

            // Start/end lines
            if (line == selStart.Y)
                x = PointX(IndexToCol(selStart));
            if (line == selEnd.Y)
                xEnd = PointX(IndexToCol(selEnd));

            context.FillRectangle(IsFocused ? mSelectColor : mSelectColorNoFocus,
                new Rect(x + FILL_X_OFFSET, y, Math.Max(0, xEnd - x), (int)(PointY(line + 1) - y)));
        }
    }

    void DrawLines(DrawingContext context)
    {
        // Draw continuation lines
        GetTextClipRegion(context, out var startLine, out var endLine);
        for (int i = 0; i < mLexer.LineCount; i++)
        {
            var line = mLexer.GetLineTokens(i);
            if (line.Length == 0)
                continue;

            // Draw continuation marks
            var token = line[0];
            if (i >= startLine && i < endLine && token.Continuation)
            {
                var col = IndexToCol(line[0].Location);
                var x = (int)PointX(col - 1) - 2;
                var y = (int)PointY(i);
                DrawString(context, "►", GetFontInfo(sNormalToken).Font, mContinuationBrush, x, y);
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
                    var x = (int)PointX(col) + mFontSize.Width / 2;
                    var y = (int)PointY(verticleLine.Y) + 2;
                    var y2 = (int)PointY(verticleLine.Y + verticleLine.Lines) - 8;
                    context.DrawLine(verticleLine.Error ? mScopeLineErrorPen : mScopeLinePen, new(x, y), new(x, y2));
                }
            }

        }
    }

    private void DrawCursor(DrawingContext context)
    {
        if (!mCursorVisible || !IsFocused)
            return;

        // Draw the cursor
        context.FillRectangle(Brushes.DarkBlue, mCursorRect);

        // Draw text under cursor in over-write mode
        if (mOverwriteMode && !HasSel()
                && CursorLoc.Y < LineCount
                && CursorLoc.X >= 0
                && CursorLoc.X < GetLine(CursorLoc.Y).Length)
        {
            var x = PointX(IndexToCol(CursorLoc));
            var y = PointY(CursorLoc.Y);
            DrawString(context, GetLine(CursorLoc.Y)[CursorLoc.X].ToString(),
                GetFontInfo(sNormalToken).Font, mTextUnderOverwriteCursor, x, y);
        }
    }


    /// <summary>
    /// Caluclate locations of lines whenever the text or font changes
    /// </summary>
    void RecalcLineTops()
    {
        // Font is changing
        if (mMeasureFont)
            return;

        if (!mShrinkLines)
        {
            mLineTops = Array.Empty<int>();
            mLineShrunk = Array.Empty<bool>();
            return;
        }
        // Resize array if necessary
        if (mLineTops.Length != mLexer.LineCount + 1)
        {
            mLineTops = new int[mLexer.LineCount + 1];
            mLineShrunk = new bool[mLexer.LineCount + 1];
        }
        // Measure lines
        int top = 0;
        int index = 0;
        var e = mLexer.GetEnumerator();
        while (e.MoveNextLine())
        {
            if (e.CurrentLineTokenCount == 0)
            {
                // Empty space
                mLineShrunk[index] = true;
                mLineTops[index++] = top;
                top += (int)(mFontSize.Height * SHRUNK_EMPTY_LINE_SCALE);
            }
            else if (e.CurrentLineTokenCount == 1 // && e.Current.Shrink) *** TBD: See comment below
                        && (e.Current.Name == "{" || e.Current.Name == "}"))
            {
                // *** TBD: Shrink should come from the token, not be hard coded here.
                // Problem is that a change un-shrinks the symbol, then it gets re-shrunk
                // during parsing.  There is a delay and it becomes overly annoying when
                // lines shrink and un-shrink.  So leave this for now and fix later.

                // Shrink entire line only if there is one symbol on line
                mLineShrunk[index] = true;
                mLineTops[index++] = top;
                top += (int)(mFontSize.Height * SHRUNK_TEXT_LINE_SCALE);
            }
            else
            {
                // Normal text
                mLineShrunk[index] = false;
                mLineTops[index++] = top;
                top += (int)(mFontSize.Height);
            }
        }
        mLineTops[index] = top;
        mLineShrunk[index] = false;
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
                //if (Font.Size < 16)
                //    Font = new Font(Font.FontFamily, Font.Size * 1.1f);
            }
            else
            {
                // TBD: Port to Avalonia
                //if (Font.Size > 8)
                //    Font = new Font(Font.FontFamily, Font.Size / 1.1f);
            }
        }
        else
        {
            if (e.Delta.Y > 0)
                vScrollBarValue = Math.Max(0, vScrollBarValue - 3);
            else
                vScrollBarValue = Math.Min(vScrollBarMaximum, vScrollBarValue + 3);
        }
    }


    /// <summary>
    /// Move cursor when user clicks
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Debug.WriteLine("OnPointerPressed"); // TBD: Remove

        var position = e.GetCurrentPoint(this).Position;
        mMousePosition = position;
        if (e.ClickCount < 2)
        {
            // Single click
            // Set cursor location, remove selection
            SetCursorByMouse(position);
            mSelStart = mSelEnd = CursorLoc;
            mMouseDown = true;
            UpdateCursorBlinker();
            UpdateMouseHoverToken();
        }
        else
        {
            // Double click
            // Select a single token when the user double clicks
            mMouseDown = false;
            Invalidate();
            SetCursorByMouse(position);

            if (mMouseHoverToken != null)
            {
                // Set selected text (and cursor)
                mSelStart = mSelEnd = CursorLoc;
                mSelStart.X = mMouseHoverToken.X;
                mSelEnd.X = mMouseHoverToken.X + mMouseHoverToken.Name.Length;
                CursorLoc = new TokenLoc(mSelEnd.X, CursorLoc.Y);
            }

            UpdateCursorBlinker();
        }

        base.OnPointerPressed(e);
        Invalidate();
    }

    /// <summary>
    /// Set the cursor to the mouse location
    /// </summary>
    void SetCursorByMouse(Point position)
    {
        // Ensure we can't go more than one line above/below the window
        var y = Math.Max(position.Y, -(int)mFontSize.Height);
        y = Math.Min(y, Height);// + (int)mFontSize.Height);

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
    /// Update the hover token or selected text when the mouse moves
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        mMousePosition = position;
        UpdateMouseHoverToken();

        // If mouse button is down, move selection
        if (mMouseDown)
        {
            SetCursorByMouse(position);
            mSelEnd = CursorLoc;

            // NOTE: Invalidating here can cause so much screen
            // updating that the screen doesn't scroll via
            // the timer.  Yet we need to invalidate if the
            // selection changes.  This fixes that problem.
            mDelayedInvalidate = true;
        }
        base.OnPointerMoved(e);
    }

    private void UpdateMouseHoverToken(bool forceEvent = false)
    {
        // TBD: Port to Avalonia (many places use null here)
        if (mMousePosition == null)
            return;

        // Draw to NULL graphics to find the point
        //mTestPoint = PointToClient(Form.MousePosition); // TBD: Port
        mTestPoint = mMousePosition.Value;
        mTestToken = null;
        DrawTokens(null, true);

        // Set new mouse hover token
        if (forceEvent || mTestToken != mMouseHoverToken)
        {
            var previousToken = mMouseHoverToken;
            mMouseHoverToken = mTestToken;
            MouseHoverTokenChanged?.Invoke(this, previousToken, mMouseHoverToken);
            Invalidate();
        }
    }


    /// <summary>
    /// Done selecting text
    /// </summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        mMousePosition = position;
        EnsureCursorOnScreen();
        mMouseDown = false;
        TokenLoc.FixOrder(ref mSelStart, ref mSelEnd);
        base.OnPointerReleased(e);
    }

    /// <summary>
    /// Hover token is cleared when mouse leaves the window
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        mMousePosition = null;
        if (mMouseHoverToken != null)
        {
            Token previousToken = mMouseHoverToken;
            mMouseHoverToken = null;
            MouseHoverTokenChanged?.Invoke(this, previousToken, mMouseHoverToken);
            Invalidate();
        }
        base.OnPointerExited(e);
    }

    /// <summary>
    /// User scrolls text
    /// </summary>
    void ScrollBar_Changed()
    {
        SetTopLeft((int)hScrollBarValue, (int)vScrollBarValue);
        UpdateCursorBlinker();
        UpdateMouseHoverToken();
        Invalidate();
    }

    /// <summary>
    /// User scrolls text
    /// </summary>
    private void vScrollBar_ValueChanged(object sender, EventArgs e)
    {
        ScrollBar_Changed();
    }

    /// <summary>
    /// User scrolls text
    /// </summary>
    private void hScrollBar_ValueChanged(object sender, EventArgs e)
    {
        ScrollBar_Changed();
    }


    protected override void OnKeyUp(KeyEventArgs e)
    {

        // SHIFT, CTRL, and ALT keys update the hover token
        // since the look may change when these keys are pressed
        var key = e.Key;
        if (key == Key.LeftShift || key == Key.RightShift
            || key == Key.LeftCtrl || key == Key.RightCtrl
            || key == Key.LeftAlt || key == Key.RightAlt)
        {
            if (mControlKeyDown)
                UpdateMouseHoverToken(true);
            mControlKeyDown = false;
        }
    }

    
    /// <summary>
    /// Handle all control type keys
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        Debug.WriteLine("Edit keydown"); // TBD: Remove


        // Allow user event to intercept key strokes
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        e.Handled = true;
        
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

        // SHIFT, CTRL, and ALT keys update the hover token
        // since the look may change when these keys are pressed
        if (key == Key.LeftShift || key == Key.RightShift
            || key == Key.LeftCtrl || key == Key.RightCtrl
            || key == Key.LeftAlt || key == Key.RightAlt)
        {
            if (!mControlKeyDown)
                UpdateMouseHoverToken(true);
            mControlKeyDown = true;
            return;
        }

        var ensureCursorOnScreen = true;

        // ESC key
        if (key == Key.Escape)
        {
            mSelStart = mSelEnd = new TokenLoc();
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
            mCursorUpDownColumn = -1;
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
            mCursorUpDownColumn = -1;
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
            mCursorUpDownColumn = -1;
        }
        // END
        if (key == Key.End)
        {
            TokenLoc newCursor = new TokenLoc(0, CursorLoc.Y);
            if (control)
                newCursor = new TokenLoc(0, LineCount - 1);
            newCursor.X = GetLine(newCursor.Y).Length;
            MoveCursor(newCursor, true, shift);
            mCursorUpDownColumn = -1;
        }
        // PAGE UP
        if (key == Key.PageUp && !control)
        {
            var linesInWindow = LinesInWindow();
            vScrollBarValue = Math.Max(0, vScrollBarValue - linesInWindow);
            MoveCursor(ArrowKeyDown(CursorLoc, -linesInWindow), true, shift);
        }
        // PAGE DOWN
        if (key == Key.PageDown && !control)
        {
            var linesInWindow = LinesInWindow();
            vScrollBarValue = Math.Max(0, Math.Min(vScrollBarMaximum - linesInWindow, vScrollBarValue + linesInWindow));
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
            mSelStart = new TokenLoc();
            mSelEnd = CursorLoc;
            ensureCursorOnScreen = false;
        }

        // CTRL-Z: undo
        if (key == Key.Z && control && !shift)
            Undo(mUndo, mRedo);

        // CTRL-Y or SHIFT_CTRL-Z: redo
        if (key == Key.Y && control
            || key == Key.Z && control && shift)
            Undo(mRedo, mUndo);

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
        Invalidate();
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
        for (int i = mSelStart.Y; i <= mSelEnd.Y; i++)
            lines.Add(GetLine(i));

        // Add or remove spaces
        for (int i = 0; i < lines.Count; i++)
        {
            // If there is nothing on the last line, don't bother moving
            if (i != 0 && i == lines.Count - 1 && mSelEnd.X == 0)
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
            if (CursorLoc.Y == mSelStart.Y + i)
                CursorLoc = new TokenLoc(Math.Max(0, CursorLoc.X + moveCh), CursorLoc.Y);
            if (mSelStart.Y == mSelStart.Y + i)
                mSelStart.X = Math.Max(0, mSelStart.X + moveCh);
            if (mSelEnd.Y == mSelStart.Y + i)
                mSelEnd.X = Math.Max(0, mSelEnd.X + moveCh);
        }

        ReplaceText(lines.ToArray(), new TokenLoc(0, mSelStart.Y),
                                     new TokenLoc(GetLine(mSelEnd.Y).Length, mSelEnd.Y));
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
            CursorLoc = mSelStart;
            start = mSelStart;
            end = mSelEnd;
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
        else if (keyChar == '\t' && mTabInsertsSpaces)
        {
            insert = mInsertOneString;
            mInsertOneString[0] = new string(' ', mTabSize - (IndexToCol(CursorLoc) % mTabSize));
        }
        else
        {
            // Insert a single char
            insert = mInsertOneString;
            mInsertOneString[0] = char.ToString(keyChar);
        }

        // Insert/replace the typed char
        CursorLoc = ReplaceText(insert, start, end);
        mCursorUpDownColumn = -1;
        mSelStart = mSelEnd = new TokenLoc();
        EnsureCursorOnScreen();
        UpdateCursorBlinker();
        Invalidate();
        return true;
    }
    
    /// <summary>
    /// Returns true if this is a letter or digit or underscore '_'
    /// </summary>
    bool IsLetterOrDigit(char ch)
    {
        return ch == '_' || char.IsLetterOrDigit(ch);
    }

    /// <summary>
    /// Update the cursor and scroll while user is selecting text
    /// </summary>
    private void timer1_Tick(object sender, EventArgs e)
    {
        // Update cursor visibility
        bool cursorVisible = IsCursorVisible();
        if (cursorVisible != mCursorVisible)
        {
            mCursorVisible = cursorVisible;
            Invalidate(mCursorRect);
            // TBD: Port to Avalonia
            // vMarksLeft.ShowCursor = mCursorVisible;
        }

        // While selecting text, scroll the screen
        if (mMouseDown && mMousePosition != null)
        {
            int linesInWindow = LinesInWindow();
            if (CursorLoc.Y - vScrollBarValue > linesInWindow
                    && vScrollBarValue < vScrollBarMaximum - linesInWindow
                    && mMousePosition.Value.Y > ClientRectangle.Height - hScrollBarHeight)
                vScrollBarValue++;

            if (CursorLoc.Y < vScrollBarValue
                    && vScrollBarValue > 0)
                vScrollBarValue--;
        }

        // Optionally invalidate
        if (mDelayedInvalidate)
            Invalidate();
        mDelayedInvalidate = false;
        timer1Enabled = IsVisible && IsFocused;
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

    /// <summary>
    /// Show marks on the vertical scroll bar
    /// </summary>
    public struct VerticalMarkInfo
    {
        public int Start;
        public int Length;
        public Color Color;
    }




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

    public TokenColorOverride(Token token)
    {
        Token = token;
    }

    public TokenColorOverride(Token token, Pen outlineColor)
    {
        Token = token;
        OutlineColor = outlineColor;
    }
    public TokenColorOverride(Token token, Brush backColor)
    {
        Token = token;
        BackColor = backColor;
    }
    public TokenColorOverride(Token token, Pen outlineColor, Brush backColor)
    {
        Token = token;
        OutlineColor = outlineColor;
        BackColor = backColor;
    }
}


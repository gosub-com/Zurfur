using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Class to display and edit tokens
    /// </summary>
    public partial class TextEditor : UserControl, IEditor
    {
        readonly float SHRUNK_EMPTY_LINE_SCALE = 0.75f;
        readonly float SHRUNK_TEXT_LINE_SCALE = 0.5f;
        readonly float SHRUNK_FONT_SCALE = 0.65f;
        readonly PointF SHRUNK_FONT_OFFSET = new PointF(0.2f, -0.12f); // Scaled by font size
        const int FILL_X_OFFSET = 3;


        // Lexer and text
        Lexer mLexer;
        List<UndoOp>    mUndo = new List<UndoOp>();
        List<UndoOp>    mRedo = new List<UndoOp>();
        bool            mReadOnly;
        bool            mShrinkLines = true;
        int             mModifySaved;
        int             mModifyCount;
        int             mModifyTotal;
        string          mFilePath = "";


        // Tabs and character
        StringFormat    mTabFormat = new StringFormat();
        float[]         mTabSpacing = new float[32];
        int             mTabStartColumnPrevious = -1;
        string[]        mInsertOneString = new string[] { "" };
        string[]        mInsertCR = new string[] { "", "" };
        int             mTabSize = 4;
        bool            mTabInsertsSpaces = true;

        // Mouse and drawing info
        bool            mMouseDown;
        Token           mMouseHoverToken;
        Point           mTopLeft;
        Point           mTestPoint;
        Token           mTestToken;
        SizeF           mFontSize = new SizeF(9, 19);
        bool            mMeasureFont = true;
        bool            mDelayedInvalidate;
        int[]           mLineTops = Array.Empty<int>();
        bool[]          mLineShrunk = Array.Empty<bool>();

        // Cursor info
        TokenLoc        mCursorLoc;
        int             mCursorUpDownColumn = -1;
        DateTime        mCursorBaseTime;
        Rectangle       mCursorRect;
        bool            mCursorVisible;
        bool            mOverwriteMode;

        // Selection
        TokenLoc        mSelStart;
        TokenLoc        mSelEnd;

        // Fonts, colors, and misc.
        Dictionary<eTokenType, FontInfo> mTokenFonts = new Dictionary<eTokenType, FontInfo>();
        Dictionary<eTokenType, FontInfo> mTokenFontsGrayed = new Dictionary<eTokenType, FontInfo>();
        Dictionary<eTokenType, FontInfo> mTokenFontsUnderlined = new Dictionary<eTokenType, FontInfo>();
        Font mShrunkFont;
        Brush       mSelectColor = new SolidBrush(Color.FromArgb(208, 208, 255));
        Brush       mSelectColorNoFocus = new SolidBrush(Color.FromArgb(224, 224, 224));
        EventArgs   mEventArgs = new EventArgs();
        Brush       mErrorColor = Brushes.Pink;
        Brush       mWarnColor = Brushes.Yellow;
        Brush       mCodeInCommentColor = new SolidBrush(Color.FromArgb(208, 255, 208));
        static Token sNormalToken = new Token();

        TokenColorOverride[]mTokenColorOverrides;

        // Internal quick access to mLexer
        int LineCount { get { return mLexer.LineCount; } }
        string GetLine(int line) { return mLexer.GetLine(line); }

        // Delegate types
        public delegate void EditorTokenDelegate(TextEditor sender, Token previousToken, Token newToken);

        /// <summary>
        /// This event occurs when the mouse hover token changes.
        /// </summary>
        public event EditorTokenDelegate MouseHoverTokenChanged;

        /// <summary>
        /// This event occurs when the cursor location changes
        /// </summary>
        public event EventHandler CursorLocChanged;

        /// <summary>
        /// This event happens if a text change is blocked because
        /// the ReadOnly flag is set.  Resetting the ReadOnly flag
        /// inside this event allows the text to be changed.
        /// </summary>
        public event EventHandler BlockedByReadOnly;

        /// <summary>
        /// This event happens after the text has been changed.
        /// </summary>
        public event EventHandler TextChanged2;

        /// <summary>
        /// Occurs when Modify changes
        /// </summary>
        public event EventHandler ModifiedChanged;

        /// <summary>
        /// Occurs when the file path is changed
        /// </summary>
        public event EventHandler FilePathChanged;

        /// <summary>
        /// Occurs when the lexer is set (even if the object does not change)
        /// </summary>
        public event EventHandler LexerChanged;

        public TextEditor()
        {
            mLexer = new Lexer();
            InitializeComponent();
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
            mTabFormat.Dispose();
        }

        /// <summary>
        /// Initialize the editor
        /// </summary>
        private void Editor_Load(object sender, EventArgs e)
        {
            mCursorBaseTime = DateTime.Now;
            SetStyle(ControlStyles.Selectable, true);
            SetStyle(ControlStyles.ContainerControl, true);
        }

        /// <summary>
        /// File info from when file was last loaded or saved (or null when not loaded)
        /// </summary>
        public FileInfo FileInfo { get; set; }

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

        public Control GetControl() { return this; }

        public void LoadFile(string filePath)
        {
            mUndo.Clear(); // TBD: Should handle Undo
            mRedo.Clear();
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
        public override string Text
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
                    new TokenLoc(GetLine(LineCount-1).Length, LineCount));
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

        public void SetMarks(VerticalMarkInfo[] marks)
        {
            vMarksLeft.SetMarks(marks);
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
        public Token MouseHoverToken
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
            cursor.Y = Math.Min(cursor.Y, LineCount-1);
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
                mCursorLoc = value;
                CursorLocChanged?.Invoke(this, mEventArgs);
                UpdateCursorBlinker();
            }
        }

        public int TopVisibleLine => vScrollBar.Value;

        /// <summary>
        /// Returns the number of full lines in the window (partial lines don't count)
        /// </summary>
        public int LinesInWindow()
        {            
            const int REMOVE_PARTIALS = 2; // Add 1 for bottom of line, then 1 more to remove partials
            var height = ClientRectangle.Height - (hScrollBar.Visible ? hScrollBar.Height : 0);
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
            return Math.Max(0, (int)(ClientRectangle.Width / mFontSize.Width)-1);
        }

        /// <summary>
        /// Return X position in window, given the column number
        /// </summary>
        float PointX(int col)
        {
            return col*mFontSize.Width - mTopLeft.X;
        }

        /// <summary>
        /// Return Y position in window, given the line number
        /// </summary>
        float PointY(int line)
        {
            if (line < 0 || mLineTops.Length == 0)
                return line*mFontSize.Height - mTopLeft.Y;
            if (line < mLineTops.Length)
                return mLineTops[line] - mTopLeft.Y;
            return mLineTops[mLineTops.Length-1] + (line-mLineTops.Length+1)*mFontSize.Height - mTopLeft.Y;
        }

        Point ScreenToText(int x, int y)
        {
            x += mTopLeft.X;
            y += mTopLeft.Y;
            var pointX = (int)(x / mFontSize.Width + 0.5f);
            if (y < 0 || mLineTops.Length == 0)
                return new Point(pointX, (int)(y / mFontSize.Height));

            // This function isn't used often, so it's OK to be slow
            for (int line = 1; line < mLineTops.Length; line++)
                if (y < mLineTops[line])
                    return new Point(pointX, line-1);              

            return new Point(pointX, (int)(mLineTops.Length-1+y/mFontSize.Height));
        }


        /// <summary>
        /// Returns the position of the text at the given token location
        /// </summary>
        public Point LocationToken(TokenLoc loc)
        {
            return new Point((int)PointX(IndexToCol(loc)), (int)PointY(loc.Y));
        }

        public Size FontSize => new Size((int)mFontSize.Width, (int)mFontSize.Height);


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
            int col = 0;
            for (int i = 0; i < charIndex && i < line.Length; i++)
            {
                if (line[i] == '\t')
                    col += mTabSize - col%mTabSize;
                else
                    col++;
            }
            return col;
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
                    col += mTabSize - col%mTabSize;
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
            else if (inc.Y < LineCount-1)
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
            if (mTokenFonts.Count == 0 || mShrunkFont == null)
            {
                Font normalFont = new Font(Font, Font.Bold ? FontStyle.Bold : FontStyle.Regular);
                Font boldFont = new Font(Font, FontStyle.Bold);
                mShrunkFont = new Font(Font.Name, Font.Size*SHRUNK_FONT_SCALE, FontStyle.Bold);

                // TBD: These should come from a Json config file, and
                //      eTokenType should be an open ended index (i.e. integer)
                mTokenFonts = new Dictionary<eTokenType, FontInfo>()
                {
                    { eTokenType.Normal, new FontInfo(normalFont, Color.Black) },
                    { eTokenType.Reserved, new FontInfo(normalFont, Color.Blue) },
                    { eTokenType.ReservedControl, new FontInfo(boldFont, Color.Blue) },
                    { eTokenType.Identifier, new FontInfo(normalFont, Color.Black) },
                    { eTokenType.Quote, new FontInfo(normalFont, Color.Brown) },
                    { eTokenType.Comment, new FontInfo(normalFont, Color.Green) },
                    { eTokenType.PublicComment, new FontInfo(boldFont, Color.Green) },
                    { eTokenType.DefineField, new FontInfo(boldFont, Color.Black) },
                    { eTokenType.DefineMethod, new FontInfo(boldFont, Color.Black) },
                    { eTokenType.DefineParam, new FontInfo(boldFont, Color.Black) },
                    { eTokenType.DefineLocal, new FontInfo(boldFont, Color.Black) },
                    { eTokenType.TypeName, new FontInfo(normalFont, Color.FromArgb(20,125,160)) },
                    { eTokenType.BoldSymbol, new FontInfo(boldFont, Color.Black) },
                };

                foreach (var font in mTokenFonts)
                {
                    mTokenFontsUnderlined[font.Key] = new FontInfo(new Font(
                        font.Value.Font, FontStyle.Underline | font.Value.Font.Style), font.Value.Color);
                }
                foreach (var font in mTokenFonts)
                {
                    mTokenFontsGrayed[font.Key] = new FontInfo(font.Value.Font,
                                                    Lerp(font.Value.Color, Color.LightGray, 0.5f));
                }
            }
            // Font info, normal, grayed, or shrunk
            Dictionary<eTokenType, FontInfo> colorTable;
            if (token.Grayed)
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
            return Color.FromArgb((a2*from.R + a1*to.R) >> 8,
                                  (a2*from.G + a1*to.R) >> 8, 
                                  (a2*from.B + a1*to.B) >> 8);
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
            vScrollBar.Maximum = linesInFile;
            vScrollBar.LargeChange = linesInWindow;
            vScrollBar.Enabled = linesInFile > linesInWindow;
            vScrollBar.Visible = linesInFile > linesInWindow && linesInFile > 1;
            vScrollBar.SmallChange = 1;

            // Horizontal properties
            int charsAccross = 0;
            int charsAcrossWindow = CharsAcrossWindow();
            for (int i = 0; i < LineCount; i++)
                charsAccross = Math.Max(charsAccross, IndexToCol(GetLine(i), GetLine(i).Length));
            hScrollBar.Maximum = charsAccross;
            hScrollBar.LargeChange = Math.Max(1, charsAcrossWindow);
            hScrollBar.Enabled = charsAccross > charsAcrossWindow;
            hScrollBar.Visible = charsAccross > charsAcrossWindow && charsAccross > 1;
            hScrollBar.SmallChange = 1;

            // Location & Size
            vScrollBar.Location = new Point(Math.Max(0, ClientRectangle.Width - vScrollBar.Width), 0);
            vScrollBar.Height = Math.Max(0, ClientRectangle.Height);
            hScrollBar.Location = new Point(0, Math.Max(0, ClientRectangle.Height - hScrollBar.Height));
            hScrollBar.Width = Math.Max(0, ClientRectangle.Width - (vScrollBar.Visible ? vScrollBar.Width : 0));

            vMarksLeft.Visible = vScrollBar.Visible;
            vMarksLeft.ArrowHight = vScrollBar.Width;
            vMarksLeft.Location = new Point(vScrollBar.Left-vMarksLeft.Width+1, vScrollBar.Top);
            vMarksLeft.Height = vScrollBar.Height;
            vMarksLeft.CursorMark = CursorLoc.Y;
            vMarksLeft.Maximum = linesInFile-1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SetupScrollBars();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            mMeasureFont = true;
            mTokenFonts.Clear();
            mTabStartColumnPrevious = -1;
            var vScrollWidth = vScrollBar.Width; // Preserve vScrollBar width which gets changed when font is changed
            base.OnFontChanged(e);
            vScrollBar.Width = vScrollWidth; 
            SetupScrollBars();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateCursorBlinker();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            UpdateCursorBlinker();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }


        /// <summary>
        /// Returns TRUE if the cursor should be visible, based
        /// on the control visibility, focus, and cursor blink time.
        /// </summary>
        bool IsCursorVisible()
        {
            return Visible && Focused
                    && (DateTime.Now - mCursorBaseTime).Milliseconds < 600;
        }

        /// <summary>
        /// Sets the cursor location, and resets the cursor blink timer.
        /// </summary>
        void UpdateCursorBlinker()
        {
            timer1.Enabled = Visible && Focused;
            mCursorBaseTime = DateTime.Now;
            int column = IndexToCol(CursorLoc);

            var x = (int)PointX(column);
            var y = (int)PointY(CursorLoc.Y);
            Rectangle cursorRect = new Rectangle(x + 1 + (mOverwriteMode ? 2 : 0), y + 1,
                                                 mOverwriteMode && !HasSel() ? (int)mFontSize.Width : 2,
                                                (int)(PointY(CursorLoc.Y+1)-y)-2);

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

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            UpdateCursorBlinker();
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            UpdateCursorBlinker();
        }

        /// <summary>
        /// Ensure that this control processes all control
        /// keys (TAB, Arrow Keys, etc.)
        /// </summary>
        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }

        /// <summary>
        /// Ensure that normal key strokes are not processed by the dialog
        /// (i.e. Without this, the dialog would process regular keys,
        /// like 'b' and send them to a button or menu)
        /// </summary>
        protected override bool IsInputChar(char charCode)
        {
            return true;
        }

        /// <summary>
        /// Move the cursor down (or up if lines is negative)
        /// </summary>
        TokenLoc ArrowKeyDown(TokenLoc cursor, int lines)
        {
            // Calculate new cursor location
            int oldCursorLine = cursor.Y;
            cursor.Y += lines;
            cursor.Y = Math.Min(LineCount-1, cursor.Y);
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
            if (CursorLoc.Y < vScrollBar.Value+marginY)
                vScrollBar.Value = Math.Max(0, CursorLoc.Y-marginY);
            if (CursorLoc.Y > vScrollBar.Value + linesInWindow - marginY)
                vScrollBar.Value = Math.Min(vScrollBar.Maximum, CursorLoc.Y- linesInWindow + marginY);

            // Horizontal
            int charsAcrossWindow = CharsAcrossWindow();
            int marginX = Math.Min(4, Math.Max(0, charsAcrossWindow - 5));
            if (IndexToCol(CursorLoc) < hScrollBar.Value+marginX)
                hScrollBar.Value = Math.Max(0, IndexToCol(CursorLoc)-marginX);
            if (IndexToCol(CursorLoc) > hScrollBar.Value + charsAcrossWindow - marginX)
                hScrollBar.Value = Math.Min(hScrollBar.Maximum, IndexToCol(CursorLoc)- charsAcrossWindow + marginX);
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
            try
            {
                Clipboard.Clear();
                string []copy = mLexer.GetText(mSelStart, mSelEnd);
                if (copy.Length == 1)
                    Clipboard.SetText(copy[0]);
                else if (copy.Length > 1)
                {
                    int length = 0;
                    foreach (string s in copy)
                        length += s.Length + 2;
                    StringBuilder sb = new StringBuilder(length+3);
                    sb.Append(copy[0]);
                    for (int i = 1; i < copy.Length; i++)
                    {
                        sb.Append("\r\n");
                        sb.Append(copy[i]);
                    }
                    Clipboard.SetText(sb.ToString());
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Error copying to clipboard: " + e.Message);
            }
        }

        /// <summary>
        /// Paste from clipboard
        /// </summary>
        void Paste()
        {
            SelDel();

            try
            {
                string clip = Clipboard.GetText();
                string []clipArray = clip.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                CursorLoc = ReplaceText(clipArray, CursorLoc, CursorLoc);

            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Error copying from clipboard: " + e.Message);
            }

        }

        /// <summary>
        /// Returns an undo operation with the current state filled in.
        /// NOTE: You must fill in the Text, TextStart, and TextEnd
        /// </summary>
        UndoOp GetDefaultUndo()
        {
            UndoOp undo = new UndoOp();
            undo.TopIndex = vScrollBar.Value;
            undo.LeftIndex = hScrollBar.Value;
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
        TokenLoc ReplaceText(string[] replacementText,
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
            UndoOp fundo = mUndo.Count == 0 ? null : mUndo[mUndo.Count-1];
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
                && fundo.TextEnd.X == undo.TextEnd.X-1)
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
            UndoOp undo = undoList[undoList.Count-1];
            undoList.RemoveAt(undoList.Count-1);

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
            vScrollBar.Value = Math.Max(0, Math.Min(vScrollBar.Maximum, undo.TopIndex));
            hScrollBar.Value = Math.Max(0, Math.Min(hScrollBar.Maximum, undo.LeftIndex));

            var modified = Modified;
            mModifyCount = undo.ModifyCount;
            if (modified != Modified)
                ModifiedChanged.Invoke(this, EventArgs.Empty);

            // Call user delegate
            OnTextChangedInternal();
        }

        /// <summary>
        /// Gets/sets the lexer to hold and lex the text.
        /// When set, if the text is different, the undo-redo
        /// is deleted.
        /// WARNING: Do not change the text from the lexer, or else
        ///          it gets out of sync with this control.  
        /// </summary>
        public Lexer Lexer
        {
            get { return mLexer; }
            set
            {
                if ((object)mLexer == (object)value)
                    return;
                bool eq = mLexer.Equals(value);
                mLexer = value;
                if (!eq)
                {
                    mUndo.Clear(); // TBD: Fix undo/redo
                    mRedo.Clear();
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
        void SelClear(bool left)
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


        /// <summary>
        /// Draw the selection background
        /// </summary>
        void DrawSelection(Graphics gr)
        {
            if (!HasSel())
                return;

            // Get selection start/end
            TokenLoc selStart = mSelStart;
            TokenLoc selEnd = mSelEnd;
            TokenLoc.FixOrder(ref selStart, ref selEnd);

            float mMinY = -mFontSize.Height;
            for (int line = Math.Max(0, selStart.Y);
                 line <= selEnd.Y && line < LineCount;
                 line++)
            {
                // Skip lines not in window
                float y = PointY(line);
                if (y < mMinY)
                    continue;
                if (y > ClientRectangle.Height)
                    break;

                // Get default start and end of draw location
                float x = 0;
                float xEnd = mFontSize.Width;

                // Middle line xEnd
                if (GetLine(line).Length != 0)
                    xEnd = IndexToCol(GetLine(line), GetLine(line).Length)*mFontSize.Width;

                // Start/end lines
                if (line == selStart.Y)
                    x = PointX(IndexToCol(selStart));
                if (line == selEnd.Y)
                    xEnd = PointX(IndexToCol(selEnd));

                gr.FillRectangle(Focused ? mSelectColor : mSelectColorNoFocus,
                    new RectangleF(x+FILL_X_OFFSET, y, Math.Max(0, xEnd-x), (int)(PointY(line+1)-y)));
            }
        }

        /// <summary>
        /// Print a token (either the foreground or background)
        /// Sets mTestToken if a token is under mTestPoint
        /// </summary>
        void DrawToken(Graphics gr, Token token, bool background)
        {
            // Find token position and bounds
            int col = IndexToCol(token.Location);
            float x = PointX(col);
            float y = PointY(token.Y);
            int tokenLength = token.Name.Length == 0 ? 4 : token.Name.Length; // Give EOF some width
            float xEnd = PointX(col + tokenLength);
            float yEnd = PointY(token.Y + 1);

            // If it's under the test point, return it
            if (mTestPoint.X >= x && mTestPoint.X < xEnd
                && mTestPoint.Y >= y && mTestPoint.Y < yEnd)
            {
                mTestToken = token;
            }
            if (gr == null)
                return;

            // Print the token
            var backRect = new Rectangle((int)(x - 1) + FILL_X_OFFSET, (int)y, (int)(xEnd - x + 1), (int)(yEnd - y));
            var overrides = FindColorOverride(token);

            if (background)
            {
                // Draw background color
                if (overrides != null && overrides.BackColor != null)
                {
                    gr.FillRectangle(overrides.BackColor, backRect);
                }
                else
                {
                    // TBD: This should be looked up in GetFontInfo based on Type and Subtype
                    if (token.Subtype == eTokenSubtype.Error)
                        gr.FillRectangle(mErrorColor, backRect);
                    else if (token.Subtype == eTokenSubtype.Warn)
                        gr.FillRectangle(mWarnColor, backRect);
                    else if (token.Subtype == eTokenSubtype.CodeInComment)
                        gr.FillRectangle(mCodeInCommentColor, backRect);
                }
                return;
            }

            if (token.Invisible)
                return;

            // Adjust tabs
            int tabStartColumn = mTabSize - col % mTabSize;
            if (tabStartColumn != mTabStartColumnPrevious && token.Name.IndexOf('\t') >= 0)
            {
                mTabStartColumnPrevious = tabStartColumn;
                mTabFormat.SetTabStops(tabStartColumn*mFontSize.Width, mTabSpacing);
            }

            if (token.Y >= 0 && token.Y < mLineShrunk.Length && mLineShrunk[token.Y])
            {
                // Draw shrunk text
                x = (int)(x + mFontSize.Width * SHRUNK_FONT_OFFSET.X);
                y = (int)(y + mFontSize.Height * SHRUNK_FONT_OFFSET.Y);
                gr.DrawString(token.Name, mShrunkFont, Brushes.Black, x, y, mTabFormat);
            }
            else
            {
                // Draw normal text
                FontInfo fontInfo = GetFontInfo(token);
                var font = overrides != null && overrides.Font != null ? overrides.Font : fontInfo.Font;
                var brush = overrides != null && overrides.ForeColor != null ? overrides.ForeColor : fontInfo.Brush;
                gr.DrawString(token.Name, font, brush, x, y, mTabFormat);
            }
            // Draw outline
            if (overrides != null && overrides.OutlineColor != null)
                gr.DrawRectangle(overrides.OutlineColor, backRect);
        }

        private TokenColorOverride FindColorOverride(Token token)
        {
            if (mTokenColorOverrides != null && mTokenColorOverrides.Length != 0)
                foreach (var over in mTokenColorOverrides)
                    if (over.Token == token)
                        return over;
            return null;
        }

        /// <summary>
        /// Draw tokens on the screen (either the foreground or background).
        /// When gr is NULL, a test is performed to see if the token is
        /// under the cursor (mTestPoint) and the result is put in to mTestToken.
        /// </summary>
        void DrawScreen(Graphics gr, bool background)
        {
            // Get clipping region (entire screen when gr is NULL)
            float minY = -mFontSize.Height - 1;
            float maxY = ClientRectangle.Height + 1;
            if (gr != null)
            {
                // Optionally adjust clipping region
                minY = gr.VisibleClipBounds.Top - mFontSize.Height - 1;
                maxY = gr.VisibleClipBounds.Bottom + 1;
            }

            // Find first and last visible line
            int startLine = TopVisibleLine;
            while (startLine < LineCount && PointY(startLine) < minY)
                startLine++;
            int endLine = startLine;
            while (endLine <= LineCount && PointY(endLine) < maxY)
                endLine++;


            // Draw all tokens on the screen
            foreach (Token token in mLexer.GetEnumeratorStartAtLine(startLine))
            {
                // Quick exit when drawing below screen
                if (token.Y >= endLine)
                    break;

                DrawToken(gr, token, background);
            }

            foreach (Token token in Lexer.MetaTokens)
                DrawToken(gr, token, background);
        }

        /// <summary>
        /// Paint the screen
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Initialize fonts (or setup when changed)
            if (mMeasureFont)
            {
                // Measure the font size
                mMeasureFont = false;
                var normalFont = GetFontInfo(sNormalToken).Font;
                SizeF size1 = e.Graphics.MeasureString("MM\r\nMM", normalFont);
                SizeF size2 = e.Graphics.MeasureString("MMM\r\nMMM\r\nMMM", normalFont);
                mFontSize.Width = Math.Max(1, size2.Width - size1.Width);
                mFontSize.Height = Math.Max(1, (int)(size2.Height - size1.Height+1 + 0.5f));
                for (int i = 0; i < mTabSpacing.Length; i++)
                    mTabSpacing[i] = mFontSize.Width*mTabSize;
                mTabSpacing[0] = 0;
                RecalcLineTops();

                // Setup cursor
                UpdateCursorBlinker();
            }

            // Draw the graphics
            //e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            DrawScreen(e.Graphics, true);
            DrawSelection(e.Graphics);
            DrawScreen(e.Graphics, false);

            // Draw the cursor
            if (mCursorVisible)
            {
                // Draw the cursor
                e.Graphics.FillRectangle(Brushes.DarkBlue, mCursorRect);

                // Draw text under cursor in over-write mode
                if (mOverwriteMode && !HasSel()
                        && CursorLoc.Y < LineCount 
                        && CursorLoc.X >= 0 
                        && CursorLoc.X < GetLine(CursorLoc.Y).Length)
                {
                    float x = PointX(IndexToCol(CursorLoc));
                    float y = PointY(CursorLoc.Y);
                    e.Graphics.DrawString(GetLine(CursorLoc.Y)[CursorLoc.X].ToString(),
                                            GetFontInfo(sNormalToken).Font, Brushes.White, x, y);
                }
            }

            // Set the scroll bar properties
            // TBD - Do only when text is changed
            SetupScrollBars();

            base.OnPaint(e);
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
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta > 0)
                vScrollBar.Value = Math.Max(0, vScrollBar.Value-3);
            else
                vScrollBar.Value = Math.Min(vScrollBar.Maximum, vScrollBar.Value + 3);
        }

        /// <summary>
        /// Set the cursor to the mouse location
        /// </summary>
        void SetCursorByMouse(MouseEventArgs e)
        {
            // Ensure we can't go more than one line above/below the window
            int y = e.Y;
            y = Math.Max(y, -(int)mFontSize.Height);
            y = Math.Min(y, Height);// + (int)mFontSize.Height);

            // Set cursor according to line
            TokenLoc cursor = CursorLoc;
            Point text = ScreenToText(e.X, y);
            cursor.Y = text.Y;
            cursor.Y = Math.Min(cursor.Y, LineCount-1);
            cursor.Y = Math.Max(cursor.Y, 0);
            cursor.X = ColToIndex(GetLine(cursor.Y), text.X);
            CursorLoc = cursor;
            UpdateCursorBlinker();
        }

        /// <summary>
        /// Move cursor when user clicks
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Set cursor location, remove selection
            SetCursorByMouse(e);
            mSelStart = mSelEnd = CursorLoc;
            mMouseDown = true;
            UpdateCursorBlinker();

            base.OnMouseDown(e);
            Invalidate();
        }

        /// <summary>
        /// Returns true if this is a letter or digit or underscore '_'
        /// </summary>
        bool IsLetterOrDigit(char ch)
        {
            return ch == '_' || char.IsLetterOrDigit(ch);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            UpdateMouseHoverToken();
            base.OnMouseClick(e);
        }

        /// <summary>
        /// Select a single token when the user double clicks
        /// </summary>
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            mMouseDown = false;
            Invalidate();
            SetCursorByMouse(e);

            // Empty line?
            string line = GetLine(CursorLoc.Y);
            int startIndex = Math.Min(CursorLoc.X, line.Length-1);
            if (startIndex < 0)
                return;

            // Scan to beginning of token
            if (char.IsWhiteSpace(line, startIndex))
            {
                while (startIndex > 0 && char.IsWhiteSpace(line, startIndex-1))
                    startIndex--;
            }
            else if (IsLetterOrDigit(line[startIndex]))
            {
                while (startIndex > 0 && IsLetterOrDigit(line[startIndex-1]))
                    startIndex--;
            }

            // Scan to end of token
            int endIndex = startIndex;
            if (char.IsWhiteSpace(line, endIndex))
            {
                while (endIndex < line.Length && char.IsWhiteSpace(line, endIndex))
                    endIndex++;
            }
            else if (IsLetterOrDigit(line[endIndex]))
            {
                while (endIndex < line.Length && IsLetterOrDigit(line[endIndex]))
                    endIndex++;
            }

            if (startIndex != endIndex)
            {
                // Set selected text (and cursor)
                mSelStart = mSelEnd = CursorLoc;
                mSelStart.X = startIndex;
                mSelEnd.X = endIndex;
                CursorLoc = new TokenLoc(endIndex, CursorLoc.Y);
                UpdateCursorBlinker();
            }


            base.OnMouseDoubleClick(e);
        }

        /// <summary>
        /// Update the hover token or selected text when the mouse moves
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            UpdateMouseHoverToken();

            // If mouse button is down, move selection
            if (mMouseDown)
            {
                SetCursorByMouse(e);
                mSelEnd = CursorLoc;

                // NOTE: Invalidating here can cause so much screen
                // updating that the screen doesn't scroll via
                // the timer.  Yet we need to invalidate if the
                // selection changes.  This fixes that problem.
                mDelayedInvalidate = true;
            }
            base.OnMouseMove(e);
        }

        private void UpdateMouseHoverToken(bool forceEvent = false)
        {
            // Draw to NULL graphics to find the point
            mTestPoint = PointToClient(Form.MousePosition);
            mTestToken = null;
            DrawScreen(null, true);

            // Set new mouse hover token
            if (forceEvent || mTestToken != mMouseHoverToken)
            {
                Token previousToken = mMouseHoverToken;
                mMouseHoverToken = mTestToken;
                MouseHoverTokenChanged?.Invoke(this, previousToken, mMouseHoverToken);
                Invalidate();
            }
        }

        /// <summary>
        /// Done selecting text
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            EnsureCursorOnScreen();
            mMouseDown = false;
            TokenLoc.FixOrder(ref mSelStart, ref mSelEnd);
            base.OnMouseUp(e);
        }

        /// <summary>
        /// Hover token is cleared when mouse leaves the window
        /// </summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            if (mMouseHoverToken != null)
            {
                Token previousToken = mMouseHoverToken;
                mMouseHoverToken = null;
                MouseHoverTokenChanged?.Invoke(this, previousToken, mMouseHoverToken);
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        /// <summary>
        /// User scrolls text
        /// </summary>
        void ScrollBar_Changed()
        {
            mTopLeft = new Point();
            mTopLeft = new Point((int)PointX(hScrollBar.Value), (int)PointY(vScrollBar.Value));
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

        bool mControlKeyDown;
        protected override void OnKeyUp(KeyEventArgs e)
        {
            // SHIFT, CTRL, and ALT keys update the hover token
            // since the look may change when these keys are pressed
            if (e.KeyValue == 16 || e.KeyValue == 17 || e.KeyValue == 18)
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
            // Allow user event to intercept key strokes
            base.OnKeyDown(e);
            if (e.Handled)
                return;
            e.Handled = true;

            // Display search form
            if (e.Control && e.KeyCode == Keys.F)
                FormSearch.Show(ParentForm, this);
            if (e.KeyCode == Keys.F3)
                FormSearch.FindNext(ParentForm, this);

            // SHIFT, CTRL, and ALT keys update the hover token
            // since the look may change when these keys are pressed
            if (e.KeyValue == 16 || e.KeyValue == 17 || e.KeyValue == 18)
            {
                if (!mControlKeyDown)
                    UpdateMouseHoverToken(true);
                mControlKeyDown = true;
                return;
            }

            // NOTE: ASCII characters are handled in OnKeyPress
            bool ensureCursorOnScreen = true;

            // ESC key
            if (e.KeyValue == 27)
            {
                mSelStart = mSelEnd = new TokenLoc();
                ensureCursorOnScreen = false;
            }

            // Handle DOWN arrow
            if (e.KeyCode == Keys.Down)
                MoveCursor(ArrowKeyDown(CursorLoc, 1), false, e.Shift);

            // Handle UP arrow
            if (e.KeyCode == Keys.Up)
                MoveCursor(ArrowKeyDown(CursorLoc, -1), true, e.Shift);

            // Handle RIGHT arrow
            if (e.KeyCode == Keys.Right)
            {
                TokenLoc cursor = CharIndexInc(CursorLoc);
                if (e.Control)
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
                MoveCursor(cursor, false, e.Shift);
                mCursorUpDownColumn = -1;
            }
            // Handle LEFT arrow
            if (e.KeyCode == Keys.Left)
            {
                TokenLoc cursor = CharIndexDec(CursorLoc);
                if (e.Control)
                {
                    // Move word left (max 32 chars)
                    int i = 32;
                    while (--i > 0 && cursor.X > 0
                            && char.IsWhiteSpace(GetLine(cursor.Y)[cursor.X-1]))
                        cursor = CharIndexDec(cursor);
                    while (--i > 0 && cursor.X > 0
                            && char.IsLetterOrDigit(GetLine(cursor.Y)[cursor.X-1]))
                        cursor = CharIndexDec(cursor);
                }
                MoveCursor(cursor, true, e.Shift);
                mCursorUpDownColumn = -1;
            }
            // HOME
            if (e.KeyCode == Keys.Home)
            {
                // Find first non-white space
                string line = GetLine(CursorLoc.Y);
                int firstText = 0;
                while (firstText < GetLine(CursorLoc.Y).Length
                            && char.IsWhiteSpace(line, firstText))
                    firstText++;

                // Go home, or to beginning of text
                TokenLoc newCursor = CursorLoc;
                if (newCursor.X > firstText || newCursor.X == 0)
                    newCursor.X = firstText;
                else
                    newCursor.X = 0;

                // CTRL-HOME goes to beginning
                if (e.Control)
                    newCursor = new TokenLoc();
                MoveCursor(newCursor, true, e.Shift);
                mCursorUpDownColumn = -1;
            }
            // END
            if (e.KeyCode == Keys.End)
            {
                TokenLoc newCursor = new TokenLoc(0, CursorLoc.Y);
                if (e.Control)
                    newCursor = new TokenLoc(0, LineCount-1);
                newCursor.X = GetLine(newCursor.Y).Length;
                MoveCursor(newCursor, true, e.Shift);
                mCursorUpDownColumn = -1;
            }
            // PAGE UP
            if (e.KeyCode == Keys.PageUp && !e.Control)
            {
                int linesInWindow = LinesInWindow();
                vScrollBar.Value = Math.Max(0, vScrollBar.Value - linesInWindow);
                MoveCursor(ArrowKeyDown(CursorLoc, -linesInWindow), true, e.Shift);
            }
            // PAGE DOWN
            if (e.KeyCode == Keys.PageDown && !e.Control)
            {
                int linesInWindow = LinesInWindow();
                vScrollBar.Value = Math.Max(0, Math.Min(vScrollBar.Maximum - linesInWindow, vScrollBar.Value + linesInWindow));
                MoveCursor(ArrowKeyDown(CursorLoc, linesInWindow), false, e.Shift);
            }
            // DELETE
            if (e.KeyCode == Keys.Delete && !e.Shift && !e.Control)
            {
                if (!SelDel())
                {
                    // Delete char
                    TokenLoc inc = CharIndexInc(CursorLoc);
                    ReplaceText(null, CursorLoc, inc);
                }
            }
            // BACK SPACE
            if (e.KeyCode == Keys.Back && !e.Shift && !e.Control)
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
            if (e.KeyCode == Keys.A && e.Control)
            {
                CursorLoc = new TokenLoc(GetLine(LineCount-1).Length, LineCount-1);
                mSelStart = new TokenLoc();
                mSelEnd = CursorLoc;
                ensureCursorOnScreen = false;
            }

            // CTRL-Z: undo
            if (e.KeyCode == Keys.Z && e.Control && ! e.Shift)
                Undo(mUndo, mRedo);

            // CTRL-Y or SHIFT_CTRL-Z: redo
            if (e.KeyCode == Keys.Y && e.Control
                || e.KeyCode == Keys.Z && e.Control && e.Shift)
                Undo(mRedo, mUndo);

            // CTRL-X: cut
            if (e.KeyCode == Keys.X && e.Control
                || e.KeyCode == Keys.Delete && e.Shift)
                Cut();

            // CTRL-C: copy
            if (e.KeyCode == Keys.C && e.Control
                || e.KeyCode == Keys.Insert && e.Control)
            {
                Copy();
                ensureCursorOnScreen = false;
            }

            // CTRL-V - paste
            if (e.KeyCode == Keys.V && e.Control
                || e.KeyCode == Keys.Insert && e.Shift)
                Paste();

            if (e.KeyCode == Keys.Insert && !e.Control && !e.Shift)
                OverwriteMode = !OverwriteMode;

            // '\t' with selection (without selection handled in OnKeyPress)
            if (e.KeyCode == Keys.Tab && HasSel())
                TabWithSelection(e.Shift);

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
            List<string> lines = new List<string>();
            for (int i = mSelStart.Y; i <= mSelEnd.Y; i++)
                lines.Add(GetLine(i));

            // Add or remove spaces
            for (int i = 0; i < lines.Count; i++)
            {
                // If there is nothing on the last line, don't bother moving
                if (i != 0 && i == lines.Count-1 && mSelEnd.X == 0)
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
                    CursorLoc = new TokenLoc(Math.Max(0, CursorLoc.X+moveCh), CursorLoc.Y);
                if (mSelStart.Y == mSelStart.Y + i)
                    mSelStart.X = Math.Max(0, mSelStart.X+moveCh);
                if (mSelEnd.Y == mSelStart.Y + i)
                    mSelEnd.X = Math.Max(0, mSelEnd.X+moveCh);
            }

            ReplaceText(lines.ToArray(), new TokenLoc(0, mSelStart.Y),
                                         new TokenLoc(GetLine(mSelEnd.Y).Length, mSelEnd.Y));
        }

        /// <summary>
        /// Handle normal text keys (including enter)
        /// </summary>
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // Allow user event to intercept key strokes
            base.OnKeyPress(e);
            if (e.Handled)
                return;
            e.Handled = true;

            // NOTE: Special chars are handled in OnKeyDown
            if (!(e.KeyChar >= ' ' && e.KeyChar <= '~'
                || char.IsLetterOrDigit(e.KeyChar)
                // NOTE: '\t' with selection is handled in OnKeyDown
                || e.KeyChar == '\t' && !HasSel() 
                || e.KeyChar == '\r'))
                return;

            // Setup to replace selection (or insert new char)
            TokenLoc start = CursorLoc;
            TokenLoc end = CursorLoc;

            if (OverwriteMode && !HasSel() && e.KeyChar != '\r')
                end.X++;

            if (HasSel())
            {
                CursorLoc = mSelStart;
                start = mSelStart;
                end = mSelEnd;
            }
            // Setup to insert the char (or '\r')
            string []insert;
            if (e.KeyChar == '\r')
            {
                // Insert ENTER (and space before cursor)
                string line = GetLine(CursorLoc.Y);
                int i = 0;
                while (i < line.Length && i < CursorLoc.X
                            && char.IsWhiteSpace(line, i))
                    i++;
                insert = mInsertCR;
                mInsertCR[1] = line.Substring(0, i);
            }
            else if (e.KeyChar == '\t' && mTabInsertsSpaces)
            {
                insert = mInsertOneString;
                mInsertOneString[0] = new string(' ', mTabSize - ( IndexToCol(CursorLoc) % mTabSize) );
            }
            else
            {
                // Insert a single char
                insert = mInsertOneString;
                mInsertOneString[0] = char.ToString(e.KeyChar);
            }

            // Insert/replace the typed char
            CursorLoc = ReplaceText(insert, start, end);
            mCursorUpDownColumn = -1;
            mSelStart = mSelEnd = new TokenLoc();
            EnsureCursorOnScreen();
            UpdateCursorBlinker();
            Invalidate();
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
                vMarksLeft.ShowCursor = mCursorVisible;
            }

            // While selecting text, scroll the screen
            if (mMouseDown)
            {
                int linesInWindow = LinesInWindow();
                if (CursorLoc.Y - vScrollBar.Value > linesInWindow
                        && vScrollBar.Value < vScrollBar.Maximum - linesInWindow
                        && PointToClient(Form.MousePosition).Y > ClientSize.Height - hScrollBar.Height)
                    vScrollBar.Value++;

                if (CursorLoc.Y < vScrollBar.Value
                        && vScrollBar.Value > 0)
                    vScrollBar.Value--;
            }

            // Optionally invalidate
            if (mDelayedInvalidate)
                Invalidate();
            mDelayedInvalidate = false;
            timer1.Enabled = Visible && Focused;
        }

        /// <summary>
        /// Class to keep track of undo info
        /// </summary>
        class UndoOp
        {
            public string []Text;
            public int		TopIndex;
            public int		LeftIndex;
            public int      ModifyCount;
            public TokenLoc	TextStart;
            public TokenLoc	TextEnd;
            public TokenLoc	Cursor;
            public TokenLoc	SelStart;
            public TokenLoc	SelEnd;
        }

        /// <summary>
        /// Class to keep track of font foreground color
        /// </summary>
        class FontInfo
        {
            public Font Font;
            public Color Color;
            public Brush Brush;

            public FontInfo(Font font, Color color)
            {
                Font = font;
                Color = color;
                Brush = new SolidBrush(color);
            }
        }
    }

    /// <summary>
    /// Override the background color of a token
    /// </summary>
    public class TokenColorOverride
    {
        public Token Token;
        public Font  Font;
        public Brush ForeColor;
        public Pen   OutlineColor;
        public Brush BackColor;

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
}

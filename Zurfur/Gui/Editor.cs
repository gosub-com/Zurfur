using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Class to display and edit tokens
    /// </summary>
    public partial class Editor:UserControl
    {
        // Lexer and text
        Lexer				mLexer;
        List<UndoOp>		mUndo = new List<UndoOp>();
        List<UndoOp>		mRedo = new List<UndoOp>();
        bool				mReadOnly;
        int                 mModifySaved;
        int                 mModifyCount;
        int                 mModifyTotal;

        // Tabs and character
        StringFormat mTabFormat = new StringFormat();
        float				[]mTabSpacing = new float[32];
        int					mTabStartColumnPrevious = -1;
        string				[]mInsertOneChar = new string[] { "" };
        string				[]mInsertCR = new string[] { "", "" };
        int					mTabSize = 4;

        // Mouse and drawing info
        Token				mLastMouseHoverToken;
        Point				mOffset;
        Point				mTestPoint;
        Token				mTestToken;
        SizeF				mFontSize;
        bool				mDelayedInvalidate;

        // Cursor info
        TokenLoc			mCursorLoc;
        int					mCursorUpDownColumn = -1;
        DateTime			mCursorBaseTime;
        Rectangle			mCursorRect;
        bool				mCursorVisible;
        bool				mOverwriteMode;

        // Selection
        TokenLoc			mSelStart;
        TokenLoc			mSelEnd;
        bool				mMouseDown;


        // Fonts, colors, and misc.
        Dictionary<eTokenType, FontInfo> mTokenFonts = new Dictionary<eTokenType, FontInfo>();
        Dictionary<eTokenType, FontInfo> mTokenFontsGrayed = new Dictionary<eTokenType, FontInfo>();
        Brush mSelectColor = new SolidBrush(Color.FromArgb(160, 160, 255));
        Brush				mSelectColorNoFocus = new SolidBrush(Color.FromArgb(208, 208, 208));
        EventArgs			mEventArgs = new EventArgs();
        Brush				mErrorColor = Brushes.Pink;
        static Token sNormalToken = new Token();

        // User back color overrides
        TokenColorOverride	[]mTokenColorOverrides;

        // Internal quick access to mLexer
        int LineCount { get { return mLexer.LineCount; } }
        string GetLine(int line) { return mLexer.GetLine(line); }

        // Delegate types
        public delegate void EditorTokenDelegate(Editor sender, Token previousToken, Token newToken);

        /// <summary>
        /// This event occurs when the mouse hover token changes.
        /// </summary>
        public event EditorTokenDelegate		MouseTokenChanged;

        /// <summary>
        /// This event occurs when the cursor location changes
        /// </summary>
        public event EventHandler				CursorLocChanged;

        /// <summary>
        /// This event happens if a text change is blocked because
        /// the ReadOnly flag is set.  Resetting the ReadOnly flag
        /// inside this event allows the text to be changed.
        /// </summary>
        public event EventHandler				BlockedByReadOnly;

        /// <summary>
        /// This event happens after the text has been changed.
        /// </summary>
        public event EventHandler				TextChanged2;

        /// <summary>
        /// Occurs when Modify changes
        /// </summary>
        public event EventHandler ModifiedChanged;

        public Editor()
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
        /// This field is reserved for the user of this class
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// This field is reserved for the user of this class.
        /// </summary>
        public string FileTitle { get; set; } = "";

        /// <summary>
        /// This field is reserved for the user of this class
        /// </summary>
        public FileInfo FileInfo;

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
                    new TokenLoc(LineCount, GetLine(LineCount-1).Length));
                TextChanged2?.Invoke(this, mEventArgs);
                Invalidate();
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
            get { return mLastMouseHoverToken; }
        }

        /// <summary>
        /// Array of tokens to override the default coloring
        /// </summary>
        public TokenColorOverride[] TokenColorOverrides
        {
            get { return mTokenColorOverrides; }
            set
            {
                Invalidate();
                mTokenColorOverrides = value;
            }
        }

        /// <summary>
        /// Fixes the chararcter and line index to be within bounds.
        /// NOTE: The char index can be equal to the end of char index
        /// </summary>
        void FixCursorLocation(ref TokenLoc cursor)
        {
            cursor.Line = Math.Min(cursor.Line, LineCount-1);
            cursor.Line = Math.Max(cursor.Line, 0);
            cursor.Char = Math.Min(cursor.Char, GetLine(cursor.Line).Length);
            cursor.Char = Math.Max(cursor.Char, 0);
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

        /// <summary>
        /// Returns the number of full lines in the window (partial lines don't count)
        /// </summary>
        int LinesInWindow
        {
            get { return Math.Max(0, (int)(ClientRectangle.Height / mFontSize.Height)-1); }
        }

        /// <summary>
        /// Returns the size
        /// </summary>
        int CharsAcrossWindow
        {
            get { return Math.Max(0, (int)(ClientRectangle.Width / mFontSize.Width)-1); }
        }


        const int TEXT_OFFSET_X = 3;  // Offset between text and rectangle

        /// <summary>
        /// Return X position in window, given the column number
        /// </summary>
        float LocationX(int col)
        {
            return col*mFontSize.Width + mOffset.X + TEXT_OFFSET_X;
        }

        /// <summary>
        /// Return Y position in window, given the line number
        /// </summary>
        float LocationY(int line)
        {
            return line*mFontSize.Height + mOffset.Y;
        }

        /// <summary>
        /// Returns the position of the text at the given token location
        /// </summary>
        public Point LocationToken(TokenLoc loc)
        {
            return new Point((int)LocationX(IndexToCol(loc)), (int)LocationY(loc.Line));
        }


        /// <summary>
        /// Convert the index to a column
        /// </summary>
        int IndexToCol(TokenLoc loc)
        {
            if (loc.Line < 0 || loc.Line >= LineCount)
                return 0;
            return IndexToCol(GetLine(loc.Line), loc.Char);
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
            if (inc.Char < GetLine(inc.Line).Length)
            {
                inc.Char++;
            }
            else if (inc.Line < LineCount-1)
            {
                inc.Char = 0;
                inc.Line++;
            }
            return inc;
        }

        /// <summary>
        /// Get the previous character position (can return previous line)
        /// </summary>
        public TokenLoc CharIndexDec(TokenLoc dec)
        {
            if (dec.Char > 0)
            {
                dec.Char--;
            }
            else if (dec.Line > 0)
            {
                dec.Line--;
                dec.Char = GetLine(dec.Line).Length;
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
                Font normalFont = new Font(Font, Font.Bold ? FontStyle.Bold : FontStyle.Regular);
                Font boldFont = new Font(Font, FontStyle.Bold);

                mTokenFonts = new Dictionary<eTokenType, FontInfo>()
                {
                    { eTokenType.Normal, new FontInfo(normalFont, Color.Black) },
                    { eTokenType.Reserved, new FontInfo(normalFont, Color.Blue) },
                    { eTokenType.ReservedControl, new FontInfo(boldFont, Color.Blue) },
                    { eTokenType.Identifier, new FontInfo(normalFont, Color.Black) },
                    { eTokenType.Quote, new FontInfo(normalFont, Color.Brown) },
                    { eTokenType.Comment, new FontInfo(normalFont, Color.Green) },
                };
                foreach (var font in mTokenFonts)
                    mTokenFontsGrayed[font.Key] = new FontInfo(font.Value.Font, 
                                                    Lerp(font.Value.Color, Color.LightGray, 0.5f));
            }
            var colorTable = token.Grayed ? mTokenFontsGrayed : mTokenFonts;
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
                mFontSize = new SizeF(); // Flag to setup new font
                mTabStartColumnPrevious = -1;
            }
        }

        void SetupScrollBars()
        {
            // Vertical properties
            int linesInFile = LineCount;
            vScrollBar.Maximum = linesInFile;
            vScrollBar.LargeChange = Math.Max(1, LinesInWindow);
            vScrollBar.Enabled = linesInFile > LinesInWindow;
            vScrollBar.Visible = linesInFile > LinesInWindow && linesInFile > 1;
            vScrollBar.SmallChange = 1;

            // Horizontal properties
            int charsAccross = 0;
            for (int i = 0; i < LineCount; i++)
                charsAccross = Math.Max(charsAccross, IndexToCol(GetLine(i), GetLine(i).Length));
            hScrollBar.Maximum = charsAccross;
            hScrollBar.LargeChange = Math.Max(1, CharsAcrossWindow);
            hScrollBar.Enabled = charsAccross > CharsAcrossWindow;
            hScrollBar.Visible = charsAccross > CharsAcrossWindow && charsAccross > 1;
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
            vMarksLeft.CursorMark = CursorLoc.Line;
            vMarksLeft.Maximum = linesInFile-1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SetupScrollBars();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            mFontSize = new SizeF(); // Flag to setup new font
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

            Rectangle cursorRect 
                = new Rectangle((int)(mFontSize.Width*column) + mOffset.X 
                                + 1 + (mOverwriteMode ? 2 : 0),
                                (int)(mFontSize.Height*CursorLoc.Line) + mOffset.Y + 1,
                                 mOverwriteMode && !HasSel() ? (int)mFontSize.Width : 2,
                                (int)mFontSize.Height-2);

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
            int oldCursorLine = cursor.Line;
            cursor.Line += lines;
            cursor.Line = Math.Min(LineCount-1, cursor.Line);
            cursor.Line = Math.Max(0, cursor.Line);

            if (cursor.Line == oldCursorLine)
                return cursor;

            // Set column index
            int oldArrowColumn = mCursorUpDownColumn;
            if (mCursorUpDownColumn < 0)
                mCursorUpDownColumn = IndexToCol(CursorLoc);

            if (oldArrowColumn >= 0)
                cursor.Char = ColToIndex(GetLine(cursor.Line), oldArrowColumn);
            else
                cursor.Char = ColToIndex(GetLine(cursor.Line), mCursorUpDownColumn);

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
            int marginY = Math.Min(2, Math.Max(0, LinesInWindow-4));
            int marginX = Math.Min(4, Math.Max(0, CharsAcrossWindow-5));

            // Ensure carret is on the screen when a key is pressed
            if (CursorLoc.Line < vScrollBar.Value+marginY)
                vScrollBar.Value = Math.Max(0, CursorLoc.Line-marginY);
            if (CursorLoc.Line > vScrollBar.Value + LinesInWindow-marginY)
                vScrollBar.Value = Math.Min(vScrollBar.Maximum, CursorLoc.Line-LinesInWindow+marginY);

            if (IndexToCol(CursorLoc) < hScrollBar.Value+marginX)
                hScrollBar.Value = Math.Max(0, IndexToCol(CursorLoc)-marginX);
            if (IndexToCol(CursorLoc) > hScrollBar.Value + CharsAcrossWindow - marginX)
                hScrollBar.Value = Math.Min(hScrollBar.Maximum, IndexToCol(CursorLoc)-CharsAcrossWindow+marginX);
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
                && undo.TextStart.Line == undo.TextEnd.Line
                && undo.TextStart.Char == undo.TextEnd.Char - 1
                // Previous undo is a delete on this line (max 12 chars)
                && fundo != null
                && (fundo.Text == null || fundo.Text.Length == 0)
                && fundo.TextStart.Line == fundo.TextEnd.Line
                && fundo.TextEnd.Char - fundo.TextStart.Char <= 12
                // Previous undo and this one match up
                && fundo.TextStart.Line == undo.TextStart.Line
                && fundo.TextEnd.Char == undo.TextEnd.Char-1)
            {
                // If inserting multiple single characters, group them
                fundo.TextEnd.Char++;
            }
            // If deleting just one char, try to append this undo operation 
            // to the previous one (group them in to one user operation)
            else if (hasDelete
                // Replacement text is empty
                && (replacementText == null || replacementText.Length == 0)
                // This undo adds exactly one char
                && undo.TextStart.Line == undo.TextEnd.Line
                && undo.TextStart.Char == undo.TextEnd.Char
                && undo.Text != null
                && undo.Text.Length == 1
                && undo.Text[0].Length == 1
                // Previous adds no more than 12 chars
                && fundo != null
                && fundo.Text != null 
                && fundo.Text.Length == 1
                && fundo.Text[0].Length < 12
                && fundo.TextStart.Line == fundo.TextEnd.Line
                && fundo.TextEnd.Char == fundo.TextStart.Char
                && fundo.TextStart.Line == undo.TextStart.Line
                // Previous undo and this one match up
                && fundo.TextStart.Line == undo.TextStart.Line
                && (fundo.TextEnd.Char - undo.TextEnd.Char == 1
                    || fundo.TextEnd.Char == undo.TextEnd.Char))
            {
                if (fundo.TextEnd.Char == undo.TextEnd.Char)
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
            TextChanged2?.Invoke(this, mEventArgs);
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
            TextChanged2?.Invoke(this, mEventArgs);

        }

        /// <summary>
        /// Setup tokens to be drawn.  Sets all tokens to
        /// default info (see static function GetDefaultTokenInfo)
        /// Deletes undo info.
        /// </summary>
        public Lexer Lexer
        {
            get { return mLexer; }
            set
            {
                mLexer = value;
                mUndo.Clear();
                mRedo.Clear();
                Invalidate();
                TextChanged2?.Invoke(this, mEventArgs);
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
            for (int line = Math.Max(0, selStart.Line);
                 line <= selEnd.Line && line < LineCount;
                 line++)
            {
                // Skip lines not in window
                float y = LocationY(line);
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
                if (line == selStart.Line)
                    x = LocationX(IndexToCol(selStart)) - TEXT_OFFSET_X;
                if (line == selEnd.Line)
                    xEnd = LocationX(IndexToCol(selEnd)) - TEXT_OFFSET_X;

                gr.FillRectangle(Focused ? mSelectColor : mSelectColorNoFocus,
                    new RectangleF(x, y, Math.Max(0, xEnd-x)+2, mFontSize.Height));
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
            float x = LocationX(col);
            float y = LocationY(token.Line);
            float xEnd = x + (int)(token.Name.Length*mFontSize.Width);
            float yEnd = y + (int)mFontSize.Height;

            // If it's under the test point, return it
            if (mTestPoint.X >= x && mTestPoint.X < xEnd
                && mTestPoint.Y >= y && mTestPoint.Y < yEnd)
            {
                mTestToken = token;
            }

            // Print the token
            if (gr != null)
            {
                if (background)
                {
                    // Find override
                    Brush backColor = null;
                    if (mTokenColorOverrides != null && mTokenColorOverrides.Length != 0)
                        foreach (TokenColorOverride over in mTokenColorOverrides)
                            if ((object)(over.Token) == (object)token)
                            {
                                backColor = over.BackColor;
                                break;
                            }
                    // Draw background color
                    if (backColor != null)
                        gr.FillRectangle(backColor, new RectangleF(x-1, y, xEnd-x+1, yEnd-y));
                    else if (token.Error)
                        gr.FillRectangle(mErrorColor, new RectangleF(x-1, y, xEnd-x+1, yEnd-y));
                }
                else
                {
                    // Adjust tabs only when necessary
                    int tabStartColumn = (mTabSize - col % mTabSize);
                    if (tabStartColumn != mTabStartColumnPrevious && token.Name.IndexOf('\t') >= 0)
                    {
                        mTabStartColumnPrevious = tabStartColumn;
                        mTabFormat.SetTabStops(tabStartColumn*mFontSize.Width, mTabSpacing);
                    }

                    // Draw the token
                    FontInfo fontInfo = GetFontInfo(token);
                    gr.DrawString(token.Name, fontInfo.Font, fontInfo.Brush, x-TEXT_OFFSET_X, y, mTabFormat);
                }
            }
        }

        /// <summary>
        /// Draw tokens on the screen (either the foreground or background).
        /// When gr is NULL, a test is performed to see if the token is
        /// under the cursor (mTestPoint) and the result is put in to mTestToken.
        /// </summary>
        void DrawScreen(Graphics gr, bool background)
        {
            // Get clipping region (entire screen when gr is NULL)
            float mMinY = -mFontSize.Height - 1;
            float mMaxY = ClientRectangle.Height + 1;
            if (gr != null)
            {
                // Optionally adjust clipping region
                mMinY = gr.VisibleClipBounds.Top - mFontSize.Height - 1;
                mMaxY = gr.VisibleClipBounds.Bottom + 1;
            }

            // Find first and last visible line
            int startLine = 0;
            while (startLine < LineCount && LocationY(startLine) < mMinY)
                startLine++;
            int endLine = startLine;
            while (endLine <= LineCount && LocationY(endLine) < mMaxY)
                endLine++;

            // Draw all tokens on the screen
            foreach (Token token in mLexer.GetEnumeratorStartAtLine(startLine))
            {
                // Quick exit when drawing below screen
                if (token.Line >= endLine)
                    break;

                DrawToken(gr, token, background);
            }			
        }

        /// <summary>
        /// Paint the screen
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Initialize fonts (or setup when changed)
            if (mFontSize == new SizeF())
            {
                // Measure the font size
                var normalFont = GetFontInfo(sNormalToken).Font;
                SizeF size1 = e.Graphics.MeasureString("MM\r\nMM", normalFont);
                SizeF size2 = e.Graphics.MeasureString("MMM\r\nMMM\r\nMMM", normalFont);
                mFontSize.Width = size2.Width - size1.Width;
                mFontSize.Height = (int)(size2.Height - size1.Height+1);
                for (int i = 0; i < mTabSpacing.Length; i++)
                    mTabSpacing[i] = mFontSize.Width*mTabSize;
                mTabSpacing[0] = 0;

                // Setup cursor
                UpdateCursorBlinker();
            }

            // Draw the graphics
            //e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            DrawScreen(e.Graphics, true);
            DrawSelection(e.Graphics);
            DrawScreen(e.Graphics, false);

            // Draw a rectangle over the hover token (red or gray)
            if (MouseHoverToken != null 
                && (MouseHoverToken.Type == eTokenType.Identifier))
            {
                Token token = MouseHoverToken;

                // Find token position and bounds
                int col = IndexToCol(token.Location);
                int x = (int)(LocationX(col));
                int y = (int)(LocationY(token.Line));
                int xEnd = x + (int)(token.Name.Length*mFontSize.Width);
                int yEnd = y + (int)mFontSize.Height;

                e.Graphics.DrawRectangle(token.Error ? Pens.Red : Pens.Gray,
                                            new Rectangle(x-1, y, xEnd-x+1, yEnd-y));
            }

            // Draw the cursor
            if (mCursorVisible)
            {
                // Draw the cursor
                e.Graphics.FillRectangle(Brushes.DarkBlue, mCursorRect);

                // Draw text under cursor in over-write mode
                if (mOverwriteMode && !HasSel()
                        && CursorLoc.Line < LineCount 
                        && CursorLoc.Char >= 0 
                        && CursorLoc.Char < GetLine(CursorLoc.Line).Length)
                {
                    float x = LocationX(IndexToCol(CursorLoc));
                    float y = LocationY(CursorLoc.Line);
                    e.Graphics.DrawString(GetLine(CursorLoc.Line)[CursorLoc.Char].ToString(),
                                            GetFontInfo(sNormalToken).Font, Brushes.White,
                                            x - TEXT_OFFSET_X, y);
                }
            }

            // Set the scroll bar properties
            // TBD - Do only when text is changed
            SetupScrollBars();

            base.OnPaint(e);
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
            cursor.Line = (int)((y - mOffset.Y)/mFontSize.Height);
            cursor.Line = Math.Min(cursor.Line, LineCount-1);
            cursor.Line = Math.Max(cursor.Line, 0);
            cursor.Char = ColToIndex(GetLine(cursor.Line),
                                     (int)((e.X - mOffset.X)/mFontSize.Width+0.5f));
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

        /// <summary>
        /// Select a single token when the user double clicks
        /// </summary>
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            mMouseDown = false;
            Invalidate();
            SetCursorByMouse(e);

            // Empty line?
            string line = GetLine(CursorLoc.Line);
            int startIndex = Math.Min(CursorLoc.Char, line.Length-1);
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
                mSelStart.Char = startIndex;
                mSelEnd.Char = endIndex;
                CursorLoc = new TokenLoc(CursorLoc.Line, endIndex);
                UpdateCursorBlinker();
            }


            base.OnMouseDoubleClick(e);
        }

        /// <summary>
        /// Update the hover token or selected text when the mouse moves
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            // Draw to NULL graphics to find the point
            mTestPoint = new Point(e.X, e.Y);
            mTestToken = null;
            DrawScreen(null, true);

            // Set new mouse hover token
            if (mTestToken != mLastMouseHoverToken)
            {
                Token previousToken = mLastMouseHoverToken;
                mLastMouseHoverToken = mTestToken;
                MouseTokenChanged?.Invoke(this, previousToken, mLastMouseHoverToken);
                Invalidate();
            }

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
            if (mLastMouseHoverToken != null)
            {
                Token previousToken = mLastMouseHoverToken;
                mLastMouseHoverToken = null;
                MouseTokenChanged?.Invoke(this, previousToken, mLastMouseHoverToken);
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        /// <summary>
        /// User scrolls text
        /// </summary>
        void ScrollBar_Changed()
        {
            mOffset = new Point(-(int)(hScrollBar.Value*mFontSize.Width),
                                -(int)(vScrollBar.Value*mFontSize.Height));
            UpdateCursorBlinker();
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

            // Don't do anything for SHIFT, CTRL, or ALT keys
            if (e.KeyValue == 16 || e.KeyValue == 17 || e.KeyValue == 18)
                return;

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
                    while (--i > 0 && cursor.Char < GetLine(cursor.Line).Length
                            && char.IsWhiteSpace(GetLine(cursor.Line)[cursor.Char]))
                        cursor = CharIndexInc(cursor);
                    while (--i > 0 && cursor.Char < GetLine(cursor.Line).Length
                            && char.IsLetterOrDigit(GetLine(cursor.Line)[cursor.Char]))
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
                    while (--i > 0 && cursor.Char > 0
                            && char.IsWhiteSpace(GetLine(cursor.Line)[cursor.Char-1]))
                        cursor = CharIndexDec(cursor);
                    while (--i > 0 && cursor.Char > 0
                            && char.IsLetterOrDigit(GetLine(cursor.Line)[cursor.Char-1]))
                        cursor = CharIndexDec(cursor);
                }
                MoveCursor(cursor, true, e.Shift);
                mCursorUpDownColumn = -1;
            }
            // HOME
            if (e.KeyCode == Keys.Home)
            {
                // Find first non-white space
                string line = GetLine(CursorLoc.Line);
                int firstText = 0;
                while (firstText < GetLine(CursorLoc.Line).Length
                            && char.IsWhiteSpace(line, firstText))
                    firstText++;

                // Go home, or to beginning of text
                TokenLoc newCursor = CursorLoc;
                if (newCursor.Char > firstText || newCursor.Char == 0)
                    newCursor.Char = firstText;
                else
                    newCursor.Char = 0;

                // CTRL-HOME goes to beginning
                if (e.Control)
                    newCursor = new TokenLoc();
                MoveCursor(newCursor, true, e.Shift);
                mCursorUpDownColumn = -1;
            }
            // END
            if (e.KeyCode == Keys.End)
            {
                TokenLoc newCursor = new TokenLoc(CursorLoc.Line, 0);
                if (e.Control)
                    newCursor = new TokenLoc(LineCount-1, 0);
                newCursor.Char = GetLine(newCursor.Line).Length;
                MoveCursor(newCursor, true, e.Shift);
                mCursorUpDownColumn = -1;
            }
            // PAGE UP
            if (e.KeyCode == Keys.PageUp && !e.Control)
            {
                int linesInWindow = Math.Max(1, LinesInWindow);
                vScrollBar.Value = Math.Max(0, vScrollBar.Value - linesInWindow);
                MoveCursor(ArrowKeyDown(CursorLoc, -linesInWindow), true, e.Shift);
            }
            // PAGE DOWN
            if (e.KeyCode == Keys.PageDown && !e.Control)
            {
                int linesInWindow = Math.Max(1, LinesInWindow);
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
                CursorLoc = new TokenLoc(LineCount-1, GetLine(LineCount-1).Length);
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
            for (int i = mSelStart.Line; i <= mSelEnd.Line; i++)
                lines.Add(GetLine(i));

            // Add or remove spaces
            for (int i = 0; i < lines.Count; i++)
            {
                // If there is nothing on the last line, don't bother moving
                if (i != 0 && i == lines.Count-1 && mSelEnd.Char == 0)
                    break;

                int moveCh = 0;
                string line = lines[i];
                if (!shift)
                {
                    // Append '\t' to start of line
                    line = "\t" + line;
                    moveCh = 1;
                }
                else
                {
                    // Remove first '\t' from line (if within TabSize zone)
                    for (int col = 0; col < line.Length &&  col < TabSize
                                        && (line[col] == '\t' || line[col] == ' '); col++)
                    {
                        if (line[col] == '\t')
                        {
                            line = line.Substring(0, col)
                                    + new string(' ', TabSize - col)
                                    + line.Substring(col+1);
                            moveCh = TabSize-col-1;
                            break;
                        }
                    }
                    // Remove TabSize spaces from line
                    int spaceIndex = 0;
                    while (spaceIndex < line.Length
                            && line[spaceIndex] == ' '
                            && spaceIndex < TabSize)
                        spaceIndex++;
                    line = line.Substring(spaceIndex);
                    moveCh -= spaceIndex;
                }
                lines[i] = line;

                // Mover cursor and selection to match inserted/removed chars
                if (CursorLoc.Line == mSelStart.Line + i)
                    CursorLoc = new TokenLoc(CursorLoc.Line, Math.Max(0, CursorLoc.Char+moveCh));
                if (mSelStart.Line == mSelStart.Line + i)
                    mSelStart.Char = Math.Max(0, mSelStart.Char+moveCh);
                if (mSelEnd.Line == mSelStart.Line + i)
                    mSelEnd.Char = Math.Max(0, mSelEnd.Char+moveCh);
            }

            ReplaceText(lines.ToArray(), new TokenLoc(mSelStart.Line, 0),
                                         new TokenLoc(mSelEnd.Line, GetLine(mSelEnd.Line).Length));

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
                end.Char++;

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
                string line = GetLine(CursorLoc.Line);
                int i = 0;
                while (i < line.Length && i < CursorLoc.Char
                            && char.IsWhiteSpace(line, i))
                    i++;
                insert = mInsertCR;
                mInsertCR[1] = line.Substring(0, i);
            }
            else
            {
                // Insert a single char
                insert = mInsertOneChar;
                mInsertOneChar[0] = char.ToString(e.KeyChar);
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
            int linesInWindow = Math.Max(1, LinesInWindow);

            // While selecting text, scroll the screen
            if (mMouseDown && CursorLoc.Line - vScrollBar.Value > linesInWindow
                        && vScrollBar.Value < vScrollBar.Maximum -linesInWindow)
                vScrollBar.Value++;
            else if (mMouseDown && CursorLoc.Line < vScrollBar.Value
                        && vScrollBar.Value > 0)
                vScrollBar.Value--;

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
        public Token		Token;
        public Brush		BackColor;

        public TokenColorOverride(Token token, Brush backColor)
        {
            Token = token;
            BackColor = backColor;
        }
    }
}

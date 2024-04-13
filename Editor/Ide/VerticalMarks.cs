using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

namespace Zurfur.Ide;


public struct VerticalMarkInfo
{
    public int Start;
    public int Length;
    public Color Color;
}

/// <summary>
/// Quick way to display info on V scroll bar.  Would be better to just re-implement the scroll bar.
/// </summary>
class VerticalMarks : Control
{
    VerticalMarkInfo[] mMarks = new VerticalMarkInfo[0];
    int mArrowHeight;
    int mCursorMark;
    int mMaximum;
    bool mShowCursor;

    public VerticalMarks()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
    }

    public int ArrowHight
    {
        get { return mArrowHeight; }
        set
        {
            if (mArrowHeight == value)
                return;
            mArrowHeight = value;
            Invalidate();
        }
    }

    public int Maximum
    {
        get { return mMaximum; }
        set
        {
            if (mMaximum == value)
                return;
            mMaximum = value;
            Invalidate();
        }
    }

    public void SetMarks(VerticalMarkInfo[] marks)
    {
        mMarks = marks;
        Invalidate();
    }

    public int CursorMark
    {
        get { return mCursorMark; }
        set
        {
            if (mCursorMark == value)
                return;
            mCursorMark = value;
            Invalidate();
        }
    }

    public bool ShowCursor
    {
        get { return mShowCursor; }
        set
        {
            if (value != mShowCursor)
            {
                mShowCursor = value;
                Invalidate();
            }
        }
    }

    float LineHeight {  get { return 1 / (float)Math.Max(1, Maximum); } }
    
    int LineToPixel(int line)
    {
        return (int)(line / (float)Math.Max(1, Maximum) * ((Height-ArrowHight*2-1))) + ArrowHight;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Draw background
        var brush = new SolidBrush(SystemColors.Control);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        brush.Dispose();

        // Draw marks
        foreach (var mark in mMarks)
        {
            brush = new SolidBrush(mark.Color);
            e.Graphics.FillRectangle(brush, new Rectangle(0, LineToPixel(mark.Start), Width, 3));
            brush.Dispose();
        }

        // Draw cursor
        if (mShowCursor)
        {
            brush = new SolidBrush(Color.Blue);
            e.Graphics.FillRectangle(brush, new Rectangle(0, LineToPixel(CursorMark), Width, 3));
            brush.Dispose();
        }
    }       
}

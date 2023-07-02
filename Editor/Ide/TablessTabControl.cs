using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace Zurfur.Ide
{
    /// <summary>
    /// A tab control that can hide its tabs so it's easy to
    /// create wizards or panels that change at run time.
    /// </summary>
    public class TablessTabControl : System.Windows.Forms.TabControl
    {
        bool mShowTabsDesignMode = true;
        bool mShowTabs = true;

        /// <summary>
        /// Toggle this in the designer to quickly see what the
        /// tab will look like at run time without tabs.
        /// </summary>
        public bool ShowTabsDesignMode
        {
            get { return mShowTabsDesignMode; }
            set
            {
                if (value == mShowTabsDesignMode)
                    return;
                mShowTabsDesignMode = value;
                ForceDisplayRectangleUpdate();
            }
        }

        /// <summary>
        /// Toggle this at run time to show tabs
        /// </summary>
        public bool ShowTabs
        {
            get { return mShowTabs; }
            set
            {
                if (value == mShowTabs)
                    return;
                mShowTabs = value;
                ForceDisplayRectangleUpdate();
            }
        }

        public override Rectangle DisplayRectangle
        {
            get
            {
                if (DesignMode && mShowTabsDesignMode || !DesignMode && mShowTabs)
                {
                    return base.DisplayRectangle;
                }
                return new Rectangle(0, 0, Width, Height);
            }
        }

        void ForceDisplayRectangleUpdate()
        {
            Width++;
            Width--;
        }
    }
}

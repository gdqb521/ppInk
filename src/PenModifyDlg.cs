﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace gInk
{
    public partial class PenModifyDlg : Form
    {
        public PenModifyDlg(Root Root)
        {
            InitializeComponent();
            OkBtn.Text = Root.Local.ButtonOkText;
            CancelBtn.Text = Root.Local.ButtonCancelText;
        }

        public void setColor(int alpha,Color c)
        {
            colorEditorManager.Color = Color.FromArgb(255-alpha, c);
        }

        public int getAlpha()
        {
            return 255-colorEditorManager.Color.A;
        }

        public Color getColor()
        {
            return colorEditorManager.Color;
        }

        public void setWidth(float w)
        {
            pboxPenWidthIndicator.Left = (int)(Math.Sqrt(w / 500)* 200);
        }

        public float getWidth()
        {
            double f = (pboxPenWidthIndicator.Left/ 200.0);
            return (float)(f * f * 500);
        }

        public bool ModifyPen(ref Microsoft.Ink.DrawingAttributes pen)
        {
            setColor(pen.Transparency, pen.Color);
            setWidth(pen.Width);
            if (ShowDialog() == DialogResult.OK)
            {
                pen.Color = getColor();
                pen.Transparency = (byte)getAlpha();
                pen.Width = getWidth();
                return true;
            }
            else
            {
                return false;
            }
        }
        /// inspired from FormCollection
        bool gpPenWidth_MouseOn;
        private void gpPenWidth_MouseDown(object sender, MouseEventArgs e)
        {
            gpPenWidth_MouseOn = true;
        }

        private void gpPenWidth_MouseMove(object sender, MouseEventArgs e)
        {
            if (!gpPenWidth_MouseOn)
                return;
            if (e.X < pboxPenWidthIndicator.Width/2)
                pboxPenWidthIndicator.Left = 0;
            else if (e.X > (gpPenWidth.Width - pboxPenWidthIndicator.Width/2))
                pboxPenWidthIndicator.Left = (gpPenWidth.Width - pboxPenWidthIndicator.Width/2);
            else
                pboxPenWidthIndicator.Left = e.X - pboxPenWidthIndicator.Width / 2;
        }

        private void gpPenWidth_MouseUp(object sender, MouseEventArgs e)
        {
            gpPenWidth_MouseMove(sender, e);
            gpPenWidth_MouseOn = false;
        }

        private void pboxPenWidthIndicator_MouseDown(object sender, MouseEventArgs e)
        {
            gpPenWidth_MouseOn = true;
        }

        private void pboxPenWidthIndicator_MouseMove(object sender, MouseEventArgs e)
        {
            if (gpPenWidth_MouseOn)
                gpPenWidth_MouseMove(sender, new MouseEventArgs(e.Button, e.Clicks, e.X + (sender as Control).Left, e.Y, e.Delta));
        }

        private void pboxPenWidthIndicator_MouseUp(object sender, MouseEventArgs e)
        {
            gpPenWidth_MouseOn = false;
        }

        private void colorEditorManager_ColorChanged(object sender, EventArgs e)
        {
           gpPenWidth.BackColor = colorEditorManager.Color;
        }
    }
}

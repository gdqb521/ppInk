using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
//using System.Windows.Input;
using Microsoft.Ink;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using System.Reflection;

namespace gInk
{
    public partial class FormCollection : Form
	{
        [Flags, Serializable]
        public enum RegisterTouchFlags
        {
            TWF_NONE = 0x00000000,
            TWF_FINETOUCH = 0x00000001, //Specifies that hWnd prefers noncoalesced touch input.
            TWF_WANTPALM = 0x00000002 //Setting this flag disables palm rejection which reduces delays for getting WM_TOUCH messages.
        }
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterTouchWindow(IntPtr hWnd, RegisterTouchFlags flags);

        // to load correctly customed cursor file
        static class MyNativeMethods
        {
            public static System.Windows.Forms.Cursor LoadCustomCursor(string path)
            {
                IntPtr hCurs = LoadCursorFromFile(path);
                if (hCurs == IntPtr.Zero) throw new Win32Exception();
                var curs = new System.Windows.Forms.Cursor(hCurs);
                // Note: force the cursor to own the handle so it gets released properly
                //var fi = typeof(System.Windows.Forms.Cursor).GetField("ownHandle", BindingFlags.NonPublic | BindingFlags.Instance);
                //fi.SetValue(curs, true);
                return curs;
            }
            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            private static extern IntPtr LoadCursorFromFile(string path);
        }

        // hotkeys
        const int VK_LCONTROL = 0xA2;
        const int VK_RCONTROL = 0xA3;
        const int VK_LSHIFT = 0xA0;
        const int VK_RSHIFT = 0xA1;
        const int VK_LMENU = 0xA4;
        const int VK_RMENU = 0xA5;
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;
        private PenModifyDlg PenModifyDlg;
        public Root Root;
		public InkOverlay IC;

		public Button[] btPen;
		public Bitmap image_exit, image_clear, image_undo, image_snap, image_penwidth;
		public Bitmap image_dock, image_dockback;
		public Bitmap image_pencil, image_highlighter, image_pencil_act, image_highlighter_act;
		public Bitmap image_pointer, image_pointer_act;
		public Bitmap[] image_pen;
		public Bitmap[] image_pen_act;
        public Bitmap image_eraser_act, image_eraser;
		public Bitmap image_visible_not, image_visible;
		public System.Windows.Forms.Cursor cursorred, cursorsnap,cursorerase;
		public System.Windows.Forms.Cursor cursortip;
        public System.Windows.Forms.Cursor tempArrowCursor=null;

        public DateTime MouseTimeDown;
        public object MouseDownButtonObject;
        public int ButtonsEntering = 0;  // -1 = exiting
		public int gpButtonsLeft, gpButtonsTop, gpButtonsWidth, gpButtonsHeight; // the default location, fixed

		public bool gpPenWidth_MouseOn = false;

		public int PrimaryLeft, PrimaryTop;

        private int LastPenSelected=0;
        private int SavedTool = -1;
        private int SavedFilled = -1;
        private int SavedPen = -1;

        // we have local variables for font to have an session limited default font characteristics
        public int TextSize = 25;
        public string TextFont = "Arial";
        public bool TextItalic = false;
        public bool TextBold = false;

        // http://www.csharp411.com/hide-form-from-alttab/
        protected override CreateParams CreateParams
		{
			get
			{
				CreateParams cp = base.CreateParams;
				// turn on WS_EX_TOOLWINDOW style bit
				cp.ExStyle |= 0x80;
				return cp;
			}
		}
        void PreparePenImages(int Transparency, ref Bitmap img_pen, ref Bitmap img_pen_act)
        {
            if (Transparency >= 100)
            {
                img_pen = image_highlighter;
                img_pen_act = image_highlighter_act;
            }
            else
            {
                img_pen = image_pencil;
                img_pen_act = image_pencil_act;
            }
        }

        static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);

            //[DllImport("kernel32.dll")]
            //public static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

            [DllImport("kernel32.dll")]
            public static extern uint SuspendThread(IntPtr hThread);

            [DllImport("kernel32.dll")]
            public static extern uint ResumeThread(IntPtr hThread);
        }

        public FormCollection(Root root)
        {
            Root = root;
            InitializeComponent();

            //loading default params
            TextFont = Root.TextFont;
            TextBold = Root.TextBold;
            TextItalic = Root.TextItalic;
            TextSize = Root.TextSize;

            longClickTimer.Interval = (int)(Root.LongClickTime * 1000 +100);
            if (Root.MagneticRadius>0)
                this.btMagn.BackgroundImage = global::gInk.Properties.Resources.Magnetic_act;
            else
                this.btMagn.BackgroundImage = global::gInk.Properties.Resources.Magnetic;

            PrimaryLeft = Screen.PrimaryScreen.Bounds.Left - SystemInformation.VirtualScreen.Left;
            PrimaryTop = Screen.PrimaryScreen.Bounds.Top - SystemInformation.VirtualScreen.Top;

            gpButtons.Height = (int)(Screen.PrimaryScreen.Bounds.Height * Root.ToolbarHeight);
            btClear.Height = (int)(gpButtons.Height * 0.85);
            btClear.Width = btClear.Height;
            btClear.Top = (int)(gpButtons.Height * 0.08);
            btVideo.Height = (int)(gpButtons.Height * 0.85);
            btVideo.Width = btVideo.Height;
            btVideo.Top = (int)(gpButtons.Height * 0.08);
            btDock.Height = (int)(gpButtons.Height * 0.85);
            btDock.Width = btDock.Height / 2;
            btDock.Top = (int)(gpButtons.Height * 0.08);

            btHand.Height = (int)(gpButtons.Height * 0.48);
            btHand.Width = btHand.Height;
            btHand.Top = (int)(gpButtons.Height * 0.02);
            btLine.Height = (int)(gpButtons.Height * 0.48);
            btLine.Width = btLine.Height;
            btLine.Top = (int)(gpButtons.Height * 0.52);
            btRect.Height = (int)(gpButtons.Height * 0.48);
            btRect.Width = btRect.Height;
            btRect.Top = (int)(gpButtons.Height * 0.02);
            btOval.Height = (int)(gpButtons.Height * 0.48);
            btOval.Width = btOval.Height;
            btOval.Top = (int)(gpButtons.Height * 0.52);
            btArrow.Height = (int)(gpButtons.Height * 0.48);
            btArrow.Width = btArrow.Height;
            btArrow.Top = (int)(gpButtons.Height * 0.02);
            btNumb.Height = (int)(gpButtons.Height * 0.48);
            btNumb.Width = btNumb.Height;
            btNumb.Top = (int)(gpButtons.Height * 0.52);
            btText.Height = (int)(gpButtons.Height * 0.48);
            btText.Width = btText.Height;
            btText.Top = (int)(gpButtons.Height * 0.02);
            btEdit.Height = (int)(gpButtons.Height * 0.48);
            btEdit.Width = btEdit.Height;
            btEdit.Top = (int)(gpButtons.Height * 0.52);


            btEraser.Height = (int)(gpButtons.Height * 0.85);
            btEraser.Width = btEraser.Height;
            btEraser.Top = (int)(gpButtons.Height * 0.08);
            btInkVisible.Height = (int)(gpButtons.Height * 0.85);
            btInkVisible.Width = btInkVisible.Height;
            btInkVisible.Top = (int)(gpButtons.Height * 0.08);
            btPan.Height = (int)(gpButtons.Height * 0.85);
            btPan.Width = btPan.Height;
            btPan.Top = (int)(gpButtons.Height * 0.08);
            btMagn.Height = (int)(gpButtons.Height * 0.85);
            btMagn.Width = btMagn.Height;
            btMagn.Top = (int)(gpButtons.Height * 0.08);
            btPointer.Height = (int)(gpButtons.Height * 0.85);
            btPointer.Width = btPointer.Height;
            btPointer.Top = (int)(gpButtons.Height * 0.08);
            btSnap.Height = (int)(gpButtons.Height * 0.85);
            btSnap.Width = btSnap.Height;
            btSnap.Top = (int)(gpButtons.Height * 0.08);
            btStop.Height = (int)(gpButtons.Height * 0.85);
            btStop.Width = btStop.Height;
            btStop.Top = (int)(gpButtons.Height * 0.08);
            btUndo.Height = (int)(gpButtons.Height * 0.85);
            btUndo.Width = btUndo.Height;
            btUndo.Top = (int)(gpButtons.Height * 0.08);

            btPen = new Button[Root.MaxPenCount];

            int cumulatedleft = (int)(btDock.Width * 2.5);
            for (int b = 0; b < Root.MaxPenCount; b++)
            {
                btPen[b] = new Button();
                btPen[b].Name = string.Format("pen{0}", b);
                btPen[b].Width = (int)(gpButtons.Height * 0.85);
                btPen[b].Height = (int)(gpButtons.Height * 0.85);
                btPen[b].Top = (int)(gpButtons.Height * 0.08);
                btPen[b].FlatAppearance.BorderColor = System.Drawing.Color.WhiteSmoke;
                btPen[b].FlatAppearance.BorderSize = 3;
                btPen[b].FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(250, 50, 50);
                btPen[b].FlatStyle = System.Windows.Forms.FlatStyle.Flat;
                btPen[b].ForeColor = System.Drawing.Color.Transparent;
                //btPen[b].Name = "btPen" + b.ToString();
                btPen[b].UseVisualStyleBackColor = false;
                
                btPen[b].ContextMenu = new ContextMenu();
                btPen[b].ContextMenu.Popup += new System.EventHandler(this.btColor_Click);
                btPen[b].Click += new System.EventHandler(this.btColor_Click);

                btPen[b].BackColor = Root.PenAttr[b].Color;
                btPen[b].FlatAppearance.MouseDownBackColor = Root.PenAttr[b].Color;
                btPen[b].FlatAppearance.MouseOverBackColor = Root.PenAttr[b].Color;
                this.toolTip.SetToolTip(this.btPen[b], Root.Local.ButtonNamePen[b] + " (" + Root.Hotkey_Pens[b].ToString() + ")");

                btPen[b].MouseDown += gpButtons_MouseDown;
                btPen[b].MouseMove += gpButtons_MouseMove;
                btPen[b].MouseUp += gpButtons_MouseUp;

                gpButtons.Controls.Add(btPen[b]);
                if (Root.PenEnabled[b])
                {
                    btPen[b].Visible = true;
                    btPen[b].Left = cumulatedleft;
                    cumulatedleft += (int)(btPen[b].Width * 1.1);
                }
                else
                {
                    btPen[b].Visible = false;
                }
            }
            cumulatedleft += (int)(btDock.Width * 0.2);
            if (Root.ToolsEnabled)
            {
                btHand.Visible = true;
                btLine.Visible = true;
                btHand.Left = cumulatedleft;
                btLine.Left = cumulatedleft;
                cumulatedleft += (int)(btHand.Width * 1.1);
                btRect.Visible = true;
                btOval.Visible = true;
                btRect.Left = cumulatedleft;
                btOval.Left = cumulatedleft;
                cumulatedleft += (int)(btRect.Width * 1.1);
                btArrow.Visible = true;
                btArrow.Left = cumulatedleft;
                btNumb.Visible = true;
                btNumb.Left = cumulatedleft;
                cumulatedleft += (int)(btArrow.Width * 1.1);
                btText.Visible = true;
                btText.Left = cumulatedleft;
                btEdit.Visible = true;
                btEdit.Left = cumulatedleft;
                cumulatedleft += (int)(btArrow.Width * 1.1);
            }
            else
            {
                btHand.Visible = false;
                btLine.Visible = false;
                btRect.Visible = false;
                btOval.Visible = false;
                btArrow.Visible = false;
                btNumb.Visible = false;
                btText.Visible = false;
                btEdit.Visible = false;
            }

            cumulatedleft += (int)(btDock.Width * 0.5);
            if (Root.EraserEnabled)
            {
                btEraser.Visible = true;
                btEraser.Left = cumulatedleft;
                cumulatedleft += (int)(btEraser.Width * 1.1);
            }
            else
            {
                btEraser.Visible = false;
            }
            if (Root.PanEnabled)
            {
                btPan.Visible = true;
                btPan.Left = cumulatedleft;
                cumulatedleft += (int)(btPan.Width * 1.1);
            }
            else
            {
                btPan.Visible = false;
            }
            if (Root.ToolsEnabled)
            {
                btMagn.Visible = true;
                btMagn.Left = cumulatedleft;
                cumulatedleft += (int)(btMagn.Width * 1.1);
            }
            else
            {
                btMagn.Visible = false;
            }
            if (Root.PointerEnabled)
            {
                btPointer.Visible = true;
                btPointer.Left = cumulatedleft;
                cumulatedleft += (int)(btPointer.Width * 1.1);
            }
            else
            {
                btPointer.Visible = false;
            }
            cumulatedleft += (int)(btDock.Width * 1.5);
            if (Root.PenWidthEnabled)
            {
                btPenWidth.Visible = true;
                btPenWidth.Height = (int)(gpButtons.Height * 0.85);
                btDock.Width = btDock.Height;
                btPenWidth.Left = cumulatedleft;
                cumulatedleft += (int)(btPenWidth.Width * 1.1);
            }
            else
            {
                btPenWidth.Visible = false;
            }
            if (Root.InkVisibleEnabled)
            {
                btInkVisible.Visible = true;
                btInkVisible.Left = cumulatedleft;
                cumulatedleft += (int)(btInkVisible.Width * 1.1);
            }
            else
            {
                btInkVisible.Visible = false;
            }
            if (Root.SnapEnabled)
            {
                btSnap.Visible = true;
                btSnap.Left = cumulatedleft;
                cumulatedleft += (int)(btSnap.Width * 1.1);
            }
            else
            {
                btSnap.Visible = false;
            }
            if (Root.UndoEnabled)
            {
                btUndo.Visible = true;
                btUndo.Left = cumulatedleft;
                cumulatedleft += (int)(btUndo.Width * 1.1);
            }
            else
            {
                btUndo.Visible = false;
            }
            if (Root.ClearEnabled)
            {
                btClear.Visible = true;
                btClear.Left = cumulatedleft;
                cumulatedleft += (int)(btClear.Width * 1.1);
            }
            else
            {
                btClear.Visible = false;
            }
            if (Root.VideoRecordMode>0)
            {
                btVideo.Visible = true;
                btVideo.Left = cumulatedleft;
                SetVidBgImage();
                if (Root.VideoRecordMode == VideoRecordMode.OBSBcst || Root.VideoRecordMode == VideoRecordMode.OBSRec)
                {

                    if (Root.ObsRecvTask == null || Root.ObsRecvTask.IsCompleted)
                    {
                        Root.VideoRecordWindowInProgress = true;
                        Root.ObsRecvTask = Task.Run(() => ReceiveObsMesgs(this));
                    }
                    while (Root.VideoRecordWindowInProgress)
                        Task.Delay(50);
                    Task.Delay(100);
                    //Task.Run(() => SendInWs(Root.ObsWs, "GetRecordingStatus", new CancellationToken()));
                    Task.Run(() => SendInWs(Root.ObsWs, "GetStreamingStatus", new CancellationToken()));
                }
                cumulatedleft += (int)(btClear.Width * 1.1);
            }
            else
            {
                btVideo.Visible = false;
            }
            cumulatedleft += (int)(btDock.Width * .4);
            btStop.Left = cumulatedleft;
            gpButtons.Width = (int)(btStop.Right + btDock.Width*.05);


            this.Left = SystemInformation.VirtualScreen.Left;
            this.Top = SystemInformation.VirtualScreen.Top;
            //int targetbottom = 0;
            //foreach (Screen screen in Screen.AllScreens)
            //{
            //	if (screen.WorkingArea.Bottom > targetbottom)
            //		targetbottom = screen.WorkingArea.Bottom;
            //}
            //int virwidth = SystemInformation.VirtualScreen.Width;
            //this.Width = virwidth;
            //this.Height = targetbottom - this.Top;
            this.Width = SystemInformation.VirtualScreen.Width;
            this.Height = SystemInformation.VirtualScreen.Height - 2;
            this.DoubleBuffered = true;

            gpButtonsWidth = gpButtons.Width;
            gpButtonsHeight = gpButtons.Height;
            if (true || Root.AllowDraggingToolbar)
            {
                gpButtonsLeft = Root.gpButtonsLeft;
                gpButtonsTop = Root.gpButtonsTop;
                if
                (
                    !(IsInsideVisibleScreen(gpButtonsLeft, gpButtonsTop) &&
                    IsInsideVisibleScreen(gpButtonsLeft + gpButtonsWidth, gpButtonsTop) &&
                    IsInsideVisibleScreen(gpButtonsLeft, gpButtonsTop + gpButtonsHeight) &&
                    IsInsideVisibleScreen(gpButtonsLeft + gpButtonsWidth, gpButtonsTop + gpButtonsHeight))
                    ||
                    (gpButtonsLeft == 0 && gpButtonsTop == 0)
                )
                {
                    gpButtonsLeft = Screen.PrimaryScreen.WorkingArea.Right - gpButtons.Width + PrimaryLeft;
                    gpButtonsTop = Screen.PrimaryScreen.WorkingArea.Bottom - gpButtons.Height - 15 + PrimaryTop;
                }
            }
            else
            {
                gpButtonsLeft = Screen.PrimaryScreen.WorkingArea.Right - gpButtons.Width + PrimaryLeft;
                gpButtonsTop = Screen.PrimaryScreen.WorkingArea.Bottom - gpButtons.Height - 15 + PrimaryTop;
            }

            gpButtons.Left = gpButtonsLeft + gpButtons.Width;
            gpButtons.Top = gpButtonsTop;
            gpPenWidth.Left = gpButtonsLeft + btPenWidth.Left - gpPenWidth.Width / 2 + btPenWidth.Width / 2;
            gpPenWidth.Top = gpButtonsTop - gpPenWidth.Height - 10;

            pboxPenWidthIndicator.Top = 0;
            pboxPenWidthIndicator.Left = (int)Math.Sqrt(Root.GlobalPenWidth * 30);
            gpPenWidth.Controls.Add(pboxPenWidthIndicator);

            IC = new InkOverlay(this.Handle);
            IC.CollectionMode = CollectionMode.InkOnly;
            IC.AutoRedraw = false;
            IC.DynamicRendering = false;
            IC.EraserMode = InkOverlayEraserMode.StrokeErase;
            IC.CursorInRange += IC_CursorInRange;
            IC.MouseDown += IC_MouseDown;
            IC.MouseMove += IC_MouseMove;
            IC.MouseUp += IC_MouseUp;
            IC.CursorDown += IC_CursorDown;
            IC.MouseWheel += IC_MouseWheel;
            IC.Stroke += IC_Stroke;
            IC.DefaultDrawingAttributes.Width = 80;
            IC.DefaultDrawingAttributes.Transparency = 30;
            IC.DefaultDrawingAttributes.AntiAliased = true;
            IC.DefaultDrawingAttributes.FitToCurve = true;

            string icon_filename= Root.ProgramFolder + Path.DirectorySeparatorChar + "cursor";
            if (File.Exists(icon_filename+".cur")) 
                cursorred = MyNativeMethods.LoadCustomCursor(icon_filename+".cur");
            else if (File.Exists(icon_filename + ".ani"))
                cursorred = MyNativeMethods.LoadCustomCursor(icon_filename + ".ani");
            else if (File.Exists(icon_filename + ".ico"))
                cursorred = new System.Windows.Forms.Cursor(icon_filename+".ico");
            else
                cursorred = new System.Windows.Forms.Cursor(gInk.Properties.Resources.cursorred.Handle);

            icon_filename = Root.ProgramFolder + Path.DirectorySeparatorChar + "eraser";
            if (File.Exists(icon_filename + ".cur"))
                cursorerase = MyNativeMethods.LoadCustomCursor(icon_filename + ".cur");
            else if (File.Exists(icon_filename + ".ani"))
                cursorerase = MyNativeMethods.LoadCustomCursor(icon_filename + ".ani");
            else if (File.Exists(icon_filename + ".ico"))
                cursorerase = new System.Windows.Forms.Cursor(icon_filename + ".ico");
            else
                cursorerase = new System.Windows.Forms.Cursor(gInk.Properties.Resources.cursoreraser.Handle);

            IC.Enabled = true;

            image_exit = new Bitmap(btStop.Width, btStop.Height);
            Graphics g = Graphics.FromImage(image_exit);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.exit, 0, 0, btStop.Width, btStop.Height);
            btStop.Image = image_exit;
            image_clear = new Bitmap(btClear.Width, btClear.Height);
            g = Graphics.FromImage(image_clear);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.garbage, 0, 0, btClear.Width, btClear.Height);
            //btClear.Image = image_clear;
            image_undo = new Bitmap(btUndo.Width, btUndo.Height);
            g = Graphics.FromImage(image_undo);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.undo, 0, 0, btUndo.Width, btUndo.Height);
            btUndo.Image = image_undo;
            image_eraser_act = new Bitmap(btEraser.Width, btEraser.Height);
            g = Graphics.FromImage(image_eraser_act);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.eraser_act, 0, 0, btEraser.Width, btEraser.Height);
            image_eraser = new Bitmap(btEraser.Width, btEraser.Height);
            g = Graphics.FromImage(image_eraser);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.eraser, 0, 0, btEraser.Width, btEraser.Height);
            btEraser.Image = image_eraser;

            image_visible_not = new Bitmap(btInkVisible.Width, btInkVisible.Height);
            g = Graphics.FromImage(image_visible_not);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.visible_not, 0, 0, btInkVisible.Width, btInkVisible.Height);
            image_visible = new Bitmap(btInkVisible.Width, btInkVisible.Height);
            g = Graphics.FromImage(image_visible);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.visible, 0, 0, btInkVisible.Width, btInkVisible.Height);
            btInkVisible.Image = image_visible;

            image_snap = new Bitmap(btSnap.Width, btSnap.Height);
            g = Graphics.FromImage(image_snap);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.snap, 0, 0, btSnap.Width, btSnap.Height);
            btSnap.Image = image_snap;
            image_penwidth = new Bitmap(btPenWidth.Width, btPenWidth.Height);
            g = Graphics.FromImage(image_penwidth);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.penwidth, 0, 0, btPenWidth.Width, btPenWidth.Height);
            btPenWidth.Image = image_penwidth;
            image_dock = new Bitmap(btDock.Width, btDock.Height);
            g = Graphics.FromImage(image_dock);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.dock, 0, 0, btDock.Width, btDock.Height);
            image_dockback = new Bitmap(btDock.Width, btDock.Height);
            g = Graphics.FromImage(image_dockback);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.dockback, 0, 0, btDock.Width, btDock.Height);
            if (Root.Docked)
                btDock.Image = image_dockback;
            else
                btDock.Image = image_dock;

            image_pencil = new Bitmap(btPen[2].Width, btPen[2].Height);
            g = Graphics.FromImage(image_pencil);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.pencil, 0, 0, btPen[2].Width, btPen[2].Height);
            image_highlighter = new Bitmap(btPen[2].Width, btPen[2].Height);
            g = Graphics.FromImage(image_highlighter);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.highlighter, 0, 0, btPen[2].Width, btPen[2].Height);
            image_pencil_act = new Bitmap(btPen[2].Width, btPen[2].Height);
            g = Graphics.FromImage(image_pencil_act);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.pencil_act, 0, 0, btPen[2].Width, btPen[2].Height);
            image_highlighter_act = new Bitmap(btPen[2].Width, btPen[2].Height);
            g = Graphics.FromImage(image_highlighter_act);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.highlighter_act, 0, 0, btPen[2].Width, btPen[2].Height);

            image_pointer = new Bitmap(btPointer.Width, btPointer.Height);
            g = Graphics.FromImage(image_pointer);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.pointer, 0, 0, btPointer.Width, btPointer.Height);
            image_pointer_act = new Bitmap(btPointer.Width, btPointer.Height);
            g = Graphics.FromImage(image_pointer_act);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(global::gInk.Properties.Resources.pointer_act, 0, 0, btPointer.Width, btPointer.Height);

            image_pen = new Bitmap[Root.MaxPenCount];
            image_pen_act = new Bitmap[Root.MaxPenCount];
            for (int b = 0; b < Root.MaxPenCount; b++)
            {
                image_pen[b] = new Bitmap(btPen[b].Width, btPen[b].Height);
                image_pen_act[b] = new Bitmap(btPen[b].Width, btPen[b].Height);

                PreparePenImages(Root.PenAttr[b].Transparency, ref image_pen[b], ref image_pen_act[b]);
            }

            LastTickTime = DateTime.Parse("1987-01-01");
            tiSlide.Enabled = true;

            ToTransparent();
            ToTopMost();

            this.toolTip.SetToolTip(this.btDock, Root.Local.ButtonNameDock + " (" + Root.Hotkey_DockUndock.ToString() + ")");
            this.toolTip.SetToolTip(this.btPenWidth, Root.Local.ButtonNamePenwidth);
            this.toolTip.SetToolTip(this.btEraser, Root.Local.ButtonNameErasor + " (" + Root.Hotkey_Eraser.ToString() + ")");
            this.toolTip.SetToolTip(this.btPan, Root.Local.ButtonNamePan + " (" + Root.Hotkey_Pan.ToString() + ")");
            this.toolTip.SetToolTip(this.btPointer, Root.Local.ButtonNameMousePointer + " (" + Root.Hotkey_Global.ToString() + ")");
            this.toolTip.SetToolTip(this.btInkVisible, Root.Local.ButtonNameInkVisible + " (" + Root.Hotkey_InkVisible.ToString() + ")");
            this.toolTip.SetToolTip(this.btSnap, Root.Local.ButtonNameSnapshot + " (" + Root.Hotkey_Snap.ToString() + ")");
            this.toolTip.SetToolTip(this.btUndo, Root.Local.ButtonNameUndo + " (" + Root.Hotkey_Undo.ToString() + ")");
            this.toolTip.SetToolTip(this.btClear, Root.Local.ButtonNameClear + " (" + Root.Hotkey_Clear.ToString() + ")");
            this.toolTip.SetToolTip(this.btVideo, Root.Local.ButtonNameVideo + " (" + Root.Hotkey_Video.ToString() + ")");
            this.toolTip.SetToolTip(this.btStop, Root.Local.ButtonNameExit + " (" + Root.Hotkey_Close.ToString() +"/Alt+F4)");
            this.toolTip.SetToolTip(this.btHand, Root.Local.ButtonNameHand + " (" + Root.Hotkey_Hand.ToString() + ")");
            this.toolTip.SetToolTip(this.btLine, Root.Local.ButtonNameLine + " (" + Root.Hotkey_Line.ToString() + ")");
            this.toolTip.SetToolTip(this.btRect, Root.Local.ButtonNameRect + " (" + Root.Hotkey_Rect.ToString() + ")");
            this.toolTip.SetToolTip(this.btOval, Root.Local.ButtonNameOval + " (" + Root.Hotkey_Oval.ToString() + ")");
            this.toolTip.SetToolTip(this.btArrow, Root.Local.ButtonNameArrow + " (" + Root.Hotkey_Arrow.ToString() + ")");
            this.toolTip.SetToolTip(this.btNumb, Root.Local.ButtonNameNumb + " (" + Root.Hotkey_Numb .ToString() + ")");
            this.toolTip.SetToolTip(this.btText, Root.Local.ButtonNameText + " (" + Root.Hotkey_Text.ToString() + ")");
            this.toolTip.SetToolTip(this.btEdit, Root.Local.ButtonNameEdit + " (" + Root.Hotkey_Edit.ToString() + ")");
            this.toolTip.SetToolTip(this.btMagn, Root.Local.ButtonNameMagn + " (" + Root.Hotkey_Magnet.ToString() + ")");

            foreach (Control ct in gpButtons.Controls)
            {
                if (ct.GetType() == typeof(Button))
                {
                    //Console.WriteLine("evt : " + ct.Name);
                    ct.MouseDown += new MouseEventHandler(this.btAllButtons_MouseDown);
                    ct.MouseUp += new MouseEventHandler(this.btAllButtons_MouseUp);
                    ct.ContextMenu = new ContextMenu();
                    ct.ContextMenu.Popup += new EventHandler(this.btAllButtons_RightClick);
                }
            }
            PenModifyDlg = new PenModifyDlg(Root); // It seems to be a little long to build so we prepare it.
            SelectTool(0, 0); // Select Hand Drawing by Default

        }

        // I want to be able to use the space,escape,... I must not leave leave the application handle those and generate clicks...
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return true;
        }

        //public override bool PreProcessMessage(ref Message msg)
        //[System.Security.Permissions.PermissionSet(System.Security.Permissions.SecurityAction.Demand, Name = "FullTrust")]
        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == 0x001C) //WM_ACTIVATEAPP : generated through alt+tab
            {
                if (!Root.AltTabPointer)
                    return;
                if (msg.WParam == IntPtr.Zero)
                {   //Console.WriteLine("desactivating ");
                    if (!Root.PointerMode)
                        SelectPen(-2);
                }
                /*else
                    Console.WriteLine("activating ");*/
            }
            base.WndProc(ref msg);
        }

        private void SetVidBgImage()
        {
            if (Root.VideoRecInProgress == VideoRecInProgress.Stopped)
                btVideo.BackgroundImage = global::gInk.Properties.Resources.VidStop;
            else if (Root.VideoRecInProgress == VideoRecInProgress.Recording)
                btVideo.BackgroundImage = global::gInk.Properties.Resources.VidRecord;
            else if (Root.VideoRecInProgress == VideoRecInProgress.Streaming)
                btVideo.BackgroundImage = global::gInk.Properties.Resources.VidBroadcast;
            else if (Root.VideoRecInProgress == VideoRecInProgress.Paused)
                btVideo.BackgroundImage = global::gInk.Properties.Resources.VidPause;
            else
            {
                btVideo.BackgroundImage = global::gInk.Properties.Resources.VidUnk;
                Console.WriteLine("VideoRecInProgress " + Root.VideoRecInProgress.ToString());
                }
            Root.UponButtonsUpdate |= 0x2;
        }

        private void IC_MouseWheel(object sender, CancelMouseEventArgs e)
        {
            Root.GlobalPenWidth += Root.PixelToHiMetric(e.Delta > 0 ? 2 : -2);
            if (Root.GlobalPenWidth < 1)
                Root.GlobalPenWidth = 1; 
            /*if (Root.GlobalPenWidth > 120)
                Root.GlobalPenWidth = 120;
            */
            Console.WriteLine(Root.GlobalPenWidth);
            IC.DefaultDrawingAttributes.Width = Root.GlobalPenWidth;
            if (Root.CanvasCursor == 1)
                SetPenTipCursor();

        }

        private bool AltKeyPressed()
        {
            return ((short)(GetKeyState(VK_LMENU) | GetKeyState(VK_RMENU)) & 0x8000) == 0x8000;
        }

        private void btAllButtons_MouseDown(object sender, MouseEventArgs e)
        {
            MouseTimeDown = DateTime.Now;
            MouseDownButtonObject = sender;            
            longClickTimer.Start();
            longClickTimer.Tag = sender;
            Console.WriteLine(string.Format("MD {0} {1}", DateTime.Now.Second, DateTime.Now.Millisecond));
        }

        private void btAllButtons_MouseUp(object sender, MouseEventArgs e)
        {
            Console.WriteLine("MU " + (sender as Control).Name);
            MouseDownButtonObject = null;
            (sender as Button).RightToLeft = RightToLeft.No;
            longClickTimer.Stop();
            IsMovingToolbar = 0;
        }

        private void btAllButtons_RightClick(object sender, EventArgs e)
        {
            MouseTimeDown = DateTime.FromBinary(0);
            MouseDownButtonObject = null;
            longClickTimer.Stop();
            sender = (sender as ContextMenu).SourceControl;
            (sender as Button).RightToLeft = RightToLeft.No;
            Console.WriteLine(string.Format("RC {0}", (sender as Control).Name));
            (sender as Button).PerformClick();
        }

        private void longClickTimer_Tick(object sender, EventArgs e)
        {
            Button bt = MouseDownButtonObject as Button;
            MouseDownButtonObject = null;
            longClickTimer.Stop();
            Console.WriteLine(string.Format("!LC {0}", bt.Name));
            bt.RightToLeft = RightToLeft.Yes;
            bt.PerformClick();
            IsMovingToolbar = 0;
        }

        private void setStrokeProperties(ref Stroke st, int FilledSelected)
        {
            if (FilledSelected == 0)
                st.ExtendedProperties.Add(Root.ISSTROKE_GUID, true);
            else if (FilledSelected == 1)
                st.ExtendedProperties.Add(Root.ISFILLEDCOLOR_GUID, true);
            else if (FilledSelected == 2)
                st.ExtendedProperties.Add(Root.ISFILLEDWHITE_GUID, true);
            else if (FilledSelected == 3)
                st.ExtendedProperties.Add(Root.ISFILLEDBLACK_GUID, true);
        }

            private Stroke AddEllipseStroke(int CursorX0, int CursorY0, int CursorX, int CursorY,int FilledSelected)
        {
            int NB_PTS = 36 * 3;
            Point[] pts = new Point[NB_PTS + 1];
            int dX = CursorX - CursorX0;
            int dY = CursorY - CursorY0;

            for (int i = 0; i < NB_PTS + 1; i++)
            {
                pts[i] = new Point(CursorX0 + (int)(dX * Math.Cos(Math.PI * (i + NB_PTS / 8) / (NB_PTS / 2))), CursorY0 + (int)(dY * Math.Sin(Math.PI * (i + NB_PTS / 8) / (NB_PTS / 2))));
            }
            IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref pts);
            Stroke st = Root.FormCollection.IC.Ink.CreateStroke(pts);
            st.DrawingAttributes = Root.FormCollection.IC.DefaultDrawingAttributes.Clone();
            st.DrawingAttributes.AntiAliased = true;
            st.DrawingAttributes.FitToCurve = true;
            setStrokeProperties(ref st, FilledSelected);
            Root.FormCollection.IC.Ink.Strokes.Add(st);
            return st;
        }

        private Stroke AddRectStroke(int CursorX0, int CursorY0, int CursorX, int CursorY,int FilledSelected)
        {
            Point[] pts = new Point[9];
            int i = 0;
            pts[i++] = new Point(CursorX0, CursorY0);
            pts[i++] = new Point(CursorX0, (CursorY0+CursorY)/2);
            pts[i++] = new Point(CursorX0, CursorY);
            pts[i++] = new Point((CursorX0+CursorX)/2, CursorY);
            pts[i++] = new Point(CursorX, CursorY);
            pts[i++] = new Point(CursorX, (CursorY0 + CursorY) / 2);
            pts[i++] = new Point(CursorX, CursorY0);
            pts[i++] = new Point((CursorX0 + CursorX) / 2, CursorY0);
            pts[i++] = new Point(CursorX0, CursorY0);

            IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref pts);
            Stroke st = Root.FormCollection.IC.Ink.CreateStroke(pts);
            st.DrawingAttributes = Root.FormCollection.IC.DefaultDrawingAttributes.Clone();
            st.DrawingAttributes.AntiAliased = true;
            st.DrawingAttributes.FitToCurve = false;
            setStrokeProperties(ref st, FilledSelected);
            Root.FormCollection.IC.Ink.Strokes.Add(st);
            return st;
        }

        private Stroke AddLineStroke(int CursorX0, int CursorY0, int CursorX, int CursorY)
        {
            Point[] pts = new Point[2];
            pts[0] = new Point(CursorX0, CursorY0);
            pts[1] = new Point(CursorX, CursorY);

            IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref pts);
            Stroke st = Root.FormCollection.IC.Ink.CreateStroke(pts);
            st.DrawingAttributes = Root.FormCollection.IC.DefaultDrawingAttributes.Clone();
            st.DrawingAttributes.AntiAliased = true;
            st.DrawingAttributes.FitToCurve = false;
            setStrokeProperties(ref st, 0);
            Root.FormCollection.IC.Ink.Strokes.Add(st);
            return st;
        }

        private Stroke AddArrowStroke(int CursorX0, int CursorY0, int CursorX, int CursorY)
        // arrow at starting point
        {
            Point[] pts = new Point[5];
            double theta = Math.Atan2(CursorY - CursorY0, CursorX - CursorX0);

            pts[0] = new Point((int)(CursorX0+Math.Cos(theta + Root.ArrowAngle) * Root.ArrowLen), (int)(CursorY0 + Math.Sin(theta + Root.ArrowAngle) * Root.ArrowLen));
            pts[1] = new Point(CursorX0, CursorY0);
            pts[2] = new Point((int)(CursorX0 + Math.Cos(theta - Root.ArrowAngle) * Root.ArrowLen), (int)(CursorY0 + Math.Sin(theta - Root.ArrowAngle) * Root.ArrowLen));
            pts[3] = new Point(CursorX0, CursorY0);
            pts[4] = new Point(CursorX, CursorY);

            IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref pts);
            Stroke st = Root.FormCollection.IC.Ink.CreateStroke(pts);
            st.DrawingAttributes = Root.FormCollection.IC.DefaultDrawingAttributes.Clone();
            st.DrawingAttributes.AntiAliased = true;
            st.DrawingAttributes.FitToCurve = false;
            setStrokeProperties(ref st, 0);
            Root.FormCollection.IC.Ink.Strokes.Add(st);
            return st;
        }

        private Stroke AddNumberTagStroke(int CursorX0, int CursorY0, int CursorX, int CursorY,string txt)
        // arrow at starting point
        {
            // for the filling, filled color is not used but this state is used to note that we edit the tag number
            Stroke st = AddEllipseStroke(CursorX0, CursorY0, (int)(CursorX0 + TextSize * 1.2), (int)(CursorY0 + TextSize * 1.2), Root.FilledSelected==1?0:Root.FilledSelected);
            st.ExtendedProperties.Add(Root.ISSTROKE_GUID, true);
            Point pt = new Point(CursorX0, CursorY0);
            IC.Renderer.PixelToInkSpace(IC.Handle, ref pt);
            st.ExtendedProperties.Add(Root.ISTAG_GUID, true);
            st.ExtendedProperties.Add(Root.TEXT_GUID, txt);
            st.ExtendedProperties.Add(Root.TEXTX_GUID, pt.X);
            st.ExtendedProperties.Add(Root.TEXTY_GUID, pt.Y);
            //st.ExtendedProperties.Add(Root.TEXTFORMAT_GUID, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            st.ExtendedProperties.Add(Root.TEXTHALIGN_GUID, StringAlignment.Center);
            st.ExtendedProperties.Add(Root.TEXTVALIGN_GUID, StringAlignment.Center);
            st.ExtendedProperties.Add(Root.TEXTFONT_GUID, TextFont);
            st.ExtendedProperties.Add(Root.TEXTFONTSIZE_GUID, (float)TextSize);
            st.ExtendedProperties.Add(Root.TEXTFONTSTYLE_GUID, (TextItalic ? FontStyle.Italic : FontStyle.Regular) | (TextBold ? FontStyle.Bold : FontStyle.Regular));
            return st;
        }

        private Stroke AddTextStroke(int CursorX0, int CursorY0, int CursorX, int CursorY, string txt, StringAlignment Align)
        // arrow at starting point
        {
            Point pt = new Point(CursorX0, CursorY0);
            //IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref pt);
            IC.Renderer.PixelToInkSpace(IC.Handle, ref pt);
            Point[] pts = new Point[1];
            pts[0]=pt;
            Stroke st = Root.FormCollection.IC.Ink.CreateStroke(pts);
            st.DrawingAttributes = Root.FormCollection.IC.DefaultDrawingAttributes.Clone();
            //st.DrawingAttributes.Width = 0; // no width to hide the point;
            st.ExtendedProperties.Add(Root.TEXT_GUID, txt);
            st.ExtendedProperties.Add(Root.TEXTX_GUID, pt.X);
            st.ExtendedProperties.Add(Root.TEXTY_GUID, pt.Y);
            st.ExtendedProperties.Add(Root.TEXTHALIGN_GUID, Align);
            st.ExtendedProperties.Add(Root.TEXTVALIGN_GUID, StringAlignment.Near);
            st.ExtendedProperties.Add(Root.TEXTFONT_GUID, TextFont);
            st.ExtendedProperties.Add(Root.TEXTFONTSIZE_GUID, (float)TextSize);
            st.ExtendedProperties.Add(Root.TEXTFONTSTYLE_GUID, (TextItalic ? FontStyle.Italic : FontStyle.Regular) | (TextBold ? FontStyle.Bold : FontStyle.Regular));
            setStrokeProperties(ref st, 0);
            Root.FormCollection.IC.Ink.Strokes.Add(st);
            return st;
        }

        bool TextEdited = false;
        private DialogResult ModifyTextInStroke(Stroke stk, string txt)
        {
            // required to access the dialog box
            tiSlide.Stop();
            IC.Enabled = false;
            //ToThrough();

            FormInput inp = new FormInput(Root.Local.DlgTextCaption, Root.Local.DlgTextLabel, txt,true,Root, stk);

            Point pt = stk.GetPoint(0);
            IC.Renderer.InkSpaceToPixel(IC.Handle, ref pt);
            pt = PointToScreen(pt);
            inp.Top = pt.Y - inp.Height - 10;// +this.Top ;
            inp.Left = pt.X;// +this.Left;
            //Console.WriteLine("Edit {0},{1}", inp.Left, inp.Top);
            Screen scr = Screen.FromPoint(pt);
            if((inp.Right>= scr.Bounds.Right)|| (inp.Top <= scr.Bounds.Top))
            {   // if the dialog can not be displayed above the text we will display it in the middle of the primary screen
                inp.Top = ((int)(scr.Bounds.Top+ scr.Bounds.Bottom-inp.Height)/2);//System.Windows.SystemParameters.PrimaryScreenHeight)-inp.Height) / 2;
                inp.Left = ((int)(scr.Bounds.Left+scr.Bounds.Right-inp.Width)/2);// System.Windows.SystemParameters.PrimaryScreenWidth) - inp.Width) / 2;
            }
            DialogResult ret = inp.ShowDialog();
            if (ret == DialogResult.Cancel)
                stk.ExtendedProperties.Add(Root.TEXT_GUID, txt);
            TextEdited = true;
            tiSlide.Start();
            IC.Enabled = true;
            //ToUnThrough();

            return ret;
        }

        private float NearestStroke(Point pt,bool ptInPixel, out Stroke minStroke,out float pos,bool Search4Text=true,bool butLast=false)
        {
            if(ptInPixel)
                IC.Renderer.PixelToInkSpace(IC.Handle, ref pt);

            float dst = 10000000000;
            float dst1 = dst;
            float pos1;
            pos = 0;
            //if (IC.Ink.Strokes.Count == 0)
            //    return dst;
            //minStroke = IC.Ink.Strokes[0];
            minStroke = null;
            //foreach (Stroke st in IC.Ink.Strokes)
            for(int i=0;i<=IC.Ink.Strokes.Count-(butLast?2:1);i++)
            {
                Stroke st= IC.Ink.Strokes[i];
                if (st.ExtendedProperties.Contains(Root.ISDELETION_GUID))
                    continue;
                pos1 = st.NearestPoint(pt, out dst1);
                if ((dst1 < dst) && (!Search4Text ||(st.ExtendedProperties.Contains(Root.TEXT_GUID))))
                {
                    dst = dst1;
                    minStroke = st;
                    pos = pos1;
                }
            };
            return dst;
        }

        private void MagneticEffect(int cursorX0, int cursorY0, ref int cursorX, ref int cursorY,bool Magnetic = false)
        {
            int dist(int x, int y)
            {
                if (x == int.MaxValue || y == int.MinValue)
                    return int.MaxValue;
                else
                    return x * x + y * y;
            };
            /*
                First : looking for a point on a stroke next to the pointer
            */
            Stroke st;
            float pos;
            Point pt = new Point(int.MaxValue, int.MaxValue);
            int x2 = int.MaxValue, y2 = int.MaxValue, x_a = int.MaxValue, y_a = int.MaxValue;
            if ((Control.ModifierKeys & Keys.Control) != Keys.None || (Control.ModifierKeys & Keys.Shift) != Keys.None)  // force temporarily Magnetic off if ctrl or shift is depressed
                Magnetic = false;   
            if ((Magnetic || (Control.ModifierKeys & Keys.Control)!=Keys.None  ) &&
                (NearestStroke(new Point(cursorX, cursorY), true, out st, out pos, false, true) < Root.PixelToHiMetric(Root.MinMagneticRadius())))
            {
                pt = st.GetPoint((int)Math.Round(pos));
                IC.Renderer.InkSpaceToPixel(IC.Handle, ref pt);
                //cursorX = pt.X;
                //cursorY = pt.Y;
                //return;
            }

            /*
                Second : looking for remarquable points around text
            */
            if ((Magnetic || (ModifierKeys & Keys.Control) != Keys.None))
                foreach (Stroke stk in IC.Ink.Strokes)
                {
                    if (stk.ExtendedProperties.Contains(Root.TEXTWIDTH_GUID))
                    {
                        int x0 = Root.HiMetricToPixel((int)stk.ExtendedProperties[Root.TEXTX_GUID].Data);
                        int y0 = Root.HiMetricToPixel((int)stk.ExtendedProperties[Root.TEXTY_GUID].Data);
                        int x1, y1;
                        if ((System.Drawing.StringAlignment)stk.ExtendedProperties[Root.TEXTHALIGN_GUID].Data == StringAlignment.Near)
                        {
                            x1 = (int)(x0 + (float)stk.ExtendedProperties[Root.TEXTWIDTH_GUID].Data);
                        }
                        else
                        {
                            x1 = x0;
                            x0 = (int)(x1 - (float)stk.ExtendedProperties[Root.TEXTWIDTH_GUID].Data);
                        }
                        if ((System.Drawing.StringAlignment)stk.ExtendedProperties[Root.TEXTVALIGN_GUID].Data == StringAlignment.Near)
                        {
                            y1 = (int)(y0 + (float)stk.ExtendedProperties[Root.TEXTHEIGHT_GUID].Data);
                        }
                        else
                        {
                            y1 = y0;
                            y0 = (int)(y1 - (float)stk.ExtendedProperties[Root.TEXTHEIGHT_GUID].Data);
                        }
                        //Console.WriteLine("{0},{1}   {2},{3}    {4},{5}       <= {6},{7}", x0, y0, cursorX, cursorY, x1, y1, (float)stk.ExtendedProperties[Root.TEXTWIDTH_GUID].Data, (float)stk.ExtendedProperties[Root.TEXTHEIGHT_GUID].Data);
                        if (   (x0 - Root.MinMagneticRadius()) <= cursorX && cursorX <= (x1 + Root.MinMagneticRadius()) 
                            && (y0 - Root.MinMagneticRadius()) <= cursorY && cursorY <= (y1 + Root.MinMagneticRadius()) )
                        {
                            int d = dist(cursorX - x0, cursorY - y0);
                            x2 = x0;
                            y2 = y0;
                            int d1 = dist(cursorX - (x1 + x0) / 2, cursorY - y0);
                            if (d1 < d)
                            {
                                x2 = (x1 + x0) / 2;
                                y2 = y0;
                                d = d1;
                            };
                            d1 = dist(cursorX - x1, cursorY - y0);
                            if (d1 < d)
                            {
                                x2 = x1;
                                y2 = y0;
                                d = d1;
                            };
                            d1 = dist(cursorX - x1, cursorY - (y0 + y1) / 2);
                            if (d1 < d)
                            {
                                x2 = x1;
                                y2 = (y0 + y1) / 2;
                                d = d1;
                            };
                            d1 = dist(cursorX - x1, cursorY - y1);
                            if (d1 < d)
                            {
                                x2 = x1;
                                y2 = y1;
                                d = d1;
                            };
                            d1 = dist(cursorX - (x0 + x1) / 2, cursorY - y1);
                            if (d1 < d)
                            {
                                x2 = (x0 + x1) / 2;
                                y2 = y1;
                                d = d1;
                            };
                            d1 = dist(cursorX - x0, cursorY - y1);
                            if (d1 < d)
                            {
                                x2 = x0;
                                y2 = y1;
                                d = d1;
                            };
                            d1 = dist(cursorX - x0, cursorY - (y0 + y1) / 2);
                            if (d1 < d)
                            {
                                x2 = x0;
                                y2 = (y0 + y1) / 2;
                                d = d1;
                            };
                            // the assumption is that text are not overlaying, therefore we don't need to carry on searching...
                            break;
                            //cursorX = x2;
                            //cursorY = y2;
                            //return;
                        };
                    };
                };
            //Console.WriteLine("***** {0},{1} {2},{3}=>{4} {5},{6}=>{7}", cursorX,cursorY, pt.X, pt.Y, dist(pt.X - cursorX, pt.Y - cursorY),x2, y2, dist(x2 - cursorX, y2 - cursorY));
            if (dist(pt.X - cursorX, pt.Y - cursorY) < dist(x2 - cursorX, y2 - cursorY))
            {
                x2 = pt.X;
                y2 = pt.Y;
            }
            if (x2 !=int.MaxValue && y2!=int.MaxValue)
            {
                cursorX = x2;
                cursorY = y2;
                return;
            }
            /*
                Next : on axis @+/-2 every 15�
            */
            double theta = Math.Atan2(cursorY - cursorY0, cursorX - cursorX0) * 180.0 / Math.PI;
            double theta2 = ((theta + 2.0 + 360.0) % 15.0) - 2.0;
            if ((Magnetic || (ModifierKeys & Keys.Shift) != Keys.None)&&
                (Math.Abs(theta2) < 3.0))
            {
                theta -= theta2;
                if ((Math.Abs(theta) < 45.0) || (Math.Abs(theta - 180.0) < 45.0) || (Math.Abs(theta + 180.0) < 45.0))
                    cursorY = (int)((cursorX - cursorX0) * Math.Tan(theta / 180.0 * Math.PI) + cursorY0);
                else
                    cursorX = (int)((cursorY - cursorY0) / Math.Tan(theta / 180.0 * Math.PI) + cursorX0);
                return;
            }
        }

        private void IC_Stroke(object sender, InkCollectorStrokeEventArgs e)
		{
            movedStroke = null; // reset the moving object
            try { if(e.Stroke.ExtendedProperties.Contains(Root.ISSTROKE_GUID)) e.Stroke.ExtendedProperties.Remove(Root.ISSTROKE_GUID); } catch { } // the ISSTROKE set for drawin
            if (Root.ToolSelected==0)
            {
                Stroke st = e.Stroke;// IC.Ink.Strokes[IC.Ink.Strokes.Count-1];
                setStrokeProperties(ref st, Root.FilledSelected);
            }
            else
            {
                if (Root.CursorX0 == Int32.MinValue) // process when clicking touchscreen with just a short press;
                {
                    Point p = System.Windows.Forms.Cursor.Position;
                    p=Root.FormDisplay.PointToClient(p);
                    Root.CursorX = p.X;
                    Root.CursorY = p.Y;
                }
                IC.Ink.DeleteStroke(e.Stroke); // the stroke that was just inserted has to be replaced.
                if ((Root.ToolSelected == 1) && (Root.CursorX0 != Int32.MinValue))
                    AddLineStroke(Root.CursorX0, Root.CursorY0, Root.CursorX, Root.CursorY);
                else if ((Root.ToolSelected == 2) && (Root.CursorX0 != Int32.MinValue))
                    AddRectStroke(Root.CursorX0, Root.CursorY0, Root.CursorX, Root.CursorY, Root.FilledSelected);
                else if ((Root.ToolSelected == 3) && (Root.CursorX0 != Int32.MinValue))
                    AddEllipseStroke(Root.CursorX0, Root.CursorY0, Root.CursorX, Root.CursorY, Root.FilledSelected);
                else if ((Root.ToolSelected == 4) && (Root.CursorX0 != Int32.MinValue))
                    AddArrowStroke(Root.CursorX0, Root.CursorY0, Root.CursorX, Root.CursorY);
                else if ((Root.ToolSelected == 5) && (Root.CursorX0 != Int32.MinValue))
                    AddArrowStroke(Root.CursorX, Root.CursorY, Root.CursorX0, Root.CursorY0);
                else if (Root.ToolSelected == 6)
                {
                    Stroke st = AddNumberTagStroke(Root.CursorX, Root.CursorY, Root.CursorX, Root.CursorY, Root.TagNumbering.ToString());
                    Root.TagNumbering++;
                }
                else if (Root.ToolSelected == 7) // Edit
                {
                    float pos;
                    Stroke minStroke;
                    if (NearestStroke(new Point(Root.CursorX, Root.CursorY), true, out minStroke, out pos, true, false) < Root.PixelToHiMetric(Root.MinMagneticRadius()))
                    {
                        ModifyTextInStroke(minStroke, (string)(minStroke.ExtendedProperties[Root.TEXT_GUID].Data));
                        SelectTool(0, 0);
                        ComputeTextBoxSize(ref minStroke);
                    }
                }
                else if ((Root.ToolSelected == 8) || (Root.ToolSelected == 9))  // new text
                {
                    Stroke st = AddTextStroke(Root.CursorX, Root.CursorY, Root.CursorX, Root.CursorY, "Text", (Root.ToolSelected == 8)?StringAlignment.Near:StringAlignment.Far);
                    Root.FormDisplay.DrawStrokes();
                    Root.FormDisplay.UpdateFormDisplay(true);
                    if (ModifyTextInStroke(st, (string)(st.ExtendedProperties[Root.TEXT_GUID].Data)) == DialogResult.Cancel)
                        IC.Ink.DeleteStroke(st);
                    else
                    {
                        ComputeTextBoxSize(ref st);
                    }
                }
                else if (Root.ToolSelected == 10)// Move : do Nothing
                    movedStroke = null;
            }
            SaveUndoStrokes();
            Root.FormDisplay.ClearCanvus();
            Root.FormDisplay.DrawStrokes();
            Root.FormDisplay.DrawButtons(true);
            Root.FormDisplay.UpdateFormDisplay(true);

            // reset the CursorX0/Y0 : this seems to introduce a wrong interim drawing
            Root.CursorX0 = Int32.MinValue;
            Root.CursorY0 = Int32.MinValue;
        }

        private void ComputeTextBoxSize(ref Stroke st)
        {
            System.Drawing.StringFormat stf = new System.Drawing.StringFormat(System.Drawing.StringFormatFlags.NoClip);
            stf.Alignment = (System.Drawing.StringAlignment)(st.ExtendedProperties[Root.TEXTHALIGN_GUID].Data);
            stf.LineAlignment = (System.Drawing.StringAlignment)(st.ExtendedProperties[Root.TEXTVALIGN_GUID].Data);
            SizeF layoutSize = new SizeF(2000.0F, 2000.0F);
            layoutSize = Root.FormDisplay.gOneStrokeCanvus.MeasureString((string)(st.ExtendedProperties[Root.TEXT_GUID].Data),
                            new Font((string)st.ExtendedProperties[Root.TEXTFONT_GUID].Data, (float)st.ExtendedProperties[Root.TEXTFONTSIZE_GUID].Data,
                            (System.Drawing.FontStyle)(int)st.ExtendedProperties[Root.TEXTFONTSTYLE_GUID].Data), layoutSize, stf);
            st.ExtendedProperties.Add(Root.TEXTWIDTH_GUID, layoutSize.Width);
            st.ExtendedProperties.Add(Root.TEXTHEIGHT_GUID, layoutSize.Height);
        }

        private void SaveUndoStrokes()
		{
			Root.RedoDepth = 0;
			if (Root.UndoDepth < Root.UndoStrokes.GetLength(0) - 1)
				Root.UndoDepth++;

			Root.UndoP++;
			if (Root.UndoP >= Root.UndoStrokes.GetLength(0))
				Root.UndoP = 0;

			if (Root.UndoStrokes[Root.UndoP] == null)
				Root.UndoStrokes[Root.UndoP] = new Ink();
			Root.UndoStrokes[Root.UndoP].DeleteStrokes();
			if (IC.Ink.Strokes.Count > 0)
				Root.UndoStrokes[Root.UndoP].AddStrokesAtRectangle(IC.Ink.Strokes, IC.Ink.Strokes.GetBoundingBox());
		}
        Stroke movedStroke = null;

		private void IC_CursorDown(object sender, InkCollectorCursorDownEventArgs e)
		{
            if (Root.ToolSelected == 0)
                e.Stroke.ExtendedProperties.Add(Root.ISSTROKE_GUID, true); // we set the ISTROKE_GUID in order to draw the inprogress as a line
            else
                e.Stroke.ExtendedProperties.Add(Root.ISHIDDEN_GUID, true); // we set the ISTROKE_GUID in order to draw the inprogress as a line

            if (!Root.InkVisible && Root.Snapping <= 0)
			{
				Root.SetInkVisible(true);
			}

			Root.FormDisplay.ClearCanvus(Root.FormDisplay.gOneStrokeCanvus);
            Root.FormDisplay.DrawStrokes(Root.FormDisplay.gOneStrokeCanvus);
			Root.FormDisplay.DrawButtons(Root.FormDisplay.gOneStrokeCanvus, false);
            Point p;
            try
            {
                if (e.Stroke.BezierPoints.Length > 0)
                {
                    p = e.Stroke.BezierPoints[0];
                    IC.Renderer.InkSpaceToPixel(Root.FormDisplay.gOneStrokeCanvus, ref p);
                }
                else
                    throw new System.ApplicationException("Empty Stroke");
            }
            catch
            {
                p = System.Windows.Forms.Cursor.Position;
                p = Root.FormDisplay.PointToClient(p);
            }
            Root.CursorX = p.X;
            Root.CursorY = p.Y;
            if (Root.EraserMode) // we are deleting the nearest object for clicking...
            {
                e.Stroke.ExtendedProperties.Add(Root.ISDELETION_GUID,true);
                float pos;
                Stroke minStroke;
                if (NearestStroke(new Point(Root.CursorX, Root.CursorY), true, out minStroke, out pos,false,false) < Root.PixelToHiMetric(Root.MinMagneticRadius()))
                {
                    IC.Ink.DeleteStroke(minStroke);
                }
            }
        }

        private void IC_MouseDown(object sender, CancelMouseEventArgs e)
		{
			if (Root.gpPenWidthVisible)
			{
				Root.gpPenWidthVisible = false;
				Root.UponSubPanelUpdate = true;
			}

			Root.FingerInAction = true;
			if (Root.Snapping == 1)
			{
				Root.SnappingX = e.X;
				Root.SnappingY = e.Y;
				Root.SnappingRect = new Rectangle(e.X, e.Y, 0, 0);
				Root.Snapping = 2;
			}

			if (!Root.InkVisible && Root.Snapping <= 0)
			{
				Root.SetInkVisible(true);
			}

			LasteXY.X = e.X;
			LasteXY.Y = e.Y;
			IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref LasteXY);
            Root.CursorX0 = e.X;
            Root.CursorY0 = e.Y;
            MagneticEffect(Root.CursorX0 - 1, Root.CursorY0, ref Root.CursorX0, ref Root.CursorY0, Root.MagneticRadius>0); // analysis of magnetic will be done within the module
            if (Root.InkVisible)
            {
                Root.CursorX = Root.CursorX0;
                Root.CursorY = Root.CursorY0;
            }

            if (Root.ToolSelected == 10) // Move
            {
                float pos;
                if (NearestStroke(new Point(Root.CursorX, Root.CursorY), true, out movedStroke, out pos, false, true) > Root.PixelToHiMetric(Root.MinMagneticRadius()))
                    movedStroke = null;
            }
        }


        public Point LasteXY;
		private void IC_MouseMove(object sender, CancelMouseEventArgs e)
		{
            if (e.Button== MouseButtons.None) return;
            //Console.WriteLine("Cursor {0},{1} - {2}", e.X, e.Y, e.Button);
            Root.CursorX = e.X;
            Root.CursorY = e.Y;
            MagneticEffect(Root.CursorX0, Root.CursorY0, ref Root.CursorX, ref Root.CursorY, Root.ToolSelected >0 && Root.MagneticRadius>0);

            if (LasteXY.X == 0 && LasteXY.Y == 0)
			{
				LasteXY.X = e.X;
				LasteXY.Y = e.Y;
				IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref LasteXY);
			}
			Point currentxy = new Point(e.X, e.Y);
			IC.Renderer.PixelToInkSpace(Root.FormDisplay.gOneStrokeCanvus, ref currentxy);

			if (Root.Snapping == 2)
			{
				int left = Math.Min(Root.SnappingX, e.X);
				int top = Math.Min(Root.SnappingY, e.Y);
				int width = Math.Abs(Root.SnappingX - e.X);
				int height = Math.Abs(Root.SnappingY - e.Y);
				Root.SnappingRect = new Rectangle(left, top, width, height);

				if (LasteXY != currentxy)
					Root.MouseMovedUnderSnapshotDragging = true;
			}
			else if (Root.PanMode && Root.FingerInAction)
			{
				Root.Pan(currentxy.X - LasteXY.X, currentxy.Y - LasteXY.Y);			
			}
            else if (Root.ToolSelected==10)
            {
                if (movedStroke != null)
                {
                    movedStroke.Move(currentxy.X - LasteXY.X, currentxy.Y - LasteXY.Y);
                    if (movedStroke.ExtendedProperties.Contains(Root.TEXT_GUID))
                    {
                        movedStroke.ExtendedProperties.Add(Root.TEXTX_GUID, ((int)movedStroke.ExtendedProperties[Root.TEXTX_GUID].Data) + (currentxy.X - LasteXY.X));
                        movedStroke.ExtendedProperties.Add(Root.TEXTY_GUID, ((int)movedStroke.ExtendedProperties[Root.TEXTY_GUID].Data) + (currentxy.Y - LasteXY.Y));
                    }
                    Root.FormDisplay.ClearCanvus();
                    Root.FormDisplay.DrawStrokes();
                    Root.FormDisplay.UpdateFormDisplay(true);
                }
            }

            LasteXY = currentxy;
		}

        private void IC_MouseUp(object sender, CancelMouseEventArgs e)
        {
            Root.FingerInAction = false;
			if (Root.Snapping == 2)
			{
				int left = Math.Min(Root.SnappingX, e.X);
				int top = Math.Min(Root.SnappingY, e.Y);
				int width = Math.Abs(Root.SnappingX - e.X);
				int height = Math.Abs(Root.SnappingY - e.Y);
				if (width < 5 || height < 5)
				{
					left = 0;
					top = 0;
					width = this.Width;
					height = this.Height;
				}
				Root.SnappingRect = new Rectangle(left + this.Left, top + this.Top, width, height);
				Root.UponTakingSnap = true;
				ExitSnapping();
			}
			else if (Root.PanMode)
			{
				SaveUndoStrokes();
			}
			else
			{
				Root.UponAllDrawingUpdate = true;
			}
            Root.CursorX0 = int.MinValue;
            Root.CursorY0 = int.MinValue;
        }

        private void IC_CursorInRange(object sender, InkCollectorCursorInRangeEventArgs e)
		{
			if (e.Cursor.Inverted && Root.CurrentPen != -1)
			{
				EnterEraserMode(true);
				/*
				// temperary eraser icon light
				if (btEraser.Image == image_eraser)
				{
					btEraser.Image = image_eraser_act;
					Root.FormDisplay.DrawButtons(true);
					Root.FormDisplay.UpdateFormDisplay();
				}
				*/
			}
			else if (!e.Cursor.Inverted && Root.CurrentPen != -1)
			{
				EnterEraserMode(false);
				/*
				if (btEraser.Image == image_eraser_act)
				{
					btEraser.Image = image_eraser;
					Root.FormDisplay.DrawButtons(true);
					Root.FormDisplay.UpdateFormDisplay();
				}
				*/
			}
		}

		public void ToTransparent()
		{
			UInt32 dwExStyle = GetWindowLong(this.Handle, -20);
			SetWindowLong(this.Handle, -20, dwExStyle | 0x00080000);
			SetLayeredWindowAttributes(this.Handle, 0x00FFFFFF, 1, 0x2);
		}

		public void ToTopMost()
		{
			SetWindowPos(this.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0020);
		}

		public void ToThrough()
		{
			UInt32 dwExStyle = GetWindowLong(this.Handle, -20);
			//SetWindowLong(this.Handle, -20, dwExStyle | 0x00080000);
			//SetWindowPos(this.Handle, (IntPtr)0, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0004 | 0x0010 | 0x0020);
			//SetLayeredWindowAttributes(this.Handle, 0x00FFFFFF, 1, 0x2);
			SetWindowLong(this.Handle, -20, dwExStyle | 0x00080000 | 0x00000020);
			//SetWindowPos(this.Handle, (IntPtr)(1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010 | 0x0020);
		}

		public void ToUnThrough()
		{
			UInt32 dwExStyle = GetWindowLong(this.Handle, -20);
			//SetWindowLong(this.Handle, -20, (uint)(dwExStyle & ~0x00080000 & ~0x0020));
			SetWindowLong(this.Handle, -20, (uint)(dwExStyle & ~0x0020));
			//SetWindowPos(this.Handle, (IntPtr)(-2), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010 | 0x0020);

			//dwExStyle = GetWindowLong(this.Handle, -20);
			//SetWindowLong(this.Handle, -20, dwExStyle | 0x00080000);
			//SetLayeredWindowAttributes(this.Handle, 0x00FFFFFF, 1, 0x2);
			//SetWindowPos(this.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0020);
		}

		public void EnterEraserMode(bool enter)
		{
			int exceptiontick = 0;
			bool exc;
			do
			{
				exceptiontick++;
				exc = false;
				try
				{
                    if (enter)
					{
						IC.EditingMode = InkOverlayEditingMode.Delete;
						Root.EraserMode = true;
					}
					else
					{
						IC.EditingMode = InkOverlayEditingMode.Ink;
						Root.EraserMode = false;
					}
				}
				catch
				{
					Thread.Sleep(50);
					exc = true;
				}
			}
			while (exc && exceptiontick < 3);
		}

        public void SelectTool(int tool, int filled = -1)
        // Hand (0),Line(1),Rect(2),Oval(3),StartArrow(4),EndArrow(5),NumberTag(6),Edit(7),txtLeftAligned(8),txtRightAligned(9),Move(10)
        // filled : empty(0),PenColorFilled(1),WhiteFilled(2),BlackFilled(3)
        // filled is applicable to Hand,Rect,Oval
        {
            btHand.BackgroundImage = global::gInk.Properties.Resources.tool_hand;
            btLine.BackgroundImage = global::gInk.Properties.Resources.tool_line;
            btRect.BackgroundImage = global::gInk.Properties.Resources.tool_rect;
            btOval.BackgroundImage = global::gInk.Properties.Resources.tool_oval;
            if (Root.DefaultArrow_start)
                btArrow.BackgroundImage = global::gInk.Properties.Resources.tool_stAr;
            else
                btArrow.BackgroundImage = global::gInk.Properties.Resources.tool_enAr;
            btNumb.BackgroundImage = global::gInk.Properties.Resources.tool_numb;
            btText.BackgroundImage = global::gInk.Properties.Resources.tool_txtL;
            btEdit.BackgroundImage = global::gInk.Properties.Resources.tool_edit;

            if (AltKeyPressed())
            {
                if (SavedTool < 0 || tool != Root.ToolSelected)
                {
                    SavedTool = Root.ToolSelected;
                    SavedFilled = Root.FilledSelected;
                    if (tool == 10 && SavedPen <0)
                        SavedPen = LastPenSelected;
                }
            }

            int[] applicableTool = { 0, 2, 3, 6 };
            if (filled >= 0)
                Root.FilledSelected = filled;
            else if ((Array.IndexOf(applicableTool, tool) >= 0) && (tool == Root.ToolSelected))
                Root.FilledSelected = (Root.FilledSelected + 1) % 4;
            else
                Root.FilledSelected = 0;

            Root.UponButtonsUpdate |= 0x2;

            if (tool == -1)
            {
                Root.ToolSelected = 0; // to prevent drawing
                return;
            }
            else if (tool == 0)
            {
                if (Root.FilledSelected == 0)
                    btHand.BackgroundImage = global::gInk.Properties.Resources.tool_hand_act;
                else if (Root.FilledSelected == 1)
                    btHand.BackgroundImage = global::gInk.Properties.Resources.tool_hand_filledC;
                else if (Root.FilledSelected == 2)
                    btHand.BackgroundImage = global::gInk.Properties.Resources.tool_hand_filledW;
                else if (Root.FilledSelected == 3)
                    btHand.BackgroundImage = global::gInk.Properties.Resources.tool_hand_filledB;
                EnterEraserMode(false);
            }
            else if (tool == 1)
                btLine.BackgroundImage = global::gInk.Properties.Resources.tool_line_act;
            else if (tool == 2)
            {
                if (Root.FilledSelected == 0)
                    btRect.BackgroundImage = global::gInk.Properties.Resources.tool_rect_act;
                else if (Root.FilledSelected == 1)
                    btRect.BackgroundImage = global::gInk.Properties.Resources.tool_rect_filledC;
                else if (Root.FilledSelected == 2)
                    btRect.BackgroundImage = global::gInk.Properties.Resources.tool_rect_filledW;
                else if (Root.FilledSelected == 3)
                    btRect.BackgroundImage = global::gInk.Properties.Resources.tool_rect_filledB;

            }
            else if (tool == 3)
            {
                if (Root.FilledSelected == 0)
                    btOval.BackgroundImage = global::gInk.Properties.Resources.tool_oval_act;
                else if (Root.FilledSelected == 1)
                    btOval.BackgroundImage = global::gInk.Properties.Resources.tool_oval_filledC;
                else if (Root.FilledSelected == 2)
                    btOval.BackgroundImage = global::gInk.Properties.Resources.tool_oval_filledW;
                else if (Root.FilledSelected == 3)
                    btOval.BackgroundImage = global::gInk.Properties.Resources.tool_oval_filledB;
            }
            else if ((tool == 4) || (tool == 5)) // also include tool=5
                if ((tool == 5) || (Root.ToolSelected == 4))
                {
                    btArrow.BackgroundImage = global::gInk.Properties.Resources.tool_enAr_act;
                    tool = 5;
                }
                else
                {
                    btArrow.BackgroundImage = global::gInk.Properties.Resources.tool_stAr_act;
                    tool = 4;
                }
            else if (tool == 6)
            {
                if (Root.FilledSelected == 0)
                    btNumb.BackgroundImage = global::gInk.Properties.Resources.tool_numb_act;
                else if (Root.FilledSelected == 1)
                { // we use the state FilledColor to do the modification of the tag number
                    SetTagNumber();
                    btNumb.BackgroundImage = global::gInk.Properties.Resources.tool_numb_act;
                }
                else if (Root.FilledSelected == 2)
                    btNumb.BackgroundImage = global::gInk.Properties.Resources.tool_numb_fillW;
                else if (Root.FilledSelected == 3)
                    btNumb.BackgroundImage = global::gInk.Properties.Resources.tool_numb_fillB;
            }
            else if (tool == 7)
                btEdit.BackgroundImage = global::gInk.Properties.Resources.tool_edit_act;
            else if ((tool == 8) || (tool == 9))
                if ((tool == 9) || (Root.ToolSelected == 8))
                {
                    btText.BackgroundImage = global::gInk.Properties.Resources.tool_txtR_act;
                    tool = 9;
                }
                else
                {
                    btText.BackgroundImage = global::gInk.Properties.Resources.tool_txtL_act;
                    tool = 8;
                }
            else if (tool == 10)
            {
                //SelectPen(LastPenSelected);
                btPan.BackgroundImage = global::gInk.Properties.Resources.pan1_act;
            }
            Root.ToolSelected = tool;
        }

        public void SelectPen(int pen)
		{
            btPan.BackgroundImage = global::gInk.Properties.Resources.pan;
            // -3=pan, -2=pointer, -1=erasor, 0+=pens
            if (pen == -3)
			{
                if (AltKeyPressed() && SavedPen < 0)
                {
                    SavedPen = LastPenSelected;
                }
                SelectTool(-1, 0);       // Alt will be processed inhere
                for (int b = 0; b < Root.MaxPenCount; b++)
					btPen[b].Image = image_pen[b];
				btEraser.Image = image_eraser;
				btPointer.Image = image_pointer;
                btPan.BackgroundImage = global::gInk.Properties.Resources.pan_act;
                EnterEraserMode(false);
				Root.UnPointer();
				Root.PanMode = true;

				try
				{
					IC.SetWindowInputRectangle(new Rectangle(0, 0, 1, 1));
				}
				catch
				{
					Thread.Sleep(1); 
					IC.SetWindowInputRectangle(new Rectangle(0, 0, 1, 1));
				}
			}
			else if (pen == -2)
			{
                if (AltKeyPressed() && SavedPen < 0)
                {
                    SavedPen = LastPenSelected;
                }
                SelectTool(-1, 0);       // Alt will be processed inhere
                for (int b = 0; b < Root.MaxPenCount; b++)
					btPen[b].Image = image_pen[b];
				btEraser.Image = image_eraser;
				btPointer.Image = image_pointer_act;
				EnterEraserMode(false);
				Root.Pointer();
				Root.PanMode = false;
			}
			else if (pen == -1)
			{
                if (AltKeyPressed() && SavedPen < 0)
                {
                    SavedPen = LastPenSelected;
                }
                SelectTool(-1,0);       // Alt will be processed inhere
                //if (this.Cursor != System.Windows.Forms.Cursors.Default)
				//	this.Cursor = System.Windows.Forms.Cursors.Default;
                
                for (int b = 0; b < Root.MaxPenCount; b++)
					btPen[b].Image = image_pen[b];
				btEraser.Image = image_eraser_act;
				btPointer.Image = image_pointer;
				EnterEraserMode(true);
				Root.UnPointer();
				Root.PanMode = false;

				IC.Cursor = cursorerase;

				try
				{
					IC.SetWindowInputRectangle(new Rectangle(0, 0, this.Width, this.Height));
				}
				catch
				{
					Thread.Sleep(1);
					IC.SetWindowInputRectangle(new Rectangle(0, 0, this.Width, this.Height));
				}
			}
			else if (pen >= 0)
			{
                if (AltKeyPressed() && pen != LastPenSelected && SavedPen < 0)
                {
                    SavedPen = LastPenSelected;
                }
                LastPenSelected = pen;
                if (this.Cursor != System.Windows.Forms.Cursors.Default)
					this.Cursor = System.Windows.Forms.Cursors.Default;

				IC.DefaultDrawingAttributes = Root.PenAttr[pen].Clone();
				if (Root.PenWidthEnabled && !Root.WidthAtPenSel)
				{
					IC.DefaultDrawingAttributes.Width = Root.GlobalPenWidth;
				}
                IC.DefaultDrawingAttributes.FitToCurve = true;
                for (int b = 0; b < Root.MaxPenCount; b++)
					btPen[b].Image = image_pen[b];
				btPen[pen].Image = image_pen_act[pen];
				btEraser.Image = image_eraser;
				btPointer.Image = image_pointer;
				EnterEraserMode(false);
				Root.UnPointer();
				Root.PanMode = false;

				if (Root.CanvasCursor == 0)
				{
					//cursorred = new System.Windows.Forms.Cursor(gInk.Properties.Resources.cursorred.Handle);
					IC.Cursor = cursorred;
				}
				else if (Root.CanvasCursor == 1)
					SetPenTipCursor();

				try
				{
					IC.SetWindowInputRectangle(new Rectangle(0, 0, this.Width, this.Height));
				}
				catch
				{
					Thread.Sleep(1);
					IC.SetWindowInputRectangle(new Rectangle(0, 0, this.Width, this.Height));
				}
			}
			Root.CurrentPen = pen;
			if (Root.gpPenWidthVisible)
			{
				Root.gpPenWidthVisible = false;
				Root.UponSubPanelUpdate = true;
			}
			else
				Root.UponButtonsUpdate |= 0x2;

			if (pen != -2)
				Root.LastPen = pen;
		}

		public void RetreatAndExit()
		{
			ToThrough();
			Root.ClearInk();
			SaveUndoStrokes();
			//Root.SaveOptions("config.ini");
			Root.gpPenWidthVisible = false;

			LastTickTime = DateTime.Now;
			ButtonsEntering = -9;
		}

		public void btDock_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			LastTickTime = DateTime.Now;
			if (!Root.Docked)
			{
				Root.Dock();
			}
			else
			{
				Root.UnDock();
			}
		}

		public void btPointer_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}
            if(!Root.PointerMode)
            {
                SavedTool = Root.ToolSelected;
                SavedFilled = Root.FilledSelected;
			    SelectPen(-2);
            }
            else
            {
                SelectPen(LastPenSelected);
                SelectTool(SavedTool, SavedFilled);
                SavedTool = -1;
                SavedFilled = -1;
            }
		}


		private void btPenWidth_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			if (Root.PointerMode)
				return;

			Root.gpPenWidthVisible = !Root.gpPenWidthVisible;
			if (Root.gpPenWidthVisible)
            {
                pboxPenWidthIndicator.Left = (int)Math.Sqrt(IC.DefaultDrawingAttributes.Width * 30);
				Root.UponButtonsUpdate |= 0x2;
            }
            else
                Root.UponSubPanelUpdate = true;
		}

		public void btSnap_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			if (Root.Snapping > 0)
				return;

			cursorsnap = new System.Windows.Forms.Cursor(gInk.Properties.Resources.cursorsnap.Handle);
			this.Cursor = cursorsnap;

			Root.gpPenWidthVisible = false;

			try
			{
				IC.SetWindowInputRectangle(new Rectangle(0, 0, 1, 1));
			}
			catch
			{
				Thread.Sleep(1);
				IC.SetWindowInputRectangle(new Rectangle(0, 0, 1, 1));
			}
			Root.SnappingX = -1;
			Root.SnappingY = -1;
			Root.SnappingRect = new Rectangle(0, 0, 0, 0);
			Root.Snapping = 1;
			ButtonsEntering = -2;
			Root.UnPointer();
		}

		public void ExitSnapping()
		{
			try
			{
				IC.SetWindowInputRectangle(new Rectangle(0, 0, this.Width, this.Height));
			}
			catch
			{
				Thread.Sleep(1);
				IC.SetWindowInputRectangle(new Rectangle(0, 0, this.Width, this.Height));
			}
			Root.SnappingX = -1;
			Root.SnappingY = -1;
			Root.Snapping = -60;
			ButtonsEntering = 1;
			Root.SelectPen(Root.CurrentPen);

			this.Cursor = System.Windows.Forms.Cursors.Default;
		}

		public void btStop_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			RetreatAndExit();
		}

		DateTime LastTickTime;
		bool[] LastPenStatus = new bool[10];
		bool LastEraserStatus = false;
		bool LastVisibleStatus = false;
		bool LastPointerStatus = false;
		bool LastPanStatus = false;
		bool LastUndoStatus = false;
		bool LastRedoStatus = false;
		bool LastSnapStatus = false;
		bool LastClearStatus = false;
        bool LastVideoStatus = false;
        bool LastDockStatus = false;
        bool LastHandStatus = false;
        bool LastLineStatus = false;
        bool LastRectStatus = false;
        bool LastOvalStatus = false;
        bool LastArrowStatus = false;
        bool LastNumbStatus = false;
        bool LastTextStatus = false;
        bool LastEditStatus = false;
        bool LastMoveStatus = false;
        bool LastMagnetStatus = false;

        private void gpPenWidth_MouseDown(object sender, MouseEventArgs e)
		{
			gpPenWidth_MouseOn = true;
		}

		private void gpPenWidth_MouseMove(object sender, MouseEventArgs e)
		{
			if (gpPenWidth_MouseOn)
			{
				if (e.X < 10 || gpPenWidth.Width - e.X < 10)
					return;

				Root.GlobalPenWidth = e.X * e.X / 30;
				pboxPenWidthIndicator.Left = e.X - pboxPenWidthIndicator.Width / 2;
				IC.DefaultDrawingAttributes.Width = Root.GlobalPenWidth;
				Root.UponButtonsUpdate |= 0x2;
			}
		}

		private void gpPenWidth_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.X >= 10 && gpPenWidth.Width - e.X >= 10)
			{
				Root.GlobalPenWidth = e.X * e.X / 30;
				pboxPenWidthIndicator.Left = e.X - pboxPenWidthIndicator.Width / 2;
				IC.DefaultDrawingAttributes.Width = Root.GlobalPenWidth;
			}

			if (Root.CanvasCursor == 1)
				SetPenTipCursor();

			Root.gpPenWidthVisible = false;
			Root.UponSubPanelUpdate = true;
			gpPenWidth_MouseOn = false;
		}

		private void pboxPenWidthIndicator_MouseDown(object sender, MouseEventArgs e)
		{
			gpPenWidth_MouseOn = true;
		}

		private void pboxPenWidthIndicator_MouseMove(object sender, MouseEventArgs e)
		{
			if (gpPenWidth_MouseOn)
			{
				int x = e.X + pboxPenWidthIndicator.Left;
				if (x < 10 || gpPenWidth.Width - x < 10)
					return;

				Root.GlobalPenWidth = x * x / 30;
				pboxPenWidthIndicator.Left = x - pboxPenWidthIndicator.Width / 2;
				IC.DefaultDrawingAttributes.Width = Root.GlobalPenWidth;
				Root.UponButtonsUpdate |= 0x2;
			}
		}

		private void pboxPenWidthIndicator_MouseUp(object sender, MouseEventArgs e)
		{
			if (Root.CanvasCursor == 1)
				SetPenTipCursor();

			Root.gpPenWidthVisible = false;
			Root.UponSubPanelUpdate = true;
			gpPenWidth_MouseOn = false;
		}

		private void SetPenTipCursor()
		{
			Bitmap bitmaptip = (Bitmap)(gInk.Properties.Resources._null).Clone();
			Graphics g = Graphics.FromImage(bitmaptip);
			DrawingAttributes dda = IC.DefaultDrawingAttributes;
			Brush cbrush;
			Point widt;
			if (!Root.EraserMode)
			{
				cbrush = new SolidBrush(IC.DefaultDrawingAttributes.Color);
				//Brush cbrush = new SolidBrush(Color.FromArgb(255 - dda.Transparency, dda.Color.R, dda.Color.G, dda.Color.B));
				widt = new Point((int)IC.DefaultDrawingAttributes.Width, 0);
			}
			else
			{
				cbrush = new SolidBrush(Color.Black);
				widt = new Point(60, 0);
			}
			IC.Renderer.InkSpaceToPixel(IC.Handle, ref widt);

			IntPtr screenDc = GetDC(IntPtr.Zero);
			const int VERTRES = 10;
			const int DESKTOPVERTRES = 117;
			int LogicalScreenHeight = GetDeviceCaps(screenDc, VERTRES);
			int PhysicalScreenHeight = GetDeviceCaps(screenDc, DESKTOPVERTRES);
			float ScreenScalingFactor = (float)PhysicalScreenHeight / (float)LogicalScreenHeight;
			ReleaseDC(IntPtr.Zero, screenDc);

			int dia = Math.Max((int)(widt.X * ScreenScalingFactor), 2);
			g.FillEllipse(cbrush, 64 - dia / 2, 64 - dia / 2, dia, dia);
			if (dia <= 5)
			{
				Pen cpen = new Pen(Color.FromArgb(50, 128, 128, 128), 2);
				dia += 6;
				g.DrawEllipse(cpen, 64 - dia / 2, 64 - dia / 2, dia, dia);
			}
			IC.Cursor = new System.Windows.Forms.Cursor(bitmaptip.GetHicon());
			
		}

        short LastESCStatus = 0;
		private void tiSlide_Tick(object sender, EventArgs e)
		{
			// ignore the first tick
			if (LastTickTime.Year == 1987)
			{
				LastTickTime = DateTime.Now;
				return;
			}

			int aimedleft = gpButtonsLeft;
			if (ButtonsEntering == -9)
			{
				aimedleft = gpButtonsLeft + gpButtonsWidth;
			}
			else if (ButtonsEntering < 0)
			{
				if (Root.Snapping > 0)
					aimedleft = gpButtonsLeft + gpButtonsWidth + 0;
				else if (Root.Docked)
					aimedleft = gpButtonsLeft + gpButtonsWidth - btDock.Right;
			}
			else if (ButtonsEntering > 0)
			{
				if (Root.Docked)
					aimedleft = gpButtonsLeft + gpButtonsWidth - btDock.Right;
				else
					aimedleft = gpButtonsLeft;
			}
			else if (ButtonsEntering == 0)
			{
				aimedleft = gpButtons.Left; // stay at current location
            }

            if (gpButtons.Left > aimedleft)
			{
				float dleft = gpButtons.Left - aimedleft;
				dleft /= 70;
				if (dleft > 8) dleft = 8;
				dleft *= (float)(DateTime.Now - LastTickTime).TotalMilliseconds;
				if (dleft > 120) dleft = 230;
				if (dleft < 1) dleft = 1;
				gpButtons.Left -= (int)dleft;
				LastTickTime = DateTime.Now;
				if (gpButtons.Left < aimedleft)
				{
					gpButtons.Left = aimedleft;
				}
				gpButtons.Width = Math.Max(gpButtonsWidth - (gpButtons.Left - gpButtonsLeft), btDock.Width);
				Root.UponButtonsUpdate |= 0x1;
			}
			else if (gpButtons.Left < aimedleft)
			{
				float dleft = aimedleft - gpButtons.Left;
				dleft /= 70;
				if (dleft > 8) dleft = 8;
				// fast exiting when not docked
				if (ButtonsEntering == -9 && !Root.Docked)
					dleft = 8;
				dleft *= (float)(DateTime.Now - LastTickTime).TotalMilliseconds;
				if (dleft > 120) dleft = 120;
				if (dleft < 1) dleft = 1;
				// fast exiting when docked
				if (ButtonsEntering == -9 && dleft == 1)
					dleft = 2;
				gpButtons.Left += (int)dleft;
				LastTickTime = DateTime.Now;
				if (gpButtons.Left > aimedleft)
				{
					gpButtons.Left = aimedleft;
				}
				gpButtons.Width = Math.Max(gpButtonsWidth - (gpButtons.Left - gpButtonsLeft), btDock.Width);
				Root.UponButtonsUpdate |= 0x1;
				Root.UponButtonsUpdate |= 0x4;
			}

			if (ButtonsEntering == -9 && gpButtons.Left == aimedleft)
			{
				tiSlide.Enabled = false;
				Root.StopInk();
				return;
			}
			else if (ButtonsEntering < 0)
			{
				Root.UponAllDrawingUpdate = true;
				Root.UponButtonsUpdate = 0;
			}
			if ((gpButtons.Left == aimedleft) && (ButtonsEntering != 0))
            {
                // add a background if required at opening but not when snapping is in progress
                if (Root.Snapping==0)
                {
                    if ((Root.BoardAtOpening == 1) || (Root.BoardAtOpening == 4 && Root.BoardSelected == 1)) // White
                        AddBackGround(255, 255, 255, 255);
                else if ((Root.BoardAtOpening == 2) || (Root.BoardAtOpening == 4 && Root.BoardSelected == 2)) // Customed
                        AddBackGround(Root.Gray1[0], Root.Gray1[1], Root.Gray1[2], Root.Gray1[3]);
                    else if ((Root.BoardAtOpening == 3) || (Root.BoardAtOpening == 4 && Root.BoardSelected == 3)) // Black
                        AddBackGround(255, 0, 0, 0);
                    if (Root.BoardAtOpening != 4)    // reset the board selected at opening
                    {
                        Root.BoardSelected = Root.BoardAtOpening;
                    }
                }
                 ButtonsEntering = 0;
            }



            if (!Root.PointerMode && !this.TopMost)
				ToTopMost();

			// gpPenWidth status

			if (Root.gpPenWidthVisible != gpPenWidth.Visible)
				gpPenWidth.Visible = Root.gpPenWidthVisible;

			bool pressed;

			if (!Root.PointerMode)
			{
				// ESC key : Exit
				short retVal;
                if (Root.Hotkey_Close.Key != 0)
                {
                    retVal = GetKeyState(Root.Hotkey_Close.Key);
                    if ((retVal & 0x8000) == 0x8000 && (LastESCStatus & 0x8000) == 0x0000 && !TextEdited)
                    {
                        if (Root.Snapping > 0)
                        {
                            ExitSnapping();
					}
					else if (Root.gpPenWidthVisible)
					{
						Root.gpPenWidthVisible = false;
						Root.UponSubPanelUpdate = true;
					}
					else if (Root.Snapping == 0)
						RetreatAndExit();
				}
                    LastESCStatus = retVal;
                    TextEdited = false;
                }
            }

            if (!AltKeyPressed() && !Root.PointerMode )//&& (SavedPen>=0 || SavedTool>=0))
            {
                if (SavedPen >= 0)
                {
                    SelectPen(SavedPen);
                    SavedPen = -1;
                }
                if (SavedTool >= 0)
                {
                    SelectTool(SavedTool,SavedFilled);
                    SavedTool = -1;
                    SavedFilled = -1;
                }
            }

            if ((AltKeyPressed() && !Root.FingerInAction) && tempArrowCursor is null)
            {
                tempArrowCursor = IC.Cursor;
                IC.Cursor = cursorred;
            }
            else if (!(tempArrowCursor is null) && !AltKeyPressed())
            {
                IC.Cursor = tempArrowCursor;
                tempArrowCursor = null;
            }

            if (!Root.FingerInAction && (!Root.PointerMode || Root.AllowHotkeyInPointerMode) && Root.Snapping <= 0)
			{
				bool control = ((short)(GetKeyState(VK_LCONTROL) | GetKeyState(VK_RCONTROL)) & 0x8000) == 0x8000;
                //bool alt = (((short)(GetKeyState(VK_LMENU) | GetKeyState(VK_RMENU)) & 0x8000) == 0x8000);
                int alt = Root.AltAsOneCommand?-1:(AltKeyPressed() ? 1 : 0);
                bool shift = ((short)(GetKeyState(VK_LSHIFT) | GetKeyState(VK_RSHIFT)) & 0x8000) == 0x8000;
				bool win = ((short)(GetKeyState(VK_LWIN) | GetKeyState(VK_RWIN)) & 0x8000) == 0x8000;

				for (int p = 0; p < Root.MaxPenCount; p++)
				{
					pressed = (GetKeyState(Root.Hotkey_Pens[p].Key) & 0x8000) == 0x8000;
					if(pressed && !LastPenStatus[p] && Root.Hotkey_Pens[p].ModifierMatch(control, alt, shift, win))
					{
						SelectPen(p);
					}
					LastPenStatus[p] = pressed;
				}

				pressed = (GetKeyState(Root.Hotkey_Eraser.Key) & 0x8000) == 0x8000;
				if (pressed && !LastEraserStatus && Root.Hotkey_Eraser.ModifierMatch(control, alt, shift, win))
				{
					SelectPen(-1);
				}
				LastEraserStatus = pressed;

				pressed = (GetKeyState(Root.Hotkey_InkVisible.Key) & 0x8000) == 0x8000;
				if (pressed && !LastVisibleStatus && Root.Hotkey_InkVisible.ModifierMatch(control, alt, shift, win))
				{
					btInkVisible_Click(null, null);
				}
				LastVisibleStatus = pressed;

				pressed = (GetKeyState(Root.Hotkey_Undo.Key) & 0x8000) == 0x8000;
				if (pressed && !LastUndoStatus && Root.Hotkey_Undo.ModifierMatch(control, alt, shift, win))
				{
					if (!Root.InkVisible)
						Root.SetInkVisible(true);

					Root.UndoInk();
				}
				LastUndoStatus = pressed;

				pressed = (GetKeyState(Root.Hotkey_Redo.Key) & 0x8000) == 0x8000;
				if (pressed && !LastRedoStatus && Root.Hotkey_Redo.ModifierMatch(control, alt, shift, win))
				{
					Root.RedoInk();
				}
				LastRedoStatus = pressed;

				pressed = (GetKeyState(Root.Hotkey_Pointer.Key) & 0x8000) == 0x8000;
				if (pressed && !LastPointerStatus && Root.Hotkey_Pointer.ModifierMatch(control, alt, shift, win))
				{
                    //SelectPen(-2);
                    btPointer_Click(null, null);
				}
				LastPointerStatus = pressed;

				pressed = (GetKeyState(Root.Hotkey_Pan.Key) & 0x8000) == 0x8000;
				if (pressed && !LastPanStatus && Root.Hotkey_Pan.ModifierMatch(control, alt, shift, win))
				{
                    btPan_Click(null, null);//SelectPen(-3);
                }
				LastPanStatus = pressed;

				pressed = (GetKeyState(Root.Hotkey_Clear.Key) & 0x8000) == 0x8000;
				if (pressed && !LastClearStatus && Root.Hotkey_Clear.ModifierMatch(control, alt, shift, win))
				{
					btClear_Click(null, null);
				}
				LastClearStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Video.Key) & 0x8000) == 0x8000;
                if (pressed && !LastVideoStatus && Root.Hotkey_Video.ModifierMatch(control, alt, shift, win))
                {
                    btVideo_Click(null, null);
                }
                LastVideoStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_DockUndock.Key) & 0x8000) == 0x8000;
                if (pressed && !LastDockStatus && Root.Hotkey_DockUndock.ModifierMatch(control, alt, shift, win))
                {
                    Console.WriteLine("DockKey");
                    btDock_Click(null, null);
                }
                LastDockStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Snap.Key) & 0x8000) == 0x8000;
				if (pressed && !LastSnapStatus && Root.Hotkey_Snap.ModifierMatch(control, alt, shift, win))
				{
                    btSnap_Click(null, null);
				}
				LastSnapStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Hand.Key) & 0x8000) == 0x8000;
                if (pressed && !LastHandStatus && Root.Hotkey_Hand.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btHand,null);
                }
                LastHandStatus= pressed;

                pressed = (GetKeyState(Root.Hotkey_Line.Key) & 0x8000) == 0x8000;
                if (pressed && !LastLineStatus && Root.Hotkey_Line.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btLine, null);
                }
                LastLineStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Rect.Key) & 0x8000) == 0x8000;
                if (pressed && !LastRectStatus && Root.Hotkey_Rect.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btRect, null);
                }
                LastRectStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Oval.Key) & 0x8000) == 0x8000;
                if (pressed && !LastOvalStatus && Root.Hotkey_Oval.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btOval, null);
                }
                LastOvalStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Arrow.Key) & 0x8000) == 0x8000;
                if (pressed && !LastArrowStatus && Root.Hotkey_Arrow.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btArrow, null);
                }
                LastArrowStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Numb.Key) & 0x8000) == 0x8000;
                if (pressed && !LastNumbStatus && Root.Hotkey_Numb.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btNumb, null);
                }
                LastNumbStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Text.Key) & 0x8000) == 0x8000;
                if (pressed && !LastTextStatus && Root.Hotkey_Text.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btText, null);
                }
                LastTextStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Edit.Key) & 0x8000) == 0x8000;
                if (pressed && !LastEditStatus && Root.Hotkey_Edit.ModifierMatch(control, alt, shift, win))
                {
                    btTool_Click(btEdit, null);
                }
                LastEditStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Move.Key) & 0x8000) == 0x8000;
                if (pressed && !LastMoveStatus && Root.Hotkey_Move.ModifierMatch(control, alt, shift, win))
                {
                    btPan_Click(null, null);
                }
                LastMoveStatus = pressed;

                pressed = (GetKeyState(Root.Hotkey_Magnet.Key) & 0x8000) == 0x8000;
                if (pressed && !LastMagnetStatus && Root.Hotkey_Magnet.ModifierMatch(control, alt, shift, win))
                {
                    btMagn_Click(null,null);
                }
                LastMagnetStatus = pressed;
                
            }

            if (Root.Snapping < 0)
				Root.Snapping++;
		}

		private bool IsInsideVisibleScreen(int x, int y)
		{
			x -= PrimaryLeft;
			y -= PrimaryTop;
			//foreach (Screen s in Screen.AllScreens)
			//	Console.WriteLine(s.Bounds);
			//Console.WriteLine(x.ToString() + ", " + y.ToString());

			foreach (Screen s in Screen.AllScreens)
				if (s.Bounds.Contains(x, y))
					return true;
			return false;
		}

		int IsMovingToolbar = 0;
		Point HitMovingToolbareXY = new Point();
		bool ToolbarMoved = false;
		private void gpButtons_MouseDown(object sender, MouseEventArgs e)
		{
			if (!Root.AllowDraggingToolbar)
				return;
			if (ButtonsEntering != 0)
				return;

			ToolbarMoved = false;
			IsMovingToolbar = 1;
			HitMovingToolbareXY.X = e.X;
			HitMovingToolbareXY.Y = e.Y;
		}

		private void gpButtons_MouseMove(object sender, MouseEventArgs e)
		{
			if (IsMovingToolbar == 1)
			{
				if (Math.Abs(e.X - HitMovingToolbareXY.X) > 20 || Math.Abs(e.Y - HitMovingToolbareXY.Y) > 20)
					IsMovingToolbar = 2;
			}
			if (IsMovingToolbar == 2)
			{
				if (e.X != HitMovingToolbareXY.X || e.Y != HitMovingToolbareXY.Y)
				{
					/*
					gpButtonsLeft += e.X - HitMovingToolbareXY.X;
					gpButtonsTop += e.Y - HitMovingToolbareXY.Y;
					
					if (gpButtonsLeft + gpButtonsWidth > SystemInformation.VirtualScreen.Right)
						gpButtonsLeft = SystemInformation.VirtualScreen.Right - gpButtonsWidth;
					if (gpButtonsLeft < SystemInformation.VirtualScreen.Left)
						gpButtonsLeft = SystemInformation.VirtualScreen.Left;
					if (gpButtonsTop + gpButtonsHeight > SystemInformation.VirtualScreen.Bottom)
						gpButtonsTop = SystemInformation.VirtualScreen.Bottom - gpButtonsHeight;
					if (gpButtonsTop < SystemInformation.VirtualScreen.Top)
						gpButtonsTop = SystemInformation.VirtualScreen.Top;
					*/
					int newleft = gpButtonsLeft + e.X - HitMovingToolbareXY.X;
					int newtop = gpButtonsTop + e.Y - HitMovingToolbareXY.Y;

					bool continuemoving;
					bool toolbarmovedthisframe = false;
					int dleft = 0, dtop = 0;
					if
					(
						IsInsideVisibleScreen(newleft, newtop) &&
						IsInsideVisibleScreen(newleft + gpButtonsWidth, newtop) &&
						IsInsideVisibleScreen(newleft, newtop + gpButtonsHeight) &&
						IsInsideVisibleScreen(newleft + gpButtonsWidth, newtop + gpButtonsHeight)
					)
					{
						continuemoving = true;
						ToolbarMoved = true;
						toolbarmovedthisframe = true;
						dleft = newleft - gpButtonsLeft;
						dtop = newtop - gpButtonsTop;
					}
					else
					{
						do
						{
							if (dleft != newleft - gpButtonsLeft)
								dleft += Math.Sign(newleft - gpButtonsLeft);
							else
								break;
							if
							(
								IsInsideVisibleScreen(gpButtonsLeft + dleft, gpButtonsTop + dtop) &&
								IsInsideVisibleScreen(gpButtonsLeft + gpButtonsWidth + dleft, gpButtonsTop + dtop) &&
								IsInsideVisibleScreen(gpButtonsLeft + dleft, gpButtonsTop + gpButtonsHeight + dtop) &&
								IsInsideVisibleScreen(gpButtonsLeft + gpButtonsWidth + dleft, gpButtonsTop + gpButtonsHeight + dtop)
							)
							{
								continuemoving = true;
								ToolbarMoved = true;
								toolbarmovedthisframe = true;
							}
							else
							{
								continuemoving = false;
								dleft -= Math.Sign(newleft - gpButtonsLeft);
							}
						}
						while (continuemoving);
						do
						{
							if (dtop != newtop - gpButtonsTop)
								dtop += Math.Sign(newtop - gpButtonsTop);
							else
								break;
							if
							(
								IsInsideVisibleScreen(gpButtonsLeft + dleft, gpButtonsTop + dtop) &&
								IsInsideVisibleScreen(gpButtonsLeft + gpButtonsWidth + dleft, gpButtonsTop + dtop) &&
								IsInsideVisibleScreen(gpButtonsLeft + dleft, gpButtonsTop + gpButtonsHeight + dtop) &&
								IsInsideVisibleScreen(gpButtonsLeft + gpButtonsWidth + dleft, gpButtonsTop + gpButtonsHeight + dtop)
							)
							{
								continuemoving = true;
								ToolbarMoved = true;
								toolbarmovedthisframe = true;
							}
							else
							{
								continuemoving = false;
								dtop -= Math.Sign(newtop - gpButtonsTop);
							}
						}
						while (continuemoving);
					}

					if (toolbarmovedthisframe)
					{
						gpButtonsLeft += dleft;
						gpButtonsTop += dtop;
						Root.gpButtonsLeft = gpButtonsLeft;
						Root.gpButtonsTop = gpButtonsTop;
						if (Root.Docked)
							gpButtons.Left = gpButtonsLeft + gpButtonsWidth - btDock.Right;
						else
							gpButtons.Left = gpButtonsLeft;
						gpPenWidth.Left = gpButtonsLeft + btPenWidth.Left - gpPenWidth.Width / 2 + btPenWidth.Width / 2;
						gpPenWidth.Top = gpButtonsTop - gpPenWidth.Height - 10;
						gpButtons.Top = gpButtonsTop;
						Root.UponAllDrawingUpdate = true;
					}
				}
			}
		}

		private void gpButtons_MouseUp(object sender, MouseEventArgs e)
		{
			IsMovingToolbar = 0;
		}

		private void btInkVisible_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			Root.SetInkVisible(!Root.InkVisible);
		}

        private Stroke AddBackGround(int A, int B, int C, int D)
        {
            Stroke stk = AddRectStroke(SystemInformation.VirtualScreen.Left, SystemInformation.VirtualScreen.Top,
                                      SystemInformation.VirtualScreen.Right, SystemInformation.VirtualScreen.Bottom, 1);
            stk.DrawingAttributes.Transparency = (byte)(255-A);
            stk.DrawingAttributes.Color = Color.FromArgb(A, B, C, D);
            SaveUndoStrokes();
            Root.UponAllDrawingUpdate = true;
            return stk;
        }

        private int SelectCleanBackground()
        {
            void CleanBackGround_click(object sender, EventArgs e)
            {
                (sender as Control).Parent.Tag = sender;
            }
            Form prompt = new Form();
            prompt.Width = 525;
            prompt.Height = 150;
            prompt.Text = Root.Local.BoardTitle;
            prompt.StartPosition = FormStartPosition.CenterScreen;
            prompt.TopMost = true;

            Label textLabel = new Label() { Left = 50, Top = 10, AutoSize = true, Text = Root.Local.BoardText };
            prompt.Controls.Add(textLabel);

            Button btn1 = new Button() { Text = Root.Local.BoardTransparent, Left = 25, Width = 100, Top = 30, Name = "0", DialogResult = DialogResult.Yes };
            btn1.Click += CleanBackGround_click;
            prompt.Controls.Add(btn1);

            Button btn2 = new Button() { Text = Root.Local.BoardWhite, Left = 150, Width = 100, Top = 30, Name = "1", DialogResult = DialogResult.Yes };
            btn2.Click += CleanBackGround_click;
            prompt.Controls.Add(btn2);

            Button btn3 = new Button() { Text = Root.Local.BoardGray, Left = 275, Width = 100, Top = 30, Name = "2", DialogResult = DialogResult.Yes };
            btn3.BackColor = Color.FromArgb(Root.Gray1[0], Root.Gray1[1], Root.Gray1[2], Root.Gray1[3]);
            prompt.Controls.Add(btn3);
            btn3.Click += CleanBackGround_click;

            /*Button btn4 = new Button() { Text = Root.Local.BoardGray + " (2)", Left = 400, Width = 100, Top = 30, Name = "Gray2", DialogResult = DialogResult.Yes };
            prompt.Controls.Add(btn4);
            btn4.Click += CleanBackGround_click;*/

            //Button btn5 = new Button() { Text = Root.Local.BoardBlack, Left = 25, Width = 100, Top = 60, Name = "Black", DialogResult = DialogResult.Yes };
            Button btn5 = new Button() { Text = Root.Local.BoardBlack, Left = 400, Width = 100, Top = 30, Name = "3", DialogResult = DialogResult.Yes };
            prompt.Controls.Add(btn5);
            btn5.Click += CleanBackGround_click;

            Button btnCancel = new Button() { Text = Root.Local.ButtonCancelText, Left = 350, Width = 100, Top = 80, DialogResult = DialogResult.Cancel };
            prompt.Controls.Add(btnCancel);

            tiSlide.Stop();
            IC.Enabled = false;
            TextEdited = true;
            DialogResult rst = prompt.ShowDialog();
            tiSlide.Start();
            IC.Enabled = true;

            if (rst == DialogResult.Yes)
                return Int32.Parse((prompt.Tag as Control).Name);
            else
                return -1;
        }

        public void btClear_Click(object sender, EventArgs e)
        {
            //if(sender != null)
            //    (sender as Button).RightToLeft = RightToLeft.No;
            btClear.RightToLeft = RightToLeft.No;
            longClickTimer.Stop(); // for an unkown reason the mouse arrives later
            if (sender is ContextMenu) 
            {
                sender = (sender as ContextMenu).SourceControl;
                MouseTimeDown = DateTime.FromBinary(0);
            }
            if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

            TimeSpan tsp = DateTime.Now - MouseTimeDown;

            if (sender != null && tsp.TotalSeconds > Root.LongClickTime)
            {   
                int rst = SelectCleanBackground();
                if (rst >= 0)
                {
                    Root.BoardSelected = rst;
                }
                else
                    return;
            }
			//Root.ClearInk(false); <-- code exploded inhere removing clearcanvus
            Root.FormCollection.IC.Ink.DeleteStrokes();
            if (Root.BoardSelected == 1) // White
                AddBackGround(255, 255, 255, 255);
            else if (Root.BoardSelected == 2) // Customed
                AddBackGround(Root.Gray1[0], Root.Gray1[1], Root.Gray1[2], Root.Gray1[3]);
            else if (Root.BoardSelected == 3) // Black
                AddBackGround(255,0,0,0);
            SaveUndoStrokes();
            // transferred from ClearInk to prevent some blinking
            if (Root.BoardSelected == 0)
            {
                Root.FormDisplay.ClearCanvus();
            }
            Root.FormDisplay.DrawButtons(true);
            Root.FormDisplay.UpdateFormDisplay(true);
        }

        private void btUndo_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			if (!Root.InkVisible)
				Root.SetInkVisible(true);

			Root.UndoInk();
		}

        public void btColor_LongClick(object sender)
        {
            for (int b = 0; b < Root.MaxPenCount; b++)
                if ((Button)sender == btPen[b])
                {
                    tiSlide.Stop();
                    IC.Enabled = false;
                    //ToThrough();
                    TextEdited = true;

                    SelectPen(b);
                    Root.UponButtonsUpdate |= 0x2;
                    
                    if (PenModifyDlg.ModifyPen(ref Root.PenAttr[b]))
                    {
                        if ((Root.ToolSelected == 10) || (Root.ToolSelected == 5)) // if move
                            SelectTool(0);
                        PreparePenImages(Root.PenAttr[b].Transparency, ref image_pen[b], ref image_pen_act[b]);
                        btPen[b].Image = image_pen_act[b];
                        btPen[b].BackColor = Root.PenAttr[b].Color;
                        btPen[b].FlatAppearance.MouseDownBackColor = Root.PenAttr[b].Color;
                        btPen[b].FlatAppearance.MouseOverBackColor = Root.PenAttr[b].Color;
                        SelectPen(b);
                        Root.UponButtonsUpdate |= 0x2;
                    };
                    tiSlide.Start();
                    IC.Enabled = true;
                    //ToUnThrough();
                }
        }

        public void btColor_Click(object sender, EventArgs e)
		{
            longClickTimer.Stop();
            if (sender is ContextMenu)
            {
                sender = (sender as ContextMenu).SourceControl;
                MouseTimeDown = DateTime.FromBinary(0);
            }
            TimeSpan tsp = DateTime.Now - MouseTimeDown;
            //Console.WriteLine(string.Format("{1},t = {0:N3}", tsp.TotalSeconds,e.ToString()));
            if (sender != null && tsp.TotalSeconds > Root.LongClickTime)
            {
                btColor_LongClick(sender);
            }
            if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			for (int b = 0; b < Root.MaxPenCount; b++)
				if ((Button)sender == btPen[b])
				{
                    if ((Root.ToolSelected == 10) || (Root.ToolSelected == 5)) // if move
                        SelectTool(0);
                    SelectPen(b);
				}
		}

        private void btVideo_Click(object sender, EventArgs e)
        {
            // long click  = start/stop ; short click = pause(start if not started)/resume
            longClickTimer.Stop(); // for an unkown reason the mouse arrives later
            if (sender is ContextMenu)
            {
                sender = (sender as ContextMenu).SourceControl;
                MouseTimeDown = DateTime.FromBinary(0);
            }
            if (ToolbarMoved)
            {
                ToolbarMoved = false;
                return;
            }

            TimeSpan tsp = DateTime.Now - MouseTimeDown;
            if (Root.VideoRecordMode == VideoRecordMode.NoVideo) // button should be hidden but as security we do the check
                return;
            
            if (Root.VideoRecInProgress == VideoRecInProgress.Stopped ) // no recording so we start
            {
                VideoRecordStart();
            }
            else if ((sender != null && tsp.TotalSeconds > Root.LongClickTime) || Root.VideoRecordMode == VideoRecordMode.OBSBcst ) // there is only start/stop for Broadcast 
            {
                VideoRecordStop();
            }
            else if (Root.VideoRecInProgress == VideoRecInProgress.Recording )
            {
                VideoRecordPause();
            }
            else // recording & Shortclick & paused
            {
                VideoRecordResume();
            }
        }

        public void VideoRecordStart()
        {
            Root.VideoRecordCounter += 1;
            if (Root.VideoRecordMode == VideoRecordMode.FfmpegRec)
            {
                Root.VideoRecordWindowInProgress = true;
                btSnap_Click(null, null);
            }
            else
            {
                try
                {
                    Console.Write("-->" + (Root.ObsRecvTask == null).ToString());
                    if (Root.ObsRecvTask != null)
                        Console.Write(" ; " + Root.ObsRecvTask.IsCompleted.ToString());
                }
                finally
                {
                    Console.WriteLine();
                }
                if (Root.ObsRecvTask == null || Root.ObsRecvTask.IsCompleted)
                {
                    Root.ObsRecvTask = Task.Run(() => ReceiveObsMesgs(this));
                }
                Task.Run(() => ObsStartRecording(this));
            }
        }
        public void VideoRecordStartFFmpeg(Rectangle rect)
        {
            const int VERTRES = 10;
            const int DESKTOPVERTRES = 117;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            int LogicalScreenHeight = GetDeviceCaps(screenDc, VERTRES);
            int PhysicalScreenHeight = GetDeviceCaps(screenDc, DESKTOPVERTRES);
            float ScreenScalingFactor = (float)PhysicalScreenHeight / (float)LogicalScreenHeight;
            ReleaseDC(IntPtr.Zero, screenDc);

            rect.X = (int)(rect.X * ScreenScalingFactor);
            rect.Y = (int)(rect.Y * ScreenScalingFactor);
            rect.Width = (int)(rect.Width * ScreenScalingFactor);
            rect.Height = (int)(rect.Height * ScreenScalingFactor);

            Root.FFmpegProcess = new Process();
            string[] cmdArgs = Root.ExpandVarCmd(Root.FFMpegCmd, rect.X, rect.Y, rect.Width, rect.Height).Split(new char[] {' '}, 2);
            Console.WriteLine(string.Format("%s %s", cmdArgs[0], cmdArgs[1]));

            Root.FFmpegProcess.StartInfo.FileName = cmdArgs[0];
            Root.FFmpegProcess.StartInfo.Arguments = cmdArgs[1];

            Root.FFmpegProcess.StartInfo.UseShellExecute = false;
            Root.FFmpegProcess.StartInfo.CreateNoWindow = true;
            Root.FFmpegProcess.StartInfo.RedirectStandardOutput = true;
            Root.FFmpegProcess.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;

            Root.FFmpegProcess.Start();
            IntPtr ptr = Root.FFmpegProcess.MainWindowHandle;
            ShowWindow(ptr.ToInt32(), 2);

            Root.VideoRecInProgress = VideoRecInProgress.Recording;
            SetVidBgImage();
            //ExitSnapping();
        }

        static async Task ReceiveObsMesgs(FormCollection frm)
        {
            string HashEncode(string input)
            {
                var sha256 = new SHA256Managed();

                byte[] textBytes = Encoding.ASCII.GetBytes(input);
                byte[] hash = sha256.ComputeHash(textBytes);

                return System.Convert.ToBase64String(hash);
            }

            CancellationToken ct = frm.Root.ObsCancel.Token;
            frm.Root.VideoRecordWindowInProgress = true;
            if (ct.IsCancellationRequested)
                return; 
            if (frm.Root.ObsWs == null)
            {
                frm.Root.ObsWs = new ClientWebSocket();
                Console.WriteLine("WS Created");
            }
            var rcvBytes = new byte[4096];
            var rcvBuffer = new ArraySegment<byte>(rcvBytes);
            WebSocketReceiveResult rcvResult;
            if (frm.Root.ObsWs.State != WebSocketState.Open)
            {
                await frm.Root.ObsWs.ConnectAsync(new Uri(frm.Root.ObsUrl), ct);
                Console.WriteLine("WS Connected");
                await SendInWs(frm.Root.ObsWs, "GetAuthRequired", ct);
                rcvResult = await frm.Root.ObsWs.ReceiveAsync(rcvBuffer, ct);
                string st = Encoding.UTF8.GetString(rcvBuffer.Array, 0, rcvResult.Count);
                Console.WriteLine("getAuth => " + st);
                if (st.Contains("authRequired\": t"))
                {
                    int i = st.IndexOf("\"challenge\":");
                    i = st.IndexOf("\"",i+"\"challenge\":".Length+1)+1;
                    int j = st.IndexOf("\"", i + 1);
                    string challenge = st.Substring(i, j - i);
                    i = st.IndexOf("\"salt\":");
                    i = st.IndexOf("\"", i + "\"salt\":".Length + 1)+1;                    
                    j = st.IndexOf("\"", i + 1);
                    string salt = st.Substring(i, j - i);
                    Console.WriteLine(challenge + " - " + salt);
                    string authResponse = HashEncode(HashEncode(frm.Root.ObsPwd + salt) + challenge);
                    await SendInWs(frm.Root.ObsWs, "Authenticate", ct,",\"auth\": \"" + authResponse + "\"");
                    rcvResult = await frm.Root.ObsWs.ReceiveAsync(rcvBuffer, ct);
                    st = Encoding.UTF8.GetString(rcvBuffer.Array, 0, rcvResult.Count);
                    if(!st.Contains("\"ok\""))
                    {
                        await frm.Root.ObsWs.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication failed", ct);
                        frm.Root.ObsWs = null;
                        frm.Root.ObsRecvTask  = null;
                        frm.btVideo.BackgroundImage = global::gInk.Properties.Resources.VidDead;
                    }
                }

            }
            frm.Root.VideoRecordWindowInProgress = false;
            while (frm.Root.ObsWs != null && frm.Root.ObsWs.State == WebSocketState.Open && !ct.IsCancellationRequested) // && frm.Root.VideoRecInProgress == VideoRecInProgress.Recording )
            {
                rcvResult = await frm.Root.ObsWs.ReceiveAsync(rcvBuffer, ct);
                if (ct.IsCancellationRequested)
                    return;
                string st = Encoding.UTF8.GetString(rcvBuffer.Array, 0, rcvResult.Count);
                Console.WriteLine("ObsReturned " + st);
                if (st.Contains("\"RecordingStopped\""))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Stopped;
                else if (st.Contains("\"RecordingPaused\""))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Paused;
                else if (st.Contains("StreamStopping"))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Stopped;
                else if (st.Contains("StreamStarted"))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Streaming;
                else if (st.Contains("\"RecordingStarted\"") || st.Contains("\"RecordingResumed\""))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Recording;
                // cases from getInitialStatus;
                else if (st.Contains("\"recording - paused\": true") || st.Contains("\"recording-paused\": true") || st.Contains("\"isRecordingPaused\": true"))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Paused;
                else if (st.Contains("\"recording\": true") || st.Contains("\"isRecording\": true"))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Recording;
                else if (st.Contains("\"streaming\": true"))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Streaming;
                else if (st.Contains("\"recording\": false") || st.Contains("\"isRecording\": false") || st.Contains("\"streaming\": false"))
                    frm.Root.VideoRecInProgress = VideoRecInProgress.Stopped;
                frm.SetVidBgImage();
                Console.WriteLine("vidbg " + frm.Root.VideoRecInProgress.ToString());
                // for unknown reasons, button update seems unreliable : robustify repeating update after 100ms
                Thread.Sleep(100);
                frm.SetVidBgImage();
                //Console.WriteLine(frm.btVideo.BackgroundImage.ToString()+" vidbg2 " + frm.Root.UponButtonsUpdate);
            }
            frm.btVideo.BackgroundImage = global::gInk.Properties.Resources.VidDead; // the recv task is dead so we put the cross;
            Console.WriteLine("endoft");
        }

        static async Task ObsStartRecording(FormCollection frm)
        {
            Console.WriteLine("StartRec");
            while ((frm.Root.ObsWs==null || frm.Root.VideoRecordWindowInProgress) && !frm.Root.ObsCancel.Token.IsCancellationRequested)// frm.Root.ObsWs.State != WebSocketState.Open)
                await Task.Delay(50);
            if(frm.Root.VideoRecordMode== VideoRecordMode.OBSRec)
                await Task.Run(() => SendInWs(frm.Root.ObsWs,"StartRecording", frm.Root.ObsCancel.Token));
            else if (frm.Root.VideoRecordMode == VideoRecordMode.OBSBcst)
                await Task.Run(() => SendInWs(frm.Root.ObsWs, "StartStreaming", frm.Root.ObsCancel.Token));
            Console.WriteLine("ExitStartRec");
        }

        public void VideoRecordStop()
        {
            if(Root.VideoRecordMode == VideoRecordMode.FfmpegRec)
            {
                Root.FFmpegProcess.Kill();
                Root.VideoRecInProgress = VideoRecInProgress.Stopped;
                btVideo.BackgroundImage = global::gInk.Properties.Resources.VidStop;
                Root.UponButtonsUpdate |= 0x2;
            }
            else
            {
                if (Root.ObsRecvTask == null || Root.ObsRecvTask.IsCompleted)
                {
                    Root.ObsRecvTask = Task.Run(() => ReceiveObsMesgs(this));
                }
                Task.Run(() => ObsStopRecording(this));
            }
        }

        static async Task ObsStopRecording(FormCollection frm)
        {
            while ((frm.Root.ObsWs == null || frm.Root.VideoRecordWindowInProgress) && !frm.Root.ObsCancel.Token.IsCancellationRequested)// frm.Root.ObsWs.State != WebSocketState.Open)
                await Task.Delay(50);
            if (frm.Root.VideoRecordMode == VideoRecordMode.OBSRec)
                await Task.Run(() => SendInWs(frm.Root.ObsWs, "StopRecording", frm.Root.ObsCancel.Token));
            else if (frm.Root.VideoRecordMode == VideoRecordMode.OBSBcst)
                await Task.Run(() => SendInWs(frm.Root.ObsWs, "StopStreaming", frm.Root.ObsCancel.Token));
        }

        public void VideoRecordPause()
        {
            if (Root.VideoRecordMode == VideoRecordMode.FfmpegRec)
            {
                Root.FFmpegProcess.Kill();
                btVideo.BackgroundImage = global::gInk.Properties.Resources.VidStop;
                Root.UponButtonsUpdate |= 0x2;
            }
            else if (Root.VideoRecordMode == VideoRecordMode.OBSRec)
                Task.Run(() => SendInWs(Root.ObsWs,"PauseRecording", Root.ObsCancel.Token));
            else if (Root.VideoRecordMode == VideoRecordMode.OBSRec)
                Task.Run(() => ObsStopRecording(this));
        }

        public void VideoRecordResume()
        {
            Task.Run(() => SendInWs(Root.ObsWs,"ResumeRecording", Root.ObsCancel.Token));
        }

        static async Task SendInWs(ClientWebSocket ws, string cmd, CancellationToken ct, string parameters="")
        {
            Console.WriteLine("enter " + cmd);
            string msg = string.Format("{{\"message-id\":\"{0}\",\"request-type\":\"{1}\" {2} }}", (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds), cmd, parameters);
            byte[] sendBytes = Encoding.UTF8.GetBytes(msg);
            var sendBuffer = new ArraySegment<byte>(sendBytes);
            while ((ws.State != WebSocketState.Open ) && !ct.IsCancellationRequested)// frm.Root.ObsWs.State != WebSocketState.Open)
                await Task.Delay(50);
            await ws.SendAsync(sendBuffer, WebSocketMessageType.Text, true, ct);
            Console.WriteLine("exit " + cmd);
        }

        private void btClear_RightToLeftChanged(object sender, EventArgs e)
        {
            /* work in progress
            if((sender as Button).RightToLeft == RightToLeft.No)
                (sender as Button).BackgroundImage = global::gInk.Properties.Resources.blackboard;
            else 
                (sender as Button).BackgroundImage = global::gInk.Properties.Resources.garbage;
            */
            btClear.BackgroundImage = global::gInk.Properties.Resources.garbage;
            Console.WriteLine("R2L " + (sender as Button).Name + " . " + (sender as Button).RightToLeft.ToString());
            Root.UponButtonsUpdate |= 0x2;
        }

        public void SetTagNumber()
        {
            tiSlide.Stop();
            IC.Enabled = false;
            ToThrough();
            int k = -1;
            FormInput inp = new FormInput(Root.Local.DlgTagCaption, Root.Local.DlgTagLabel, "", false, Root, null);

            while (!Int32.TryParse(inp.TextOut(), out k))
            {
                inp.TextIn(Root.TagNumbering.ToString());
                if (inp.ShowDialog() == DialogResult.Cancel)
                {
                    inp.TextIn("");
                    break;
                }
            }
            tiSlide.Start();
            IC.Enabled = true;
            ToUnThrough();
            if (inp.TextOut().Length == 0) return;
            Root.TagNumbering = k;
        }

        private void FontBtn_Modify()
        {
            tiSlide.Stop();
            IC.Enabled = false;
            FontDlg.Font = new Font(TextFont, (float)TextSize, (TextItalic ? FontStyle.Italic : FontStyle.Regular) | (TextBold ? FontStyle.Bold : FontStyle.Regular));
            if (FontDlg.ShowDialog() == DialogResult.OK)
            {
                TextFont = FontDlg.Font.Name;
                TextItalic = (FontDlg.Font.Style & FontStyle.Italic) != 0;
                TextBold = (FontDlg.Font.Style & FontStyle.Bold) != 0;
                TextSize = (int)FontDlg.Font.Size;
            }
            IC.Enabled = true;
            tiSlide.Start();
        }


        public void btTool_Click(object sender, EventArgs e)
        {
            btClear.RightToLeft = RightToLeft.No;
            longClickTimer.Stop(); // for an unkown reason the mouse arrives later
            if (sender is ContextMenu)
            {
                sender = (sender as ContextMenu).SourceControl;
                MouseTimeDown = DateTime.FromBinary(0);
            }
            if (ToolbarMoved)
            {
                ToolbarMoved = false;
                return;
            }

            TimeSpan tsp = DateTime.Now - MouseTimeDown;

            int i = -1;
            if (((Button)sender).Name.Contains("Hand"))
                i = 0;
            else if (((Button)sender).Name.Contains("Line"))
                i = 1;
            else if (((Button)sender).Name.Contains("Rect"))
                i = 2;
            else if (((Button)sender).Name.Contains("Oval"))
                i = 3;
            else if (((Button)sender).Name.Contains("Arrow"))
                if (Root.ToolSelected == 5)
                    i = 4;
                else if (Root.ToolSelected == 4)
                    i = 5;
                else if (Root.DefaultArrow_start)
                    i = 4;
                else
                    i = 5;
                //               i = (Root.DefaultArrow_start ||Root.ToolSelected==5) ?4:5 ;
            else if (((Button)sender).Name.Contains("Numb"))
            {
                /*if (Root.ToolSelected == 6) // if already selected, we open the index dialog
                {
                    SetTagNumber();
                }*/
                i = 6;
            }
            else if (((Button)sender).Name.Contains("Text"))
                i = 8;
            else if (((Button)sender).Name.Contains("Edit"))
                if (sender != null && tsp.TotalSeconds > Root.LongClickTime)
                {
                    FontBtn_Modify();
                    return ;
                }
                else
                    i = 7;
            if(i>=0)
                SelectPen(LastPenSelected);
            SelectTool(i);
        }

        public void btEraser_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}

			SelectPen(-1);
		}


		private void btPan_Click(object sender, EventArgs e)
		{
			if (ToolbarMoved)
			{
				ToolbarMoved = false;
				return;
			}
            if (Root.ToolSelected != 10)
            {
                SelectPen(LastPenSelected);
                SelectTool(10);
            }
            else
			    SelectPen(-3);
		}

        private void btMagn_Click(object sender, EventArgs e)
        {
            if (ToolbarMoved)
            {
                ToolbarMoved = false;
                return;
            }
            Root.MagneticRadius *= -1; //invert
            if (Root.MagneticRadius > 0)
                btMagn.BackgroundImage = global::gInk.Properties.Resources.Magnetic_act;
            else
                btMagn.BackgroundImage = global::gInk.Properties.Resources.Magnetic;
            Root.UponButtonsUpdate |= 0x2;
        }

		short LastF4Status = 0;
		private void FormCollection_FormClosing(object sender, FormClosingEventArgs e)
		{
			// check if F4 key is pressed and we assume it's Alt+F4
			short retVal = GetKeyState(0x73);
			if ((retVal & 0x8000) == 0x8000 && (LastF4Status & 0x8000) == 0x0000)
			{
				e.Cancel = true;

				// the following block is copyed from tiSlide_Tick() where we check whether ESC is pressed
				if (Root.Snapping > 0)
				{
					ExitSnapping();
                Root.VideoRecordWindowInProgress = false;
				}
				else if (Root.gpPenWidthVisible)
				{
					Root.gpPenWidthVisible = false;
					Root.UponSubPanelUpdate = true;
				}
				else if (Root.Snapping == 0)
					RetreatAndExit();
			}

			LastF4Status = retVal;
		}


        [DllImport("user32.dll")]
		static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
		[DllImport("user32.dll", SetLastError = true)]
		static extern UInt32 GetWindowLong(IntPtr hWnd, int nIndex);
		[DllImport("user32.dll")]
		static extern int SetWindowLong(IntPtr hWnd, int nIndex, UInt32 dwNewLong);
		[DllImport("user32.dll")]
		public extern static bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
		[DllImport("user32.dll", SetLastError = false)]
		static extern IntPtr GetDesktopWindow();
		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		private static extern short GetKeyState(int keyCode);

		[DllImport("gdi32.dll")]
		static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
		[DllImport("user32.dll")]
		static extern IntPtr GetDC(IntPtr hWnd);
		[DllImport("user32.dll")]
		static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(int hWnd, int nCmdShow);
    }
}

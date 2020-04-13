using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace SuperFast_Blit
{
    public unsafe partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        Form TargetForm;
        IntPtr TargetDC;

        IntPtr ptrClearIMG;
        IntPtr ptrWorkIMG;

        int* iptrWork;
        int IntegerColorValue;

        int targetWidth = 1920;
        int targetHeight = 1080;

        bool RTEnabled = false;
        bool BGLoaded = false;

        object safetyLock = new object();
        Color selectedBGColor = new Color();

        bool textOverrideR = false;
        bool textOverrideT = false;

        DisplayObject[] ObjectArray = new DisplayObject[0];
        RenderThread RT;

        int FramesRendered = 0;
        bool GravityEnabled = false;

        BITMAPINFO BINFO;
        Random RND = new Random();

        #region PINVOKE

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern IntPtr memcpy(IntPtr dest, IntPtr src, UIntPtr count);
        
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern int SetDIBitsToDevice(IntPtr hdc, int XDest, int YDest, uint
           dwWidth, uint dwHeight, int XSrc, int YSrc, uint uStartScan, uint cScanLines,
           IntPtr lpvBits, [In] ref BITMAPINFO lpbmi, uint fuColorUse);

        [DllImport("msvcrt.dll", EntryPoint = "memset", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern IntPtr MemSet(IntPtr dest, int c, int byteCount);

        #endregion

        private unsafe void buttonSetBg_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Bitmap bmp = new Bitmap(openFileDialog1.FileName);
                    ushort bpp = (ushort)Image.GetPixelFormatSize(bmp.PixelFormat);

                    if (bpp != 32)
                    {
                        MessageBox.Show("Your image will be converted to 32bbp ARGB as SetDIBitsToDevice() can reach 1ms speeds with 32bpp images.", 
                            "Source Image Is " + bpp + "bpp", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    int width = bmp.Width;
                    int height = bmp.Height;

                    int tw = width;
                    int th = height;

                    BitmapData resultData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bmp.PixelFormat);

                    int mb = bpp / 8;

                    if (width > targetWidth)
                        tw = targetWidth;
                    if (height > targetHeight)
                        th = targetHeight;

                    lock (safetyLock)
                    {
                        byte* bptr = (byte*)ptrClearIMG;
                        byte* sptr = (byte*)resultData.Scan0;

                        for (int i = 0; i < th; i++)
                            for (int w = 0; w < tw; w++)
                            {
                                bptr[w * 4 + i * targetWidth * 4] = sptr[w * mb + (height - i - 1) * width * mb];
                                bptr[w * 4 + i * targetWidth * 4 + 1] = sptr[w * mb + (height - i - 1) * width * mb + 1];
                                bptr[w * 4 + i * targetWidth * 4 + 2] = sptr[w * mb + (height - i - 1) * width * mb + 2];

                                if (mb == 4)
                                    bptr[w * 4 + i * targetWidth * 4 + 3] = sptr[w * mb + (height - i - 1) * width * mb + 3];
                            }
                    }
                    bmp.UnlockBits(resultData);
                    bmp.Dispose();
                    BGLoaded = true;
                }
                catch
                {
                    MessageBox.Show("Failed To Load Image!", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void buttonUpdate_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog() == DialogResult.OK)
            {
                SetBackgroundColor(ptrClearIMG, colorDialog1.Color.A, colorDialog1.Color.R, colorDialog1.Color.G, colorDialog1.Color.B);
                selectedBGColor = colorDialog1.Color;
                BGLoaded = false;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            TargetForm = new Form();
            TargetForm.Show();
            TargetForm.MinimumSize = new Size(300, 300);
            
            this.TopMost = true;
            selectedBGColor = Color.FromArgb(255, 255, 255, 255);

            targetWidth = TargetForm.ClientSize.Width;
            targetHeight = TargetForm.ClientSize.Height;

            TargetForm.SizeChanged += TargetForm_SizeChanged;
            TargetForm.FormClosing += TargetForm_FormClosing;
            

            lock (safetyLock) 
            {
                ptrClearIMG = Marshal.AllocHGlobal(targetWidth * targetHeight * 4);
                ptrWorkIMG = Marshal.AllocHGlobal(targetWidth * targetHeight * 4);
            }

            SetBackgroundColor(ptrClearIMG, 255, 255, 255, 255);

            RT = new RenderThread(144);
            RT.RenderFrame += RT_RenderFrame;
            
            //Get DC From HWND
            TargetDC = GetDC(TargetForm.Handle);

            BINFO = new BITMAPINFO();
            BINFO.bmiHeader.biBitCount = 32; //BITS PER PIXEL
            BINFO.bmiHeader.biWidth = targetWidth;
            BINFO.bmiHeader.biHeight = targetHeight;
            BINFO.bmiHeader.biPlanes = 1;
            unsafe {
                BINFO.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            }

            FrameRateDisplay.Start();
        }

        void TargetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.Close();
        }

        float PhysicsMultiplier = 6.94444f;
        Stopwatch physicsLock = new Stopwatch();
        unsafe void RT_RenderFrame()
        {
            //Stopwatch ensures physics are independent of framerate
            physicsLock.Stop();
            PhysicsMultiplier = (float)physicsLock.Elapsed.TotalMilliseconds;
            physicsLock.Reset();
            physicsLock.Start();

            //Thread Lock, To Prevent Updating The Resolution / Physics While The Program Renders The Next Frame
            lock (safetyLock){
                //1. Clear The Working Byte Array With The BG Image or a high speed parallel bitwise operation
                if (!BGLoaded)
                    QuickBGClear(selectedBGColor.R, selectedBGColor.G, selectedBGColor.B);
                else memcpy((IntPtr)ptrWorkIMG, ptrClearIMG, (UIntPtr)(targetWidth * targetHeight * 4));


                //2. Calculate The Physics and Draw The Object
                Parallel.For(0, ObjectArray.Length, i =>{
                    //  for (int i = 0; i < ObjectArray.Length; i++)
                    if (GravityEnabled)
                        ObjectArray[i].velocityY -= 9.81f * (PhysicsMultiplier * 0.144f) / 144f;

                    ObjectArray[i].positionX += ObjectArray[i].velocityX * (PhysicsMultiplier * 0.144f);
                    ObjectArray[i].positionY += ObjectArray[i].velocityY * (PhysicsMultiplier * 0.144f);

                    if ((ObjectArray[i].positionY + 128) >= targetHeight)
                    {
                        ObjectArray[i].velocityY = -ObjectArray[i].velocityY;
                        ObjectArray[i].positionY = targetHeight - 129;
                    }

                    if ((ObjectArray[i].positionY - 128) <= 0)
                    {
                        ObjectArray[i].velocityY = -ObjectArray[i].velocityY;
                        ObjectArray[i].positionY = 129;
                    }

                    if ((ObjectArray[i].positionX - 128) <= 0)
                    {
                        ObjectArray[i].velocityX = -ObjectArray[i].velocityX;
                        ObjectArray[i].positionX = 129;
                    }

                    if ((ObjectArray[i].positionX + 128) >= targetWidth)
                    {
                        ObjectArray[i].velocityX = -ObjectArray[i].velocityX;
                        ObjectArray[i].positionX = targetWidth - 129;
                    }

                  
                    int* iptr = (int*)ptrWorkIMG;
                    int c = ((((((byte)0 << 8) | ObjectArray[i].color.R) << 8) | ObjectArray[i].color.G) << 8) | ObjectArray[i].color.B; //ARGB Values

                    for (int w = -128; w < 128; w++)
                    {
                        for (int h = -128; h < 128; h++)
                        {
                            iptr[(w + (int)ObjectArray[i].positionX) + targetWidth * (h + (int)ObjectArray[i].positionY)] = c;
                        }
                    }
                });

                //3. Blit The ARGB Array To The Screen, Ensure its 32bpp format otherwise it will be very slow
                SetDIBitsToDevice(TargetDC, 0, 0, (uint)targetWidth, (uint)targetHeight, 0, 0, 0, (uint)targetHeight, ptrWorkIMG, ref BINFO, 0);
            }


           // this.Invoke((Action)delegate() { this.Text = PhysicsMultiplier + "delta ms"; });

            FramesRendered++;
        }

        unsafe void FastBackgroundClear(IntPtr TargetData, IntPtr SourceData, int RW, int RH, int Stride)
        {
            Parallel.For(0, RH, i =>
            {
                memcpy(IntPtr.Add(TargetData, i * RW * Stride), IntPtr.Add(SourceData, i * RW * Stride), (UIntPtr)(RW * Stride));
              //  MemSet(IntPtr.Add(TargetData, i * RW * Stride), (int)((float)(255f / RH) * i), (RW * Stride));
            });
        }

        unsafe void QuickBGClear(byte R, byte G, byte B)
        {
            IntegerColorValue = ((((((byte)0 << 8) | (byte)R) << 8) | (byte)G) << 8) | (byte)B; //ARGB Values
            iptrWork = (int*)ptrWorkIMG;
            Parallel.For(0, targetHeight, internalClearFunction);
        }

        unsafe void internalClearFunction(int index)
        {
            for (int i = 0; i < targetWidth; ++i)
            {
                iptrWork[index * targetWidth + i] = IntegerColorValue;
            }
        }

        unsafe void UpdateResolution(int w, int h)
        {
            lock (safetyLock)
            {
                int tw = targetWidth;
                int th = targetHeight;

                IntPtr tempPtr = Marshal.AllocHGlobal(w * h * 4);

                if (w < targetWidth)
                    tw = w;
                if (h < targetHeight)
                    th = h;

                byte* bptr = (byte*)tempPtr;
                byte* sptr = (byte*)ptrClearIMG;
                for (int i = 0; i < th; i++)
                    for (int wd = 0; wd < tw; wd++)
                    {
                        bptr[wd * 4 + i * w * 4] = sptr[wd * 4 + i * targetWidth * 4];
                        bptr[wd * 4 + i * w * 4 + 1] = sptr[wd * 4 + i * targetWidth * 4 + 1];
                        bptr[wd * 4 + i * w * 4 + 2] = sptr[wd * 4 + i * targetWidth * 4 + 2];
                        bptr[wd * 4 + i * w * 4 + 3] = sptr[wd * 4 + i * targetWidth * 4 + 3];
                    }


                targetWidth = w;
                targetHeight = h;

                Marshal.FreeHGlobal(ptrWorkIMG);
                ptrWorkIMG = Marshal.AllocHGlobal(targetWidth * targetHeight * 4);
                
                Marshal.FreeHGlobal(ptrClearIMG);
                ptrClearIMG = Marshal.AllocHGlobal(targetWidth * targetHeight * 4);
                
                memcpy(ptrClearIMG, tempPtr, (UIntPtr)(w * h * 4));
                Marshal.FreeHGlobal(tempPtr);

                BINFO.bmiHeader.biWidth = targetWidth;
                BINFO.bmiHeader.biHeight = targetHeight;
                unsafe
                {
                    BINFO.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
                }
            }

            if (!BGLoaded)
            {
                SetBackgroundColor(ptrClearIMG, selectedBGColor.A, selectedBGColor.R, selectedBGColor.G, selectedBGColor.B);
            }
        }

        unsafe void SetBackgroundColor(IntPtr ptr, byte a, byte r, byte g, byte b)
        {
            lock (safetyLock)
            {
                byte* bptr = (byte*)ptr;
                Parallel.For(0, targetWidth * targetHeight, i =>
                {
                    bptr[i * 4 + 0] = b; //B
                    bptr[i * 4 + 1] = g; //G
                    bptr[i * 4 + 2] = r; //R
                    bptr[i * 4 + 3] = a; //A
                }); 
            }
            
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            RT.Stop();
            lock (safetyLock)
            {
                Marshal.FreeHGlobal(ptrClearIMG);
                Marshal.FreeHGlobal(ptrWorkIMG);
            }

            if (!TargetForm.IsDisposed)
            {
                ReleaseDC(TargetForm.Handle, TargetDC); //Prevent Memory Leaks
            }
        }

        private void buttonExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void buttonRender_Click(object sender, EventArgs e)
        {
            if (RTEnabled)
            {
                buttonRender.Text = "Start Rendering";
                RT.Stop();
                RTEnabled = false;
            }
            else
            {
                buttonRender.Text = "Stop Rendering";
                RT.Start();
                RTEnabled = true;
            }
        }

        private void textBoxFrameRate_TextChanged(object sender, EventArgs e)
        {
            textOverrideR = true;
            int tf;
            if (int.TryParse(textBoxFrameRate.Text, out tf) & !textOverrideT)
            {
                textBoxFrameTime.Text = (1000f / (float)tf).ToString();
                RT.SetTickRate((1000f / (float)tf));
            }

            textOverrideR = false;
        }

        private void textBoxFrameTime_TextChanged(object sender, EventArgs e)
        {
            textOverrideT = true;
            float tf;
            if (float.TryParse(textBoxFrameTime.Text, out tf) & !textOverrideR)
            {
                textBoxFrameRate.Text = ((int)(1000f / (float)tf)).ToString();
                RT.SetTickRate(tf);
            }

            textOverrideT = false;
        }

        private void buttonAdd_Click(object sender, EventArgs e)
        {
            lock (safetyLock)
            {
                listBox1.Items.Clear();
                Color col = Color.FromArgb(255, 255, 255, 255);
                List<DisplayObject> tList = ObjectArray.ToList();
                tList.Add(new DisplayObject(targetWidth / 2, targetHeight / 2, 0, 0, col));
                ObjectArray = tList.ToArray();

                for (int i = 0; i < ObjectArray.Length; i++)
                {
                    listBox1.Items.Add(i + ": R: " + col.R + ", G: " + col.G + ", B:" + col.B);
                }
            }
            
        }

        private void FrameRateDisplay_Tick(object sender, EventArgs e)
        {
            TargetForm.Text = "FPS: " + FramesRendered;
            FramesRendered = 0;
        }

        void TargetForm_SizeChanged(object sender, EventArgs e)
        {
            labelResolution.Text = TargetForm.ClientSize.Width + "x" + TargetForm.ClientSize.Height;
            UpdateResolution(TargetForm.ClientSize.Width, TargetForm.ClientSize.Height);
        }

        private void buttonRandom_Click(object sender, EventArgs e)
        {
            lock (safetyLock)
            {
                if (listBox1.SelectedIndex != -1)
                {
                    ObjectArray[listBox1.SelectedIndex].velocityX = (float)RND.Next(-100, 100) / 10f;
                    ObjectArray[listBox1.SelectedIndex].velocityY = (float)RND.Next(-100, 100) / 10f;
                }
            }
        }

        private void checkBoxGravity_CheckedChanged(object sender, EventArgs e)
        {
            GravityEnabled = checkBoxGravity.Checked;
        }

        private void buttonRemove_Click(object sender, EventArgs e)
        {
            if (listBox1.SelectedIndex != -1)
            {
                lock (safetyLock)
                {
                    List<DisplayObject> tList = ObjectArray.ToList();
                    tList.RemoveAt(listBox1.SelectedIndex);
                    ObjectArray = tList.ToArray();

                    listBox1.Items.Clear();
                    for (int i = 0; i < ObjectArray.Length; i++)
                    {
                        listBox1.Items.Add(i + ": " + ObjectArray[i].GetColorInformation());
                    }
                }
            }
        }

        private void buttonColor_Click(object sender, EventArgs e)
        {
            if (listBox1.SelectedIndex != -1)
            {
                if (colorDialog1.ShowDialog() == DialogResult.OK)
                {
                    lock (safetyLock)
                    {
                        ObjectArray[listBox1.SelectedIndex].color = colorDialog1.Color;

                        listBox1.Items.Clear();
                        for (int i = 0; i < ObjectArray.Length; i++)
                        {
                            listBox1.Items.Add(i + ": " + ObjectArray[i].GetColorInformation());
                        }
                    }
                }

            }
        }

    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public BitmapCompressionMode biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RGBQUAD
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public RGBQUAD bmiColors;
    }

    public enum BitmapCompressionMode : uint
    {
        BI_RGB = 0,
        BI_RLE8 = 1,
        BI_RLE4 = 2,
        BI_BITFIELDS = 3,
        BI_JPEG = 4,
        BI_PNG = 5
    }

    public class RenderThread
    {
        Thread T;
        bool DontStop = true;
        double TickRate;
        double NextTimeToFire = 0;

        public RenderThread(float TargetFrameTime)
        {
            TickRate = TargetFrameTime;
        }

        public RenderThread(int TargetFrameRate)
        {
            TickRate = 1000f / (float)TargetFrameRate;
        }

        public delegate void TimerFire();
        public event TimerFire RenderFrame;

        public void SetTickRate(float TickRateInMs)
        {
        //    if (TickRate == float.NaN | TickRate == float.NegativeInfinity | TickRate == float.PositiveInfinity | TickRate == float.MaxValue | TickRate == float.MinValue)
        //    {return;}
            
            TickRate = TickRateInMs;
            NextTimeToFire = 0;
        }

        public void Start()
        {
            DontStop = true;
            T = new Thread(RenderCode);
            T.Start();
        }

        public void Abort()
        {
            T.Abort();
        }

        public void Stop()
        {
            DontStop = false;
        }

        void RenderCode()
        {
            Stopwatch sw = new Stopwatch();
            
            sw.Start();
            while (DontStop)
            {
                if (sw.Elapsed.TotalMilliseconds >= NextTimeToFire)
                {
                    NextTimeToFire = sw.Elapsed.TotalMilliseconds + TickRate;
                    RenderFrame();
                }
            }
        }
    }

    public struct DisplayObject
    {
        public float velocityX;
        public float velocityY;
        public float positionX;
        public float positionY;
        public Color color;

        public DisplayObject(float pX, float pY, float vX, float vY, Color col)
        {
            velocityX = vX;
            velocityY = vY;
            positionX = pX;
            positionY = pY;
            color = col;
        }

        public string GetColorInformation()
        {
            return "R: " + color.R + ", G: " + color.G + ", B:" + color.B;
        }
    }
}

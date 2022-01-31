using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using AForge.Imaging;
using AForge.Imaging.Filters;
using System.Runtime.InteropServices;
using System.IO;

namespace capture
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            videoCaptureDevice = new VideoCaptureDevice(camCollection[0].MonikerString);
            videoCaptureDevice.NewFrame += VideoCaptureDevice_NewFrame;
            videoCaptureDevice.Start();
            mat = null;
            grids = new SortedDictionary<long, bool[,]>();
        }
        private Bitmap Calc(Bitmap bm, List<int>xC, List<int>yC, int cx, int cy, out Bitmap partial) {
            List<AForge.IntPoint> corners = new List<AForge.IntPoint>();
            corners.Add(new AForge.IntPoint(xC[1], yC[1]));
            corners.Add(new AForge.IntPoint(xC[3], yC[3]));
            corners.Add(new AForge.IntPoint(xC[2], yC[2]));
            corners.Add(new AForge.IntPoint(xC[0], yC[0]));
            const int sz = 31;
            AForge.Imaging.Filters.QuadrilateralTransformation filter = new AForge.Imaging.Filters.QuadrilateralTransformation(corners, (cx + 1) * sz, (cy + 1) * sz);
            partial = filter.Apply(bm);

            Rectangle cropRect = new Rectangle(sz / 2+1, sz / 2+1, cx * sz, cy * sz);
            Bitmap target = new Bitmap(cx * 5, cy * 5);

            using (Graphics g = Graphics.FromImage(target))
            {
                g.DrawImage(partial, new Rectangle(0, 0, target.Width, target.Height), cropRect, GraphicsUnit.Pixel);
            }
            return target;
        }
        float ColDistance(Color a, Color b) {
            return Math.Abs(a.GetHue() - b.GetHue());
        }
        Color Closest(double x)
        {
            if (x < 90)
                return Color.Black;
            else
                return Color.White;
        }
        private Bitmap Simplify(Bitmap bm)
        {
            Bitmap ret = new Bitmap(bm.Width/5, bm.Height/5);
            for (int i = 0; i < bm.Width/5; i++) {
                for (int j = 0; j < bm.Height/5; j++) {
                    double sum = 0;
                    double sumcnt = 0;
                    for (int a = 1; a <= 3; a++)
                        for (int b = 1; b <= 3; b++)
                        {
                            Color c = bm.GetPixel(i*5+a, j*5+b);
                            int cnt = 3 - Math.Abs(a - 2) - Math.Abs(b - 2);
                            cnt = cnt * cnt + 2 * cnt;
                            sum += ((0.2125 * c.R) + (0.7154 * c.G) + (0.0721 * c.B)) * cnt;
                            sumcnt += cnt;
                        }
                    sum /= sumcnt;
                    ret.SetPixel(i, j, Closest(sum));
                }
            }
            return ret;
        }

        private static Bitmap ResizeBitmap(Bitmap b, int w, int h)
        {
            Bitmap ret = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(ret))
            {
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.DrawImage(b, 0, 0, w, h);
            }
            return ret;
        }
        private bool[,] Convert(Bitmap x) {
            bool[,] ret = new bool[x.Width, x.Height];
            for (int i = 0; i < x.Width; i++) {
                for (int j = 0; j < x.Height; j++) {
                    Color c = x.GetPixel(i, j);
                    if (c.R + c.G + c.B <= 128)
                        ret[i, x.Height - j - 1] = true;
                    else
                        ret[i, x.Height - j - 1] = false;
                }
            }
            return ret;
        }
        bool[,] mat = null;
        bool same(bool[,] a, bool[,] b) {
            if (a == null || b == null)
                return false;
            for (int i = 0; i < a.GetLength(0); i++)
                for (int j = 0; j < a.GetLength(1); j++)
                    if (a[i, j] != b[i, j])
                        return false;
            return true;
        }
        SortedDictionary<long, bool[,]> grids = new SortedDictionary<long, bool[,]>();
        private void PrintMat(bool[,] a)
        {
            for (int i = 0; i < a.GetLength(0); i++)
            {

                for (int j = 0; j < a.GetLength(1); j++)
                    if (a[i, j])
                    {
                        Console.Write('1');
                    }
                    else
                    {
                        Console.Write('0');
                    }
                Console.WriteLine();
            }
            Console.WriteLine();
        }
        private void AddMat(bool[,] a)
        {
            long ms = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            grids.Add(ms, a);
            Console.WriteLine(ms);
        }

        private void CalcR(Bitmap bm, ref List<int> xR, ref List<int> yR)
        {
            BitmapData srcData = bm.LockBits(
            new Rectangle(0, 0, bm.Width, bm.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

            int stride = srcData.Stride;

            IntPtr Scan0 = srcData.Scan0;


            int width = bm.Width;
            int height = bm.Height;
            unsafe
            {
                byte* p = (byte*)(void*)Scan0;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int id = (y * stride) + x * 4;
                        if (p[id + 2] - p[id + 1] - p[id + 0] > 50)
                        {
                            xR.Add(x);
                            yR.Add(y);
                        }
                    }
                }
            }
            bm.UnlockBits(srcData);
        }
        private void CalcCorners(int w, int h, ref List<int> xR, ref List<int> yR, ref List<int> xC, ref List<int> yC) {
            foreach (bool xs in new[] { false, true }) 
            {
                foreach (bool ys in new[] { false, true })
                {
                    int cnt = 0;
                    long xS = 0;
                    long yS = 0;
                    for (int i = 0; i < xR.Count; i++) 
                    {
                        if ((xR[i] > w / 2) == xs)
                        {
                            if ((yR[i] < h / 2) == ys)
                            {
                                cnt++;
                                xS += xR[i];
                                yS += yR[i];
                            }
                        }
                    }
                    if (cnt >= 25)
                    {
                        xS /= cnt;
                        yS /= cnt;
                        xC.Add((int)xS);
                        yC.Add((int)yS);
                    }
                }
            }
        }
        bool skip = true;
        private void VideoCaptureDevice_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (skip)
            {
                skip = false;
                return;
            }
            skip = true;

            Bitmap x = (Bitmap)eventArgs.Frame.Clone();
            //Console.WriteLine(x.Width);
            //Console.WriteLine(x.Height);
            Bitmap xx = (Bitmap)x.Clone();
            //int x0 = 160;
            //int x1 = 160 + 650;
            //int y0 = 35;
            //int y1 = 35 + 500;
            List<int> xR = new List<int>(), yR = new List<int>();
            CalcR(xx, ref xR, ref yR);

            List<int> xC = new List<int>(), yC = new List<int>();//corner coords
            CalcCorners(1280, 720, ref xR, ref yR, ref xC, ref yC);

            if (xC.Count == 4 && imageSet == false)
            {
                Bitmap bm2;
                Bitmap y = Calc(xx, xC, yC, (int)numericUpDown1.Value, (int)numericUpDown2.Value, out bm2);
                pictureBox4.Image = bm2;
                pictureBox2.Image = ResizeBitmap(y, 325, 250);
                Bitmap z = Simplify(y);
                pictureBox3.Image = ResizeBitmap(z, 325, 250);
                bool[,] C = Convert(z);
                if (same(C, mat) == false)
                {
                    /*
                    for (int i = 0; i < C.GetLength(0); i++)
                    {
                        for (int j = 0; j < C.GetLength(1); j++)
                        {
                            Console.Write(C[i, j]);
                        }
                        Console.WriteLine();
                    }
                    Console.WriteLine();*/
                    AddMat(C);
                    PrintMat(C);
                    mat = C;
                }
            }

            //using (Graphics g = Graphics.FromImage(x))
            //{
            //    Rectangle rect = new Rectangle(x0, y0, x1 - x0, y1 - y0);
            //    g.DrawRectangle(new Pen(Color.Red, 7), rect);
            //}
            for (int i = 0; i < xR.Count; i++)
            {
                x.SetPixel(xR[i], yR[i], Color.Blue);
            }
            for (int i = 0; i < xC.Count; i++)
            {
                for (int dx = -20; dx <= 20; dx++)
                    for (int dy = -20; dy <= 20; dy++)
                        if (xC[i] + dx >= 0 && xC[i] + dx < x.Width && yC[i] + dy >= 0 && yC[i] + dy < x.Height)
                            if (Math.Abs(dx) <= 2 || Math.Abs(dy) <= 2)
                                x.SetPixel(xC[i] + dx, yC[i] + dy, Color.Yellow);
            }
            using (x)
            {
                var bmp2 = new Bitmap(pictureBox1.Width, pictureBox1.Height);
                using (var g = Graphics.FromImage(bmp2))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.DrawImage(x, new Rectangle(Point.Empty, bmp2.Size));
                    pictureBox1.Image = bmp2;
                }
            }
            //pictureBox1.Image = x;
        }

        FilterInfoCollection camCollection;
        VideoCaptureDevice videoCaptureDevice;
        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //AllocConsole();
            camCollection = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (videoCaptureDevice.IsRunning == true)
            {
                videoCaptureDevice.Stop();
            }
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox3_Click(object sender, EventArgs e)
        {

        }
        private int Parity(bool[,] a) {
            for (int t = 0; t < 2; t++)
            {
                int blog = 0;
                for (int i = 0; i < a.GetLength(0); i++)
                {
                    for (int j = 0; j < a.GetLength(1); j++)
                    {
                        if ((i + j) % 2 == t)
                        {
                            if (a[i, j] == false)
                                blog++;
                        }
                        else
                        {
                            if (a[i, j] == true)
                                blog++;
                        }
                    }
                }
                if (blog < 22)
                    return 1 - 2 * t;
            }
            return 0;
        }
        bool[,] GetAtTime(long t, List<long> ms, List<bool[,]> mat) {
            int lo = 0;
            int hi = ms.Count - 1;
            while (lo < hi) {
                int i = (lo + hi + 1) / 2;
                if (ms[i] <= t)
                    lo = i;
                else
                    hi = i - 1;
            }
            return mat[lo];
        }
        private int Parity1(bool[,] a)
        {
            int k0 = 0;
            int k1 = 0;
            for (int t = 0; t < 2; t++)
            {
                int blog = 0;
                for (int i = 0; i < a.GetLength(0); i+= a.GetLength(0) - 1)
                {
                    for (int j = 0; j < a.GetLength(1); j++)
                    {
                        if ((i + j) % 2 == t)
                        {
                            if (a[i, j] == false)
                                blog++;
                        }
                        else
                        {
                            if (a[i, j] == true)
                                blog++;
                        }
                    }
                }
                if (t == 0)
                    k0 = blog;
                else
                    k1 = blog;
            }
            if (k0 < k1)
                return 0;
            else
                return 1;
        }
        bool imageSet = false;
        long FixTime(long t, long dt, List<long> ms, List<bool[,]> mat)
        {
            int lo = 0;
            int hi = ms.Count - 1;
            while (lo < hi)
            {
                int i = (lo + hi + 1) / 2;
                if (ms[i] <= t)
                    lo = i;
                else
                    hi = i - 1;
            }
            hi = lo;
            int x = Parity1(mat[lo]);
            while (lo > 0 && Parity1(mat[lo - 1]) == x)
                lo--;
            while (hi < ms.Count - 1 && Parity1(mat[hi + 1]) == x)
                hi++;
            if (hi == ms.Count - 1)
                return t;
            long gal = (ms[lo] + ms[hi + 1]) / 2;
            gal = Math.Max(gal, t - dt / 3);
            gal = Math.Min(gal, t + dt / 3);
            return gal;
        }
        bool[,] Rot180(bool[,] a) {
            bool[,] b = new bool[a.GetLength(0), a.GetLength(1)];
            for (int i = 0; i < a.GetLength(0); i++)
            {
                for (int j = 0; j < a.GetLength(1); j++)
                {
                    b[i, j] = a[a.GetLength(0) - i - 1, a.GetLength(1) - j - 1];
                }
            }
            return b;
        }
        private void Calculate() {
            List<long> ms = new List<long>();
            List<bool[,]> mat = new List<bool[,]>();
            List<int> par = new List<int>();
            foreach (long k in grids.Keys) {
                ms.Add(k);
                par.Add(Parity(grids[k]));
                mat.Add(grids[k]);
            }
            long mspl = -1;
            long msmn = -1;
            for (int i = 0; i < ms.Count; i++) {
                if (par[i] == 1)
                {
                    if (i + 1 < ms.Count)
                        mspl = ms[i + 1] - 1;
                    else
                        mspl = ms[i];
                }
                else if (par[i] == -1)
                {
                    if (i + 1 < ms.Count)
                        msmn = ms[i + 1] - 1;
                    else
                        msmn = ms[i];
                }
            }
            List<bool[,]> A = new List<bool[,]>();
            long t0 = (mspl + msmn) / 2;
            long dt = Math.Abs(msmn - mspl);
            if (mspl == -1 || msmn == -1) {
                Console.WriteLine("Try again!");
            }
            else
            {
                t0 += dt;
                A.Add(GetAtTime(t0, ms, mat));
                while (t0 + dt <= ms[ms.Count - 1])
                {
                    t0 += dt;
                    t0 = FixTime(t0, dt, ms, mat);
                    A.Add(GetAtTime(t0, ms, mat));
                }
            }
            if (mspl > msmn)
                for (int i = 0; i < A.Count; i++)
                    A[i] = Rot180(A[i]);
            List<bool[]> B = new List<bool[]>();
            for (int i = 0; i < A.Count; i++)
                B.Add(Flatten(A[i]));
            List<bool> C = new List<bool>();
            for (int i = 1; i < B.Count; i++)
                foreach (bool b in B[i])
                    C.Add(b);
            if (B.Count > 0)
            {
                int v = ToInt(B[0]);
                Console.WriteLine(v);
                Console.WriteLine("YOOOO!!!");
                foreach (bool[] i in B)
                {
                    foreach (bool a in i)
                    {
                        if (a)
                            Console.Write("1");
                        else
                            Console.Write("0");
                    }
                    Console.WriteLine();
                    Console.WriteLine();
                }
                while (C.Count > v)
                    C.RemoveAt(C.Count - 1);
                if (isImage)
                {
                    imageSet = true;
                    pictureBox2.Image = WriteImage(C);
                }
                else
                    WriteFile("file.txt", C);
            }
        }

        private int DotP(int x1, int y1, int x2, int y2)
        {
            return x1 * x2 + y1 * y2;
        }
        private Bitmap WriteImage(List<bool> B)
        {
            int p = 0;
            int w = 0;
            int h = 0;
            for (int t = 0; t < 15; t++)
                if (B[p++])
                    w += (1 << t);
            for (int t = 0; t < 15; t++)
                if (B[p++])
                    h += (1 << t);
            int[,,] rgb1 = new int[h, w, 3];
            for (int x = 0; x < h; x++)
                for (int y = 0; y < w; y++)
                {
                    rgb1[x, y, 0] = 128;
                    rgb1[x, y, 1] = 128;
                    rgb1[x, y, 2] = 128;
                }
            int logwh = 1;
            {
                int sz = 2;
                while (sz < w * h)
                {
                    sz *= 2;
                    logwh++;
                }
            }
            while (p + logwh * 2 + 7 * 3 < B.Count)
            {
                int r1 = 0;
                int r2 = 0;
                for (int t = 0; t < logwh; t++)
                    if (B[p++])
                        r1 += (1 << t);
                for (int t = 0; t < logwh; t++)
                    if (B[p++])
                        r2 += (1 << t);
                int x1 = r1 % h;
                int y1 = r1 / h;
                int x2 = r2 % h;
                int y2 = r2 / h;
                int x3 = ((x1 + x2) + (y2 - y1)) / 2;
                int y3 = ((y1 + y2) + (x1 - x2)) / 2;
                int x4 = ((x1 + x2) - (y2 - y1)) / 2;
                int y4 = ((y1 + y2) - (x1 - x2)) / 2;
                int xmn = Math.Min(h - 1, Math.Max(0, Math.Min(Math.Min(x1, x2), Math.Min(x3, x4))));
                int ymn = Math.Min(w - 1, Math.Max(0, Math.Min(Math.Min(y1, y2), Math.Min(y3, y4))));
                int xmx = Math.Min(h - 1, Math.Max(0, Math.Max(Math.Max(x1, x2), Math.Max(x3, x4))));
                int ymx = Math.Min(w - 1, Math.Max(0, Math.Max(Math.Max(y1, y2), Math.Max(y3, y4))));
                int r = 0, g = 0, b = 0;
                for (int t = 0; t < 7; t++)
                    if (B[p++])
                        r += (1 << t);
                for (int t = 0; t < 7; t++)
                    if (B[p++])
                        g += (1 << t);
                for (int t = 0; t < 7; t++)
                    if (B[p++])
                        b += (1 << t);
                r -= 64;
                g -= 64;
                b -= 64;
                for (int x = xmn; x <= xmx; x++)
                {
                    for (int y = ymn; y <= ymx; y++)
                    {
                        bool ok = true;
                        if (DotP(x3 - x1, y3 - y1, x - x1, y - y1) < 0)
                            ok = false;
                        if (DotP(x4 - x1, y4 - y1, x - x1, y - y1) < 0)
                            ok = false;
                        if (DotP(x3 - x2, y3 - y2, x - x2, y - y2) < 0)
                            ok = false;
                        if (DotP(x4 - x2, y4 - y2, x - x2, y - y2) < 0)
                            ok = false;
                        if (ok)
                        {
                            rgb1[x, y, 0] += r;
                            rgb1[x, y, 1] += g;
                            rgb1[x, y, 2] += b;
                            rgb1[x, y, 0] = Math.Min(rgb1[x, y, 0], 255);
                            rgb1[x, y, 1] = Math.Min(rgb1[x, y, 1], 255);
                            rgb1[x, y, 2] = Math.Min(rgb1[x, y, 2], 255);
                            rgb1[x, y, 0] = Math.Max(rgb1[x, y, 0], 0);
                            rgb1[x, y, 1] = Math.Max(rgb1[x, y, 1], 0);
                            rgb1[x, y, 2] = Math.Max(rgb1[x, y, 2], 0);
                        }
                    }
                }
            }
            Bitmap bmp = new Bitmap(w, h);
            for (int x = 0; x < h; x++)
                for (int y = 0; y < w; y++)
                {
                    int r = rgb1[x, y, 0];
                    int g = rgb1[x, y, 1];
                    int b = rgb1[x, y, 2];
                    bmp.SetPixel(y, x, Color.FromArgb(r, g, b));
                }
            return bmp;
        }
        private void WriteFile(string name, List<bool> C) {
            List<byte> CC = new List<byte>();
            for (int i = 0; i + 7 < C.Count; i += 8) {
                int a = 0;
                int x = 1;
                for (int j = 0; j < 8; j++) 
                {
                    if (C[i + j] == true)
                        a += x;
                    x *= 2;
                }
                CC.Add((byte)a);
            }
            Console.WriteLine(CC.Count);
            File.WriteAllBytes(name, CC.ToArray());
        }
        private int ToInt(bool[] a) {
            int x = 1;
            int sum = 0;
            foreach (bool b in a) {
                if (b == true) {
                    sum += x;
                }
                x *= 2;
            }
            return sum;
        }
        private bool[] Flatten(bool [,] a)
        {
            bool[] b = new bool[(a.GetLength(0)-2) * a.GetLength(1)];
            for (int i = 0; i < a.GetLength(0)-2; i++)
            {
                for (int j = 0; j < a.GetLength(1); j++)
                {
                    b[j + i * a.GetLength(1)] = a[i + 1, j];
                }
            }
            return b;
        }
        private void button2_Click(object sender, EventArgs e)
        {
            if (videoCaptureDevice.IsRunning == true)
            {
                videoCaptureDevice.Stop();
            }
            Calculate();
        }
        bool isImage = false;
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            isImage = checkBox1.Checked;
        }
    }
}

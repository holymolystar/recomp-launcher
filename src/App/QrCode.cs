// A small QR code maker, for the phone remote's address (Settings > Phone remote): byte mode, error correction level
// M, sizes 1 to 6 (up to 106 characters, plenty for "http://192.168.1.20:8770/?pin=123456"). The standard algorithm
// (ISO/IEC 18004), after Project Nayuki's QR Code generator (MIT licence), cut down to what the launcher needs.
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace RecompLauncher {

static class QrCode {
    // per size 1..6, level M: all codewords, error correction codewords per block, blocks
    static readonly int[] Total = { 0, 26, 44, 70, 100, 134, 172 };
    static readonly int[] EccPerBlock = { 0, 10, 16, 26, 18, 24, 16 };
    static readonly int[] Blocks = { 0, 1, 1, 1, 2, 2, 4 };
    static readonly int[] Align = { 0, 0, 18, 22, 26, 30, 34 };   // the second alignment position (the first is 6)

    // the modules (true = dark), or null when the text is too long
    public static bool[,] Make(string text) {
        byte[] data = Encoding.UTF8.GetBytes(text);
        int ver = 1;
        while (ver <= 6 && data.Length + 2 > Total[ver] - EccPerBlock[ver] * Blocks[ver]) ver++;
        if (ver > 6) return null;
        int capacity = Total[ver] - EccPerBlock[ver] * Blocks[ver];
        // the bits: byte mode, the length, the bytes, a terminator, then padding
        var bits = new List<bool>();
        Action<int, int> put = (v, n) => { for (int i = n - 1; i >= 0; i--) bits.Add(((v >> i) & 1) != 0); };
        put(4, 4); put(data.Length, 8);
        foreach (var b in data) put(b, 8);
        put(0, Math.Min(4, capacity * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        for (int pad = 0xEC; bits.Count < capacity * 8; pad ^= 0xEC ^ 0x11) put(pad, 8);
        var cw = new byte[capacity];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        byte[] all = Interleave(cw, ver);

        int size = ver * 4 + 17;
        var m = new bool[size, size]; var fn = new bool[size, size];   // [y, x]
        Action<int, int, bool> set = (x, y, dark) => { m[y, x] = dark; fn[y, x] = true; };
        for (int i = 0; i < size; i++) { set(6, i, i % 2 == 0); set(i, 6, i % 2 == 0); }
        foreach (var c in new[] { new[] { 3, 3 }, new[] { size - 4, 3 }, new[] { 3, size - 4 } })
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) {
                int x = c[0] + dx, y = c[1] + dy; if (x < 0 || y < 0 || x >= size || y >= size) continue;
                int d = Math.Max(Math.Abs(dx), Math.Abs(dy)); set(x, y, d != 2 && d != 4);
            }
        if (ver > 1) { int a = Align[ver]; for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) set(a + dx, a + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1); }
        Format(m, fn, size, 0, set);   // marks the format areas as taken
        // the codewords, in two-column zigzags from the bottom right
        int bit = 0;
        for (int right = size - 1; right >= 1; right -= 2) {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++) {
                    int x = right - j; bool up = ((right + 1) & 2) == 0; int y = up ? size - 1 - vert : vert;
                    if (fn[y, x] || bit >= all.Length * 8) continue;
                    m[y, x] = ((all[bit >> 3] >> (7 - (bit & 7))) & 1) != 0; bit++;
                }
        }
        // the mask that gives the fewest confusing patterns
        int best = 0; long bestScore = long.MaxValue;
        for (int mask = 0; mask < 8; mask++) {
            Mask(m, fn, size, mask); Format(m, fn, size, mask, set);
            long s = Penalty(m, size);
            if (s < bestScore) { bestScore = s; best = mask; }
            Mask(m, fn, size, mask);
        }
        Mask(m, fn, size, best); Format(m, fn, size, best, set);
        return m;
    }
    static byte[] Interleave(byte[] data, int ver) {
        int n = Blocks[ver], ecc = EccPerBlock[ver], raw = Total[ver], shortCount = n - raw % n, shortLen = raw / n;
        var gen = Generator(ecc);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < n; i++) {
            int len = shortLen - ecc + (i < shortCount ? 0 : 1);
            var dat = new byte[len]; Array.Copy(data, k, dat, 0, len); k += len;
            var e = Remainder(dat, gen);
            var block = new byte[shortLen + 1];
            Array.Copy(dat, block, len); Array.Copy(e, 0, block, block.Length - ecc, ecc);
            blocks.Add(block);
        }
        var r = new List<byte>();
        for (int i = 0; i < blocks[0].Length; i++)
            for (int j = 0; j < n; j++)
                if (i != shortLen - ecc || j >= shortCount) r.Add(blocks[j][i]);
        return r.ToArray();
    }
    static byte Mul(int x, int y) { int z = 0; for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; } return (byte)z; }
    static byte[] Generator(int degree) {
        var r = new byte[degree]; r[degree - 1] = 1; int root = 1;
        for (int i = 0; i < degree; i++) {
            for (int j = 0; j < degree; j++) { r[j] = Mul(r[j], root); if (j + 1 < degree) r[j] ^= r[j + 1]; }
            root = Mul(root, 2);
        }
        return r;
    }
    static byte[] Remainder(byte[] data, byte[] gen) {
        var r = new byte[gen.Length];
        foreach (var b in data) {
            int f = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1); r[r.Length - 1] = 0;
            for (int i = 0; i < r.Length; i++) r[i] ^= Mul(gen[i], f);
        }
        return r;
    }
    static void Format(bool[,] m, bool[,] fn, int size, int mask, Action<int, int, bool> set) {
        int data = (0 << 3) | mask;   // level M is 00
        int rem = data; for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;
        Func<int, bool> b = i => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) set(8, i, b(i));
        set(8, 7, b(6)); set(8, 8, b(7)); set(7, 8, b(8));
        for (int i = 9; i < 15; i++) set(14 - i, 8, b(i));
        for (int i = 0; i < 8; i++) set(size - 1 - i, 8, b(i));
        for (int i = 8; i < 15; i++) set(8, size - 15 + i, b(i));
        set(8, size - 8, true);
    }
    static void Mask(bool[,] m, bool[,] fn, int size, int mask) {
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
            if (fn[y, x]) continue;
            bool inv;
            switch (mask) {
                case 0: inv = (x + y) % 2 == 0; break;
                case 1: inv = y % 2 == 0; break;
                case 2: inv = x % 3 == 0; break;
                case 3: inv = (x + y) % 3 == 0; break;
                case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
            }
            if (inv) m[y, x] = !m[y, x];
        }
    }
    // runs of five or more, 2x2 blocks, and the balance of dark and light (rules 1, 2 and 4 of the standard)
    static long Penalty(bool[,] m, int size) {
        long p = 0; int dark = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++) {
                int run = 1;
                for (int b = 1; b < size; b++) {
                    bool cur = pass == 0 ? m[a, b] : m[b, a], prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; } else run = 1;
                }
            }
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
            if (m[y, x]) dark++;
            if (x < size - 1 && y < size - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) p += 3;
        }
        int total = size * size; p += (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10;
        return p;
    }
    // as a picture: black squares on white, with the quiet border of four modules around it
    public static ImageSource Picture(string text) {
        var m = Make(text); if (m == null) return null;
        int size = m.GetLength(0);
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (m[y, x]) g.Children.Add(new RectangleGeometry(new Rect(x + 4, y + 4, 1, 1)));
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, size + 8, size + 8))));
        dg.Children.Add(new GeometryDrawing(Brushes.Black, null, g));
        var img = new DrawingImage(dg); img.Freeze();
        return img;
    }
}
}

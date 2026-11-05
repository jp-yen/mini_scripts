// Md4.cs - MD4 ハッシュ実装 (NT ハッシュ用 / MS-CHAPv2)

using System;
using System.Security.Cryptography;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // 暗号処理ヘルパー: MD4, MD5, DES, CHAP, MS-CHAPv2
    // ------------------------------------------------------------------
    public static class Md4
    {
        private static uint F(uint x, uint y, uint z) => (x & y) | (~x & z);
        private static uint G(uint x, uint y, uint z) => (x & y) | (x & z) | (y & z);
        private static uint H(uint x, uint y, uint z) => x ^ y ^ z;
        private static uint ROL(uint x, int n) => (x << n) | (x >> (32 - n));

        public static byte[] Hash(byte[] input)
        {
            ArgumentNullException.ThrowIfNull(input);
            uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;
            int byteLen = input.Length;
            int padLen = (byteLen % 64 < 56) ? (56 - byteLen % 64) : (120 - byteLen % 64);
            byte[] data = new byte[byteLen + padLen + 8];
            Buffer.BlockCopy(input, 0, data, 0, byteLen);
            data[byteLen] = 0x80;
            ulong bitLen = (ulong)byteLen * 8;
            for (int i = 0; i < 8; i++) data[byteLen + padLen + i] = (byte)(bitLen >> (8 * i));

            uint[] x = new uint[16];
            for (int i = 0; i < data.Length; i += 64)
            {
                for (int j = 0; j < 16; j++)
                    x[j] = (uint)(data[i + j * 4] | (data[i + j * 4 + 1] << 8) | (data[i + j * 4 + 2] << 16) | (data[i + j * 4 + 3] << 24));

                uint aa = a, bb = b, cc = c, dd = d;

                // ラウンド 1
                a = ROL(a + F(b, c, d) + x[0], 3); d = ROL(d + F(a, b, c) + x[1], 7);
                c = ROL(c + F(d, a, b) + x[2], 11); b = ROL(b + F(c, d, a) + x[3], 19);
                a = ROL(a + F(b, c, d) + x[4], 3); d = ROL(d + F(a, b, c) + x[5], 7);
                c = ROL(c + F(d, a, b) + x[6], 11); b = ROL(b + F(c, d, a) + x[7], 19);
                a = ROL(a + F(b, c, d) + x[8], 3); d = ROL(d + F(a, b, c) + x[9], 7);
                c = ROL(c + F(d, a, b) + x[10], 11); b = ROL(b + F(c, d, a) + x[11], 19);
                a = ROL(a + F(b, c, d) + x[12], 3); d = ROL(d + F(a, b, c) + x[13], 7);
                c = ROL(c + F(d, a, b) + x[14], 11); b = ROL(b + F(c, d, a) + x[15], 19);

                // ラウンド 2
                a = ROL(a + G(b, c, d) + x[0] + 0x5a827999, 3); d = ROL(d + G(a, b, c) + x[4] + 0x5a827999, 5);
                c = ROL(c + G(d, a, b) + x[8] + 0x5a827999, 9); b = ROL(b + G(c, d, a) + x[12] + 0x5a827999, 13);
                a = ROL(a + G(b, c, d) + x[1] + 0x5a827999, 3); d = ROL(d + G(a, b, c) + x[5] + 0x5a827999, 5);
                c = ROL(c + G(d, a, b) + x[9] + 0x5a827999, 9); b = ROL(b + G(c, d, a) + x[13] + 0x5a827999, 13);
                a = ROL(a + G(b, c, d) + x[2] + 0x5a827999, 3); d = ROL(d + G(a, b, c) + x[6] + 0x5a827999, 5);
                c = ROL(c + G(d, a, b) + x[10] + 0x5a827999, 9); b = ROL(b + G(c, d, a) + x[14] + 0x5a827999, 13);
                a = ROL(a + G(b, c, d) + x[3] + 0x5a827999, 3); d = ROL(d + G(a, b, c) + x[7] + 0x5a827999, 5);
                c = ROL(c + G(d, a, b) + x[11] + 0x5a827999, 9); b = ROL(b + G(c, d, a) + x[15] + 0x5a827999, 13);

                // ラウンド 3
                a = ROL(a + H(b, c, d) + x[0] + 0x6ed9eba1, 3); d = ROL(d + H(a, b, c) + x[8] + 0x6ed9eba1, 9);
                c = ROL(c + H(d, a, b) + x[4] + 0x6ed9eba1, 11); b = ROL(b + H(c, d, a) + x[12] + 0x6ed9eba1, 15);
                a = ROL(a + H(b, c, d) + x[2] + 0x6ed9eba1, 3); d = ROL(d + H(a, b, c) + x[10] + 0x6ed9eba1, 9);
                c = ROL(c + H(d, a, b) + x[6] + 0x6ed9eba1, 11); b = ROL(b + H(c, d, a) + x[14] + 0x6ed9eba1, 15);
                a = ROL(a + H(b, c, d) + x[1] + 0x6ed9eba1, 3); d = ROL(d + H(a, b, c) + x[9] + 0x6ed9eba1, 9);
                c = ROL(c + H(d, a, b) + x[5] + 0x6ed9eba1, 11); b = ROL(b + H(c, d, a) + x[13] + 0x6ed9eba1, 15);
                a = ROL(a + H(b, c, d) + x[3] + 0x6ed9eba1, 3); d = ROL(d + H(a, b, c) + x[11] + 0x6ed9eba1, 9);
                c = ROL(c + H(d, a, b) + x[7] + 0x6ed9eba1, 11); b = ROL(b + H(c, d, a) + x[15] + 0x6ed9eba1, 15);

                a += aa; b += bb; c += cc; d += dd;
            }

            byte[] res = new byte[16];
            for (int i = 0; i < 4; i++)
            {
                res[i] = (byte)(a >> (8 * i));
                res[4 + i] = (byte)(b >> (8 * i));
                res[8 + i] = (byte)(c >> (8 * i));
                res[12 + i] = (byte)(d >> (8 * i));
            }
            return res;
        }
    }
}

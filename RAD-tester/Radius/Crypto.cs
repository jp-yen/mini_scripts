// Crypto.cs - 暗号処理ヘルパー (PAP / CHAP / MS-CHAPv2 / Authenticator 検証)

using System;
using System.Security.Cryptography;
using System.Text;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // 暗号処理ヘルパー: MD4, MD5, DES, CHAP, MS-CHAPv2
    // ------------------------------------------------------------------
    public static class Crypto
    {
        // PAP パスワード暗号化 (RFC 2865)
        public static byte[] EncryptPassword(string password, byte[] reqAuth, byte[] secret)
        {
            byte[] pw = Encoding.UTF8.GetBytes(password);
            int paddedLen = ((pw.Length + 15) / 16) * 16;
            if (paddedLen == 0) paddedLen = 16;
            byte[] padded = new byte[paddedLen];
            Buffer.BlockCopy(pw, 0, padded, 0, pw.Length);

            byte[] cipher = new byte[paddedLen];
            using var md5 = MD5.Create();
            byte[] prev = reqAuth;

            for (int i = 0; i < paddedLen; i += 16)
            {
                byte[] hashInput = new byte[secret.Length + prev.Length];
                Buffer.BlockCopy(secret, 0, hashInput, 0, secret.Length);
                Buffer.BlockCopy(prev, 0, hashInput, secret.Length, prev.Length);
                byte[] b = md5.ComputeHash(hashInput);

                for (int j = 0; j < 16; j++) cipher[i + j] = (byte)(padded[i + j] ^ b[j]);
                prev = new byte[16];
                Buffer.BlockCopy(cipher, i, prev, 0, 16);
            }
            return cipher;
        }

        // CHAP レスポンス生成 (RFC 1994 / RFC 2865)
        public static byte[] GenerateChapPassword(byte ident, string password, byte[] challenge)
        {
            byte[] pw = Encoding.UTF8.GetBytes(password);
            byte[] input = new byte[1 + pw.Length + challenge.Length];
            input[0] = ident;
            Buffer.BlockCopy(pw, 0, input, 1, pw.Length);
            Buffer.BlockCopy(challenge, 0, input, 1 + pw.Length, challenge.Length);

            byte[] hash = MD5.HashData(input);
            byte[] res = new byte[17];
            res[0] = ident;
            Buffer.BlockCopy(hash, 0, res, 1, 16);
            return res;
        }

        // MS-CHAPv2 (RFC 2759 / RFC 2548)
        private static byte[] CreateDesKey(byte[] bytes7, int offset)
        {
            byte[] key = new byte[8];
            key[0] = bytes7[offset];
            key[1] = (byte)((bytes7[offset] << 7) | (bytes7[offset + 1] >> 1));
            key[2] = (byte)((bytes7[offset + 1] << 6) | (bytes7[offset + 2] >> 2));
            key[3] = (byte)((bytes7[offset + 2] << 5) | (bytes7[offset + 3] >> 3));
            key[4] = (byte)((bytes7[offset + 3] << 4) | (bytes7[offset + 4] >> 4));
            key[5] = (byte)((bytes7[offset + 4] << 3) | (bytes7[offset + 5] >> 5));
            key[6] = (byte)((bytes7[offset + 5] << 2) | (bytes7[offset + 6] >> 6));
            key[7] = (byte)(bytes7[offset + 6] << 1);

            for (int i = 0; i < 8; i++)
            {
                byte b = key[i];
                int bitCount = 0;
                for (int k = 1; k < 8; k++) if ((b & (1 << k)) != 0) bitCount++;
                key[i] = (bitCount % 2 == 0) ? (byte)((b & 0xfe) | 1) : (byte)(b & 0xfe);
            }
            return key;
        }

        private static byte[] DesEncrypt(byte[] key8, byte[] plain8)
        {
            using var des = DES.Create();
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;
            des.Key = key8;
            return des.EncryptEcb(plain8, PaddingMode.None);
        }

        public static byte[] ChallengeResponse(byte[] challenge8, byte[] passwordHash16)
        {
            byte[] zPasswordHash = new byte[21];
            Buffer.BlockCopy(passwordHash16, 0, zPasswordHash, 0, 16);

            byte[] response = new byte[24];
            byte[] k1 = CreateDesKey(zPasswordHash, 0);
            byte[] k2 = CreateDesKey(zPasswordHash, 7);
            byte[] k3 = CreateDesKey(zPasswordHash, 14);

            Buffer.BlockCopy(DesEncrypt(k1, challenge8), 0, response, 0, 8);
            Buffer.BlockCopy(DesEncrypt(k2, challenge8), 0, response, 8, 8);
            Buffer.BlockCopy(DesEncrypt(k3, challenge8), 0, response, 16, 8);
            return response;
        }

        public static byte[] GenerateMsChap2Response(string userName, string password, byte[] authChallenge16, byte[] peerChallenge16)
        {
            byte[] passwordHash = Md4.Hash(Encoding.Unicode.GetBytes(password));

            string cleanUser = userName;
            int bs = cleanUser.IndexOf('\\');
            if (bs >= 0) cleanUser = cleanUser.Substring(bs + 1);

            byte[] userBytes = Encoding.UTF8.GetBytes(cleanUser);
            byte[] toHash = new byte[16 + 16 + userBytes.Length];
            Buffer.BlockCopy(peerChallenge16, 0, toHash, 0, 16);
            Buffer.BlockCopy(authChallenge16, 0, toHash, 16, 16);
            Buffer.BlockCopy(userBytes, 0, toHash, 32, userBytes.Length);

            byte[] fullHash = SHA1.HashData(toHash);
            byte[] challenge8 = new byte[8];
            Buffer.BlockCopy(fullHash, 0, challenge8, 0, 8);

            return ChallengeResponse(challenge8, passwordHash);
        }

        public static string GenerateAuthenticatorResponse(string password, byte[] ntResponse24, byte[] peerChallenge16, byte[] authChallenge16, string userName)
        {
            byte[] passwordHash = Md4.Hash(Encoding.Unicode.GetBytes(password));
            byte[] passwordHashHash = Md4.Hash(passwordHash);

            byte[] magic1 = Encoding.ASCII.GetBytes("Magic server to client signing constant");
            byte[] toHash1 = new byte[16 + 24 + magic1.Length];
            Buffer.BlockCopy(passwordHashHash, 0, toHash1, 0, 16);
            Buffer.BlockCopy(ntResponse24, 0, toHash1, 16, 24);
            Buffer.BlockCopy(magic1, 0, toHash1, 40, magic1.Length);
            byte[] digest1 = SHA1.HashData(toHash1);

            string cleanUser = userName;
            int bs = cleanUser.IndexOf('\\');
            if (bs >= 0) cleanUser = cleanUser.Substring(bs + 1);
            byte[] userBytes = Encoding.UTF8.GetBytes(cleanUser);
            byte[] toHash2 = new byte[16 + 16 + userBytes.Length];
            Buffer.BlockCopy(peerChallenge16, 0, toHash2, 0, 16);
            Buffer.BlockCopy(authChallenge16, 0, toHash2, 16, 16);
            Buffer.BlockCopy(userBytes, 0, toHash2, 32, userBytes.Length);
            byte[] fullHash2 = SHA1.HashData(toHash2);
            byte[] challenge8 = new byte[8];
            Buffer.BlockCopy(fullHash2, 0, challenge8, 0, 8);

            byte[] magic2 = Encoding.ASCII.GetBytes("Pad to make it do more than one iteration");
            byte[] toHash3 = new byte[20 + 8 + magic2.Length];
            Buffer.BlockCopy(digest1, 0, toHash3, 0, 20);
            Buffer.BlockCopy(challenge8, 0, toHash3, 20, 8);
            Buffer.BlockCopy(magic2, 0, toHash3, 28, magic2.Length);
            byte[] digest2 = SHA1.HashData(toHash3);

            return Printer.ToHex(digest2).ToUpperInvariant();
        }

        // Response Authenticator および Message-Authenticator の検証
        public static bool VerifyResponseAuth(byte[]? rawResponse, byte[]? reqAuth, byte[]? secret)
        {
            if (rawResponse == null || rawResponse.Length < 20 || reqAuth == null || reqAuth.Length < 16 || secret == null)
                return false;

            byte[] scratch = new byte[rawResponse.Length + secret.Length];
            Buffer.BlockCopy(rawResponse, 0, scratch, 0, rawResponse.Length);
            Buffer.BlockCopy(reqAuth, 0, scratch, 4, 16);
            Buffer.BlockCopy(secret, 0, scratch, rawResponse.Length, secret.Length);

            byte[] expected = MD5.HashData(scratch);
            return CryptographicOperations.FixedTimeEquals(
                expected.AsSpan(0, 16),
                rawResponse.AsSpan(4, 16));
        }

        public static bool VerifyMessageAuth(byte[]? rawResponse, byte[]? reqAuth, byte[]? secret)
        {
            if (rawResponse == null || rawResponse.Length < 20 || reqAuth == null || reqAuth.Length < 16 || secret == null)
                return false;

            int off = 20;
            int foundOff = -1;
            while (off + 2 <= rawResponse.Length)
            {
                byte type = rawResponse[off];
                int len = rawResponse[off + 1];
                if (len < 2 || off + len > rawResponse.Length) break;
                if (type == 80)
                {
                    // RFC 3579 / BlastRADIUS 対策: 長さが 18 バイト以外、または複数個の Type 80 が存在する場合は不正
                    if (len != 18 || foundOff >= 0) return false;
                    foundOff = off + 2;
                }
                off += len;
            }
            if (foundOff < 0) return true;

            byte[] scratch = new byte[rawResponse.Length];
            Buffer.BlockCopy(rawResponse, 0, scratch, 0, rawResponse.Length);
            Buffer.BlockCopy(reqAuth, 0, scratch, 4, 16);
            Array.Clear(scratch, foundOff, 16);

            using var hmac = new HMACMD5(secret);
            byte[] expected = hmac.ComputeHash(scratch);
            return CryptographicOperations.FixedTimeEquals(
                expected.AsSpan(0, 16),
                rawResponse.AsSpan(foundOff, 16));
        }
    }
}

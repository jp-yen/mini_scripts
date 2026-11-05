// CertHelper.cs - 証明書処理ヘルパー (PEM + PFX + 証明書ストア)

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // 証明書処理ヘルパー (.NET 10 PEM + PFX + 証明書ストア)
    // ------------------------------------------------------------------
    public static class CertHelper
    {
        public static X509Certificate2 LoadClientCertificate(Options o)
        {
            if (!string.IsNullOrEmpty(o.CertFile))
            {
                if (!File.Exists(o.CertFile)) throw new FileNotFoundException($"証明書ファイルが見つかりません: {o.CertFile}");
                string ext = Path.GetExtension(o.CertFile).ToLowerInvariant();

                if (ext == ".pem" || ext == ".crt" || ext == ".cer")
                {
                    if (string.IsNullOrEmpty(o.KeyFile))
                        throw new ArgumentException("PEM 形式の証明書を使用する場合は秘密鍵ファイル（-key <file>）が必須です。");
                    if (!File.Exists(o.KeyFile))
                        throw new FileNotFoundException($"秘密鍵ファイルが見つかりません: {o.KeyFile}");

                    if (!string.IsNullOrEmpty(o.CertPass))
                        return X509Certificate2.CreateFromEncryptedPemFile(o.CertFile, o.CertPass, o.KeyFile);
                    else
                        return X509Certificate2.CreateFromPemFile(o.CertFile, o.KeyFile);
                }

                return X509CertificateLoader.LoadPkcs12FromFile(o.CertFile, o.CertPass ?? "", X509KeyStorageFlags.DefaultKeySet);
            }

            if (!string.IsNullOrEmpty(o.Thumb) || !string.IsNullOrEmpty(o.CertSubject))
            {
                var loc = o.StoreLoc.Equals("LocalMachine", StringComparison.OrdinalIgnoreCase)
                    ? StoreLocation.LocalMachine
                    : StoreLocation.CurrentUser;
                using var st = new X509Store(o.StoreName, loc);
                st.Open(OpenFlags.ReadOnly);

                foreach (var c in st.Certificates)
                {
                    if (!c.HasPrivateKey) continue;
                    if (!string.IsNullOrEmpty(o.Thumb))
                    {
                        string clean = o.Thumb.Replace(" ", "").Replace(":", "");
                        if (c.Thumbprint.Equals(clean, StringComparison.OrdinalIgnoreCase)) return c;
                    }
                    if (!string.IsNullOrEmpty(o.CertSubject))
                    {
                        if (c.Subject.IndexOf(o.CertSubject, StringComparison.OrdinalIgnoreCase) >= 0) return c;
                    }
                }
                throw new InvalidOperationException($"証明書ストア {o.StoreName}/{loc} 内に対応する秘密鍵付き証明書が見つかりませんでした。");
            }

            throw new InvalidOperationException("クライアント証明書が指定されていません。");
        }

        public static void DescribeCertificate(X509Certificate2 cert, string phase)
        {
            Progress.Info(phase, $"クライアント証明書: サブジェクト: {cert.Subject}");
            Progress.Info(phase, $"                   発行者:       {cert.Issuer}");
            Progress.Info(phase, $"                   拇印:         {cert.Thumbprint}");
            Progress.Info(phase, $"                   有効期間:     {cert.NotBefore:yyyy-MM-dd} ～ {cert.NotAfter:yyyy-MM-dd}");
            if (DateTime.Now > cert.NotAfter) Progress.Log(phase, PState.Warn, "クライアント証明書の有効期限が切れています。");
        }

        /// <summary>
        /// RadSec / TLS テスト用の自己署名証明書および秘密鍵 (PKCS#8 PEM) を生成します。
        /// </summary>
        public static void GenerateSelfSignedCertificate(string certPath, string keyPath, string cn = "localhost")
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            san.AddIpAddress(System.Net.IPAddress.Loopback);
            san.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
            req.CertificateExtensions.Add(san.Build());

            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // Server Auth
                new Oid("1.3.6.1.5.5.7.3.2")  // Client Auth
            }, false));

            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            File.WriteAllText(certPath, cert.ExportCertificatePem());
            File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
        }
    }
}

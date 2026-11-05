// TlsContext.cs - TLS コンテキスト (SslStream + TLS 1.3 / 1.2 サポート)

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // TLS コンテキスト (SslStream + TLS 1.3 / 1.2 サポート)
    // ------------------------------------------------------------------
    public class TlsContext
    {
        public Options O;
        public X509Certificate2? ClientCert;
        public List<X509Certificate2> CaCerts = new List<X509Certificate2>();
        public string Phase;

        public TlsContext(Options o, string phase = "TLS")
        {
            O = o; Phase = phase;
        }

        public void Load()
        {
            if (O.CertFile != null || O.Thumb != null || O.CertSubject != null)
            {
                ClientCert = CertHelper.LoadClientCertificate(O);
                CertHelper.DescribeCertificate(ClientCert, "INIT");
            }
            foreach (var f in O.CaFiles)
            {
                if (!File.Exists(f)) throw new FileNotFoundException($"CA 証明書ファイルが見つかりません: {f}");
                var ca = X509CertificateLoader.LoadCertificateFromFile(f);
                CaCerts.Add(ca);
                Progress.Info("INIT", $"信頼する CA 証明書を読み込みました: {ca.Subject} (ファイル: {Path.GetFileName(f)})");
            }
        }

        public SslProtocols GetProtocols()
        {
#pragma warning disable SYSLIB0039
            return O.Tls switch
            {
                "1.3" => SslProtocols.Tls13,
                "1.2" => SslProtocols.Tls12,
                "1.1" => SslProtocols.Tls11,
                "1.0" => SslProtocols.Tls,
                _ => SslProtocols.Tls12 | SslProtocols.Tls13
            };
#pragma warning restore SYSLIB0039
        }

        public bool ValidateServerCertificate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
        {
            if (O.Insecure)
            {
                if (errors != SslPolicyErrors.None)
                    Progress.Info(Phase, $"サーバー証明書検証警告: {errors} (-insecure オプションにより無視)");
                return true;
            }

            if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) && O.NoNameCheck)
            {
                Progress.Info(Phase, "サーバー証明書ホスト名不一致を無視しました (-nonamecheck)");
                errors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
            }

            if (CaCerts.Count > 0 && chain != null)
            {
                chain.ChainPolicy.ExtraStore.AddRange(CaCerts.ToArray());
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                bool ok = chain.Build((X509Certificate2)cert!);
                if (ok)
                {
                    var root = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                    foreach (var ca in CaCerts)
                    {
                        if (root.Thumbprint.Equals(ca.Thumbprint, StringComparison.OrdinalIgnoreCase))
                        {
                            errors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
                            break;
                        }
                    }
                }
            }

            return errors == SslPolicyErrors.None;
        }

        public X509Certificate? SelectClientCertificate(object sender, string targetHost, X509CertificateCollection localCerts, X509Certificate? remoteCert, string[] acceptableIssuers)
        {
            return ClientCert;
        }
    }
}

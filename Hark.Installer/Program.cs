using System.Security.Cryptography.X509Certificates;

namespace Hark.Installer;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Silent cert-only mode: relaunched elevated by the main instance to import the cert.
        if (args.Length > 0 && args[0] == "--cert-only")
        {
            Environment.Exit(InstallCertificate());
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }

    /// <summary>Imports the embedded signing cert into LocalMachine\TrustedPeople (and Root). Runs elevated.</summary>
    /// <returns>0 on success, 1 on failure.</returns>
    internal static int InstallCertificate()
    {
        try
        {
            var cert = LoadEmbeddedCert();
            if (cert is null) return 1;

            foreach (var name in new[] { StoreName.TrustedPeople, StoreName.Root })
            {
                using var store = new X509Store(name, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                store.Add(cert);
                store.Close();
            }
            return 0;
        }
        catch { return 1; }
    }

    /// <summary>Loads the public signing cert embedded in this exe, or <see langword="null"/> if missing.</summary>
    internal static X509Certificate2? LoadEmbeddedCert()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Hark.cer");
        if (stream is null) return null;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return X509CertificateLoader.LoadCertificate(bytes);
    }
}

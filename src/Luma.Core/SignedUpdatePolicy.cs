using System.Security.Cryptography;
using System.Text;
namespace Luma.Core;
public static class SignedUpdatePolicy
{
    private static string Field(string value) => value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\\n", StringComparison.Ordinal);
    public static string CanonicalPayload(string version,string channel,string packageKey,string sha256,long packageSize,bool mandatory,string minimumVersion,string title,string notes) => string.Join("\n",Field(version),Field(channel),Field(packageKey),sha256.Trim().ToLowerInvariant(),packageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),mandatory?"true":"false",Field(minimumVersion),Field(title),Field(notes));
    public static bool Verify(string pem,string signature,string version,string channel,string packageKey,string sha256,long packageSize,bool mandatory,string minimumVersion,string title,string notes)
    {
        if(string.IsNullOrWhiteSpace(pem)||string.IsNullOrWhiteSpace(signature))return false;
        try{using var key=ECDsa.Create();key.ImportFromPem(pem);var payload=Encoding.UTF8.GetBytes(CanonicalPayload(version,channel,packageKey,sha256,packageSize,mandatory,minimumVersion,title,notes));return key.VerifyData(payload,Convert.FromBase64String(signature),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence);}catch(CryptographicException){return false;}catch(FormatException){return false;}
    }
}

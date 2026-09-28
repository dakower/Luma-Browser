using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;
namespace Luma;
public sealed class ImportedCredential { public string Origin { get; set; }=""; public string Username { get; set; }=""; public string Password { get; set; }=""; }
internal sealed class VaultPayload { public List<ImportedCredential> Credentials { get; set; }=[]; public Dictionary<string,string> Autofill { get; set; }=new(StringComparer.OrdinalIgnoreCase); }
public static class CredentialVault
{
 private static readonly object Gate=new(); private static string FilePath=>Path.Combine(LumaState.DirectoryPath,"vault.dat");
 public static int ImportCredentials(IEnumerable<ImportedCredential> source){lock(Gate){var v=Load();var n=0;foreach(var item in source.Where(x=>!string.IsNullOrWhiteSpace(x.Origin)&&!string.IsNullOrEmpty(x.Password))){var old=v.Credentials.FirstOrDefault(x=>string.Equals(x.Origin,item.Origin,StringComparison.OrdinalIgnoreCase)&&x.Username==item.Username);if(old is null){v.Credentials.Add(item);n++;}else old.Password=item.Password;}Save(v);return n;}}
 public static int ImportAutofill(IEnumerable<KeyValuePair<string,string>> source){lock(Gate){var v=Load();var n=0;foreach(var pair in source.Where(x=>!string.IsNullOrWhiteSpace(x.Key)&&!string.IsNullOrWhiteSpace(x.Value))){v.Autofill[pair.Key]=pair.Value;n++;}Save(v);return n;}}
 // Chrome's App-Bound Encryption (Chrome 127+) blocks other apps from reading passwords/cookies
 // straight out of its database — that is Chrome's own security boundary and Luma does not try
 // to defeat it. The supported, Google-sanctioned migration path is Chrome's own CSV export
 // (chrome://password-manager/settings → "Export passwords"), which this reads back in.
 public static int ImportCredentialsFromCsv(string csv, out string? warning)
 {
     warning = null;
     if (string.IsNullOrWhiteSpace(csv)) { warning = "Файл пуст."; return 0; }
     var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => l.Length > 0).ToList();
     if (lines.Count == 0) { warning = "Файл пуст."; return 0; }
     var header = SplitCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
     int urlIdx = header.IndexOf("url"), userIdx = header.IndexOf("username"), passIdx = header.IndexOf("password");
     if (urlIdx < 0 || userIdx < 0 || passIdx < 0) { warning = "Не распознан формат CSV. Нужны колонки url, username, password — это стандартный экспорт паролей Chrome, Edge или Firefox."; return 0; }
     var items = new List<ImportedCredential>();
     for (var i = 1; i < lines.Count; i++)
     {
         var cells = SplitCsvLine(lines[i]);
         if (cells.Count <= Math.Max(urlIdx, Math.Max(userIdx, passIdx))) continue;
         var origin = cells[urlIdx].Trim(); var user = cells[userIdx].Trim(); var pass = cells[passIdx];
         if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(pass)) continue;
         items.Add(new ImportedCredential { Origin = origin, Username = user, Password = pass });
     }
     if (items.Count == 0) { warning = "В файле не найдено ни одной записи с паролем."; return 0; }
     return ImportCredentials(items);
 }
 private static List<string> SplitCsvLine(string line)
 {
     var cells = new List<string>(); var current = new System.Text.StringBuilder(); var inQuotes = false;
     for (var i = 0; i < line.Length; i++)
     {
         var ch = line[i];
         if (inQuotes) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else inQuotes = false; } else current.Append(ch); }
         else if (ch == '"') inQuotes = true;
         else if (ch == ',') { cells.Add(current.ToString()); current.Clear(); }
         else current.Append(ch);
     }
     cells.Add(current.ToString());
     return cells;
 }
 public static async Task ApplyAsync(WebView2 view)
 {
  if(view.CoreWebView2 is null||!Uri.TryCreate(view.Source?.ToString(),UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https"))return;
  VaultPayload v;lock(Gate)v=Load();var credentials=v.Credentials.Where(x=>Uri.TryCreate(x.Origin,UriKind.Absolute,out var u)&&string.Equals(u.Host,uri.Host,StringComparison.OrdinalIgnoreCase)).Take(4).Select(x=>new{username=x.Username,password=x.Password}).ToList();if(credentials.Count==0&&v.Autofill.Count==0)return;
  var cj=JsonSerializer.Serialize(credentials);var aj=JsonSerializer.Serialize(v.Autofill);
  var script="(()=>{const cs="+cj+",vals="+aj+";const fire=e=>{e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}))};const fill=()=>{const p=document.querySelector('input[type=password]');if(p&&cs.length){const c=cs[0],u=document.querySelector('input[type=email],input[autocomplete=username],input[name*=user i],input[name*=login i],input[id*=email i]');if(u&&!u.value&&c.username){u.value=c.username;fire(u)}if(!p.value&&c.password){p.value=c.password;fire(p)}}document.querySelectorAll('input:not([type=password]):not([type=hidden])').forEach(i=>{if(i.value)return;const keys=[i.autocomplete,i.name,i.id].filter(Boolean).map(x=>String(x).toLowerCase());for(const k of keys){const f=Object.entries(vals).find(([n])=>n.toLowerCase()===k);if(f){i.value=f[1];fire(i);break}}})};fill();setTimeout(fill,350);setTimeout(fill,1100)})()";
  try{await view.CoreWebView2.ExecuteScriptAsync(script);}catch{}
 }
 private static VaultPayload Load(){try{if(!File.Exists(FilePath))return new();var enc=File.ReadAllBytes(FilePath);var clear=ProtectedData.Unprotect(enc,null,DataProtectionScope.CurrentUser);var v=JsonSerializer.Deserialize<VaultPayload>(clear)??new();v.Credentials??=[];v.Autofill=new(v.Autofill??[],StringComparer.OrdinalIgnoreCase);CryptographicOperations.ZeroMemory(clear);return v;}catch{return new();}}
 private static void Save(VaultPayload v){Directory.CreateDirectory(LumaState.DirectoryPath);var clear=JsonSerializer.SerializeToUtf8Bytes(v);File.WriteAllBytes(FilePath,ProtectedData.Protect(clear,null,DataProtectionScope.CurrentUser));CryptographicOperations.ZeroMemory(clear);}
}

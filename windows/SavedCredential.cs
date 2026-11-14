using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal static class SavedCredential
{
    internal static string Read(string path)
    {
        try
        {
            if (File.Exists(path))
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(path)), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception error) { ErrorLog.Write("Kontoverbindung", "Gespeicherte Verbindung konnte nicht gelesen werden.", error); }
        return null;
    }

    internal static void Write(string path, string data, Action<bool> saved)
    {
        try
        {
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(data), null, DataProtectionScope.CurrentUser);
            ChatReplies.Persist(path, Convert.ToBase64String(encrypted), saved);
        }
        catch (CryptographicException error)
        {
            ErrorLog.Write("Kontoverbindung", "Verbindung konnte nicht verschlüsselt werden.", error);
            saved(false);
        }
    }
}

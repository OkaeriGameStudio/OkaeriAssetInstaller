using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace Okaeri.Editor
{
    /// <summary>
    /// Class responsible for updating and launching the Okaeri Asset Installer window.
    /// </summary>
    public class OkaeriAssetInstallerLauncher : EditorWindow
    {
        /// <summary>
        /// The default Okaeri Asset Installer version.
        /// </summary>
        private static readonly Version DEFAULT_VERSION = new Version(0, 0, 0);

        /// <summary>
        /// The link to the Okaeri Discord server.
        /// </summary>
        private const string OKAERI_DISCORD_URL = "https://discord.gg/m69Ex9kCPN";

        /// <summary>
        /// The Okaeri Asset Installer file name.
        /// </summary>
        private const string INSTALLER_FILE_NAME = "OkaeriAssetInstaller.cs";

        /// <summary>
        /// Window title.
        /// </summary>
        private const string WINDOW_TITLE = "Okaeri Asset Installer";

        /// <summary>
        /// The Okaeri Asset Installer window width.
        /// </summary>
        private const int WINDOW_WIDTH = 360;

        /// <summary>
        /// The Okaeri Asset Installer window height.
        /// </summary>
        private const int WINDOW_HEIGHT = 500;

        /// <summary>
        /// The reference to the currently open Okaeri Asset Installer window.
        /// </summary>
        private static EditorWindow m_installerWindow;

        /// <summary>
        /// The reference to the Okaeri Asset Installer launcher window.
        /// </summary>
        private static OkaeriAssetInstallerLauncher m_launcherWindow;

        /// <summary>
        /// Okaeri Asset Installer Unity menu entry.
        /// </summary>
        [MenuItem("Okaeri/Asset Installer")]
        public static void ShowWindow()
        {
            m_launcherWindow = GetWindow<OkaeriAssetInstallerLauncher>();

            const int height = WINDOW_WIDTH / 4;
            var x = (Screen.currentResolution.width - WINDOW_WIDTH) / 2;
            var y = (Screen.currentResolution.height - height) / 2;
            m_launcherWindow.position = new Rect(x, y, WINDOW_WIDTH, height);
            m_launcherWindow.titleContent = new GUIContent($"{WINDOW_TITLE} Launcher");
            m_launcherWindow.Show();
        }

        /// <summary>
        /// The path to the Okaeri Asset Installer script path.
        /// </summary>
        private string m_installerScriptPath;

        /// <summary>
        /// The current Okaeri Asset Installer version.
        /// </summary>
        private Version m_installerVersion = DEFAULT_VERSION;

        /// <summary>
        /// The latest Okaeri Asset Installer version.
        /// </summary>
        private Version m_latestInstallerVersion;

        /// <summary>
        /// The Okaeri Asset Installer launcher status text.
        /// </summary>
        private string m_launcherStatus;

        /// <summary>
        /// Determines if the window needs to be closed.
        /// </summary>
        private bool m_closeWindow;

        /// <summary>
        /// Handles the window initialization.
        /// </summary>
        private void OnEnable()
        {
            GetInstallerScriptPath();
            if (CheckForDependencies())
            {
                CheckForUpdates();
                return;
            }

            m_closeWindow = true;
        }

        /// <summary>
        /// Handles the window drawing.
        /// </summary>
        private void OnGUI()
        {
            // Check if we close the window
            if (m_closeWindow)
            {
                Close();
                return;
            }

            // Check if the installer window is shown
            if (m_installerWindow != null)
            {
                Close();
                return;
            }

            // Display any messages
            var isError = false;
            var status = string.Empty;
            if (!string.IsNullOrWhiteSpace(m_launcherStatus))
            {
                isError = m_launcherStatus.StartsWith("e!");
                status = m_launcherStatus.Substring(isError ? 2 : 0);
            }
            EditorGUILayout.HelpBox(status, isError ? MessageType.Error : MessageType.Info, true);

            // Display the versions
            GUI.enabled = false;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Current version:");
            GUILayout.TextField(m_installerVersion.ToString(), GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Latest version:");
            GUILayout.TextField(m_latestInstallerVersion?.ToString(), GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        /// <summary>
        /// Gets the path to the Okaeri Asset Installer script file.
        /// </summary>
        private void GetInstallerScriptPath()
        {
            var launcherPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            string errorMessage;
            if (string.IsNullOrWhiteSpace(launcherPath))
            {
                errorMessage = "Could not get the launcher script path.";
                m_launcherStatus = "e!" + errorMessage;
                throw new InvalidOperationException(errorMessage);
            }

            var launcherDirectory = Path.GetDirectoryName(launcherPath);
            if (string.IsNullOrWhiteSpace(launcherDirectory))
            {
                errorMessage = "Could not get the launcher script directory.";
                m_launcherStatus = "e!" + errorMessage;
                throw new InvalidOperationException(errorMessage);
            }

            m_installerScriptPath = Path.Combine(launcherDirectory, INSTALLER_FILE_NAME);
        }

        /// <summary>
        /// Checks for Okaeri Asset Installer dependencies
        /// </summary>
        private bool CheckForDependencies()
        {
            // Check for VRLabs AV3 Manager
            var vpmManifest = "Packages/vpm-manifest.json";
            if (File.Exists(vpmManifest))
            {
                var vpmPackages = File.ReadAllText(vpmManifest);
                if (!vpmPackages.Contains("dev.vrlabs.av3manager"))
                {
                    EditorUtility.DisplayDialog("Okaeri Asset Installer Dependencies",
                        "The Okaeri Asset Installer requires VRLabs AV3 Manager package to be installed.\n\nPlease install the VRLabs AV3 Manager package and try again!",
                        "OK");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets the SemVer version of the Okaeri Asset Installer script provided.
        /// </summary>
        /// <param name="installer">The Okaeri Asset Installer script content.</param>
        /// <returns>A Version</returns>
        private Version getInstallerVersion(string installer)
        {
            // Check the given installer code
            if (string.IsNullOrWhiteSpace(installer))
            {
                return DEFAULT_VERSION;
            }

            // Search for the first using statement
            int versionEndIndex = installer.IndexOf("using") - 1;

            // Get the version comment and extract the SemVer version
            string versionText = installer.Substring(0, versionEndIndex).Replace("//VERSION", "");

            // Parse and return the version
            return Version.Parse(versionText);
        }

        /// <summary>
        /// Checks for updates to the Okaeri Asset Installer.
        /// </summary>
        private async void CheckForUpdates()
        {
            
            m_launcherStatus = "Checking for updates";

            // Check if the window is currently installed
            var installerScript = AssetDatabase.LoadAssetAtPath<TextAsset>(m_installerScriptPath);
            if (installerScript != null && !string.IsNullOrWhiteSpace(installerScript.text))
            {
                m_launcherStatus = "Getting current version";
                m_installerVersion = getInstallerVersion(installerScript.text);
            }

            // Try to update the script
            try
            {
                // Get the latest installer
                m_launcherStatus = "Getting latest version";
                var latestInstallerScriptText = await GetLatestInstaller();
                if (string.IsNullOrWhiteSpace(latestInstallerScriptText))
                {
                    var errorMessage = "Could not get the latest installer: Empty or invalid installer content.";
                    m_launcherStatus = "e!" + errorMessage;
                    throw new InvalidOperationException(errorMessage);
                }

                // Get the latest installer version
                m_latestInstallerVersion = getInstallerVersion(latestInstallerScriptText);

                // Check if we need to update
                if (m_installerVersion >= m_latestInstallerVersion)
                {
                    ShowInstaller();
                    return;
                }

                // Update the asset
                m_launcherStatus = "Installing latest version";
                AssetDatabase.DeleteAsset(m_installerScriptPath);
                File.WriteAllText(m_installerScriptPath, latestInstallerScriptText);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();

                // Wait for code compilation
                await Task.Run(() => {
                    while(EditorApplication.isCompiling)
                    {
                        Thread.Sleep(1000);
                    }
                });
            }
            catch
            {
                // Logs if the server is dead or no internet
                m_launcherStatus = "Couldn't connect to the server!";
                if (!m_installerVersion.Equals(DEFAULT_VERSION))
                {
                    Debug.LogWarning("[Okaeri Asset Installer] Couldn't connect to the server! Proceeding without installer updates... Version: " + m_installerVersion);
                }
            }

            // Show the installer
            ShowInstaller();
        }

        /// <summary>
        /// Gets the latest version of the Okaeri Asset Installer.
        /// </summary>
        /// <returns>The latest version of the Okaeri Asset Installer.</returns>
        private async Task<string> GetLatestInstaller()
        {
            using (var client = new HttpClient())
            {
                var stringContents = await client.GetStringAsync("https://rammyvps.vps.webdock.cloud/api/installer");
                return stringContents.Replace("\n", Environment.NewLine);
            }
        }

        /// <summary>
        /// Shows the Okaeri Asset Installer window.
        /// </summary>
        private void ShowInstaller()
        {
            // Check if the script is present
            if (!File.Exists(m_installerScriptPath))
            {
                string errorMessage = "Could not find the Okaeri Asset Installer script file. Try to reopen the Okaeri Asset Installer window and try again.";
                errorMessage += Environment.NewLine + Environment.NewLine + "If the problem persists, please contact us on Discord!";
                if(!EditorUtility.DisplayDialog("Okaeri Asset Installer Error", errorMessage, "Ok", "Discord"))
                {
                    System.Diagnostics.Process.Start(OKAERI_DISCORD_URL);
                }

                m_closeWindow = true;
                return;
            }

            // Prepare the window position
            var windowX = (Screen.currentResolution.width - WINDOW_WIDTH) / 2;
            var windowY = (Screen.currentResolution.height - WINDOW_HEIGHT) / 2;

            // Instantiate the window
            m_installerWindow = CreateInstance("OkaeriAssetInstaller") as EditorWindow;
            if (m_installerWindow != null)
            {
                m_installerWindow.titleContent = new GUIContent(WINDOW_TITLE);
                m_installerWindow.position = new Rect(windowX, windowY, WINDOW_WIDTH, WINDOW_HEIGHT);
                m_installerWindow.Show();
            }
        }
    }
}
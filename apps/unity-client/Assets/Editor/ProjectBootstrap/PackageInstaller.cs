using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Orbis.Editor
{
    public static class PackageInstaller
    {
        static readonly string[] PackagesToAdd =
        {
            "com.unity.localization",
            "com.unity.test-framework",
        };

        const double TimeoutSeconds = 600;
        static AddAndRemoveRequest request;
        static double deadline;

        public static void Install()
        {
            request = Client.AddAndRemove(PackagesToAdd, new string[0]);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (request == null || !request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup <= deadline) return;
                EditorApplication.update -= Poll;
                Debug.LogError("[Orbis] Package installation timed out.");
                EditorApplication.Exit(2);
                return;
            }

            EditorApplication.update -= Poll;
            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[Orbis] Packages resolved: {string.Join(", ", request.Result.Select(package => $"{package.name}@{package.version}"))}");
                EditorApplication.Exit(0);
                return;
            }

            Debug.LogError($"[Orbis] Package installation failed: {request.Error?.message}");
            EditorApplication.Exit(1);
        }
    }
}

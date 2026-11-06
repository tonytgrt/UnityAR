using Unity.XR.XREAL;
using UnityEngine;

namespace UnityAR
{
    /// <summary>
    /// Restarts the app, which clears everything as if the user had closed it and opened it again.
    /// </summary>
    /// <remarks>
    /// XREAL can't reset its tracking or clear its environment mesh while running, so a full restart is the only
    /// way to start over. The app quits the way XREAL's own home menu quits it, and <c>RestartActivity</c>, from the
    /// <c>AppRestart.androidlib</c> plugin, opens it again once its process has ended.
    /// </remarks>
    public static class AppRestart
    {
        /// <summary>
        /// Quits the app and opens it again. Does nothing outside an Android player.
        /// </summary>
        public static void Restart()
        {
            Debug.Log("[AppRestart] Restarting");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var restartActivity = new AndroidJavaClass("com.unityar.restart.RestartActivity"))
                restartActivity.CallStatic("restart", XREALUtility.UnityActivity);
            XREALPlugin.QuitApplication();
#endif
        }
    }
}

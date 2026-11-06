package com.unityar.restart;

import android.app.Activity;
import android.app.ActivityOptions;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Process;
import android.os.SystemClock;
import android.view.Display;

import java.io.File;

/**
 * Restarts the app as if it had been closed and opened again from the launcher.
 *
 * <p>The app starts this activity, which runs in its own process, and then quits. This activity waits for the
 * app's process to end, ending it after a timeout, then launches the app again in a new process.</p>
 */
public class RestartActivity extends Activity {
    static final String EXTRA_PID = "pid";
    static final String EXTRA_LAUNCH_INTENT = "launchIntent";

    static final long POLL_INTERVAL_MS = 100;

    // How long the app gets to quit by itself before it is ended.
    static final long QUIT_TIMEOUT_MS = 4000;

    // How long to wait for the app's process to go away after ending it.
    static final long KILL_TIMEOUT_MS = 2000;

    /**
     * Starts the restart. Call it from the app's process, then quit the app.
     *
     * @param activity A running activity of the app.
     */
    public static void restart(Activity activity) {
        Intent launchIntent = activity.getPackageManager().getLaunchIntentForPackage(activity.getPackageName());
        Intent intent = new Intent(activity, RestartActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                .putExtra(EXTRA_PID, Process.myPid())
                .putExtra(EXTRA_LAUNCH_INTENT, launchIntent);

        // Open on the phone's screen, where the launcher opens the app, rather than on the glasses.
        ActivityOptions options = ActivityOptions.makeBasic().setLaunchDisplayId(Display.DEFAULT_DISPLAY);
        activity.startActivity(intent, options.toBundle());
    }

    final Handler m_Handler = new Handler(Looper.getMainLooper());
    int m_Pid;
    Intent m_LaunchIntent;
    long m_StartTime;
    long m_KillTime = -1;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        m_Pid = getIntent().getIntExtra(EXTRA_PID, -1);
        m_LaunchIntent = getIntent().getParcelableExtra(EXTRA_LAUNCH_INTENT, Intent.class);
        m_StartTime = SystemClock.uptimeMillis();
        m_Handler.post(this::waitForAppToQuit);
    }

    void waitForAppToQuit() {
        long now = SystemClock.uptimeMillis();
        boolean running = m_Pid > 0 && new File("/proc/" + m_Pid).exists();
        if (running && m_KillTime < 0 && now - m_StartTime >= QUIT_TIMEOUT_MS) {
            Process.killProcess(m_Pid);
            m_KillTime = now;
        }

        if (running && (m_KillTime < 0 || now - m_KillTime < KILL_TIMEOUT_MS)) {
            m_Handler.postDelayed(this::waitForAppToQuit, POLL_INTERVAL_MS);
            return;
        }

        if (m_LaunchIntent != null) {
            m_LaunchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
            startActivity(m_LaunchIntent);
        }
        finishAndRemoveTask();
    }
}
